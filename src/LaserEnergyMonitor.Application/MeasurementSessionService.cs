using System;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Application
{
    public sealed class MeasurementSessionService : IDisposable
    {
        private readonly IMeasurementSource _firstSource;
        private readonly IMeasurementSource _secondSource;
        private readonly IStationarityDetector _detector;
        private readonly IMeasurementExporter _exporter;
        private readonly IApplicationLogger _logger;
        private readonly IOperatorNotifier _notifier;
        private readonly IClock _clock;

        private MeasurementSessionState _state;
        private SessionMetadata _metadata;
        private int _sampleCount;
        private int _eventCount;
        private int _faultCount;
        private int _stationarySegmentCount;
        private int _closedStationarySegmentCount;
        private DateTime? _lastFaultUtc;
        private SessionSettings _currentSettings;
        private StationarySegmentResult _activeStationarySegment;
        private MeasurementRecord _lastRecord;
        private StationarityUpdate _lastUpdate;
        private double? _lastFirstEnergy;
        private double? _lastSecondEnergy;
        private string _terminationReasonCode;
        private string _terminationReason;
        private bool _sessionStarted;
        private bool _sessionFinalized;
        private bool _disposing;
        private bool _disposed;

        public MeasurementSessionService(
            IMeasurementSource firstSource,
            IMeasurementSource secondSource,
            IStationarityDetector detector,
            IMeasurementExporter exporter,
            IApplicationLogger logger,
            IOperatorNotifier notifier,
            IClock clock)
        {
            _firstSource = firstSource ?? throw new ArgumentNullException("firstSource");
            _secondSource = secondSource ?? throw new ArgumentNullException("secondSource");
            _detector = detector ?? throw new ArgumentNullException("detector");
            _exporter = exporter ?? throw new ArgumentNullException("exporter");
            _logger = logger ?? throw new ArgumentNullException("logger");
            _notifier = notifier ?? throw new ArgumentNullException("notifier");
            _clock = clock ?? throw new ArgumentNullException("clock");
            _state = MeasurementSessionState.Idle;
            _sessionFinalized = true;

            _firstSource.MeasurementReceived += OnMeasurementReceived;
            _secondSource.MeasurementReceived += OnMeasurementReceived;
            _firstSource.Faulted += OnFaulted;
            _secondSource.Faulted += OnFaulted;
        }

        public event EventHandler<SessionStateChangedEventArgs> StateChanged;
        public event EventHandler<LiveMeasurementUpdatedEventArgs> LiveMeasurementUpdated;
        public event EventHandler<SessionEventRaisedEventArgs> SessionEventRaised;
        public event EventHandler<StationarySegmentRecordedEventArgs> StationarySegmentRecorded;
        public event EventHandler<SessionSummaryAvailableEventArgs> SessionSummaryAvailable;

        public MeasurementSessionState State
        {
            get { return _state; }
        }

        public void Initialize(SessionSettings settings)
        {
            ThrowIfDisposed();

            if (_state == MeasurementSessionState.Measuring || _state == MeasurementSessionState.Stationary)
            {
                throw new InvalidOperationException("Cannot reinitialize while a measurement session is running.");
            }

            SessionSettings validatedSettings = SessionSettingsValidator.NormalizeAndValidate(settings);
            ResetSessionRuntimeState();

            _logger.Info("Initializing measurement sources.");
            _firstSource.Initialize();
            _secondSource.Initialize();
            EnsureSourcesReady("initialize");
            _detector.Configure(validatedSettings, _firstSource.SourceId, _secondSource.SourceId);

            _currentSettings = validatedSettings;
            TransitionTo(MeasurementSessionState.Initialized);
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (_state != MeasurementSessionState.Initialized)
            {
                throw new InvalidOperationException("Session must be initialized before start.");
            }

            if (_currentSettings == null)
            {
                throw new InvalidOperationException("Session settings were not initialized.");
            }

            EnsureSourcesReady("start");
            ResetProcessingBuffers();
            _sessionStarted = true;
            _sessionFinalized = false;
            _metadata = new SessionMetadata
            {
                SessionName = _currentSettings.SessionName,
                StartedUtc = _clock.UtcNow,
                FirstSourceId = _firstSource.SourceId,
                SecondSourceId = _secondSource.SourceId
            };

            try
            {
                _exporter.StartSession(_metadata, _currentSettings);
                RaiseEvent(SessionEventType.SessionStarted, "Measurement session started.", null, null, "session-started");
                TransitionTo(MeasurementSessionState.Measuring);
                _firstSource.Start();
                _secondSource.Start();
            }
            catch (Exception ex)
            {
                HandleStartupFailure(ex);
                throw;
            }
        }

        public void Stop()
        {
            ThrowIfDisposed();

            if (!_sessionStarted || _sessionFinalized)
            {
                return;
            }

            if (_state != MeasurementSessionState.Measuring &&
                _state != MeasurementSessionState.Stationary &&
                _state != MeasurementSessionState.Faulted)
            {
                return;
            }

            StopSourcesSafely();

            if (_state != MeasurementSessionState.Faulted)
            {
                string message = "Measurement session stopped by the operator.";
                RaiseEvent(SessionEventType.SessionStopped, message, null, null, "manual-stop");
                CompleteSession(true, MeasurementSessionState.Completed.ToString(), "manual-stop", message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposing = true;

            try
            {
                if (_sessionStarted && !_sessionFinalized)
                {
                    StopSourcesSafely();
                    AbortSession(
                        "Measurement session was interrupted because the session service was disposed.",
                        "Disposed",
                        "service-disposed");
                }
                else
                {
                    StopSourcesSafely();
                }
            }
            finally
            {
                _firstSource.MeasurementReceived -= OnMeasurementReceived;
                _secondSource.MeasurementReceived -= OnMeasurementReceived;
                _firstSource.Faulted -= OnFaulted;
                _secondSource.Faulted -= OnFaulted;

                _exporter.Dispose();
                IDisposable disposableLogger = _logger as IDisposable;
                if (disposableLogger != null)
                {
                    disposableLogger.Dispose();
                }

                _firstSource.Dispose();
                _secondSource.Dispose();
                _disposed = true;
                _disposing = false;
            }
        }

        private void OnMeasurementReceived(object sender, MeasurementReceivedEventArgs e)
        {
            if (!IsAcceptingMeasurements())
            {
                return;
            }

            try
            {
                ProcessMeasurement(e.Sample);
            }
            catch (Exception ex)
            {
                HandlePipelineException("Measurement processing failed.", e != null ? e.Sample : null, ex);
            }
        }

        private void ProcessMeasurement(MeasurementSample sample)
        {
            if (!IsAcceptingMeasurements())
            {
                return;
            }

            try
            {
                _sampleCount += 1;
                bool isFirstSource = string.Equals(sample.SourceId, _firstSource.SourceId, StringComparison.OrdinalIgnoreCase);
                bool isSecondSource = string.Equals(sample.SourceId, _secondSource.SourceId, StringComparison.OrdinalIgnoreCase);
                MeasurementRecord record = new MeasurementRecord
                {
                    RecordId = _sampleCount,
                    Sample = sample,
                    IsFirstSource = isFirstSource,
                    IsSecondSource = isSecondSource
                };

                if (isFirstSource)
                {
                    _lastFirstEnergy = sample.Energy;
                }
                else if (isSecondSource)
                {
                    _lastSecondEnergy = sample.Energy;
                }

                StationarityUpdate update = _detector.Evaluate(sample);
                _lastRecord = record;
                _lastUpdate = update;
                _exporter.WriteMeasurement(record, update);

                if (update.EnteredStationaryState)
                {
                    OpenStationarySegment(record, update);
                    RaiseEvent(SessionEventType.StationaryEntered, "Stationary mode detected.", record.RecordId, update.StabilityMetric, "stationary-entered");
                    TransitionTo(MeasurementSessionState.Stationary);
                }
                else if (update.ExitedStationaryState)
                {
                    CloseActiveStationarySegment(record, update, "Stationary mode lost.");
                    RaiseEvent(SessionEventType.StationaryExited, "Stationary mode lost.", record.RecordId, update.StabilityMetric, "stationary-exited");
                    TransitionTo(MeasurementSessionState.Measuring);
                }

                LiveMeasurementUpdated?.Invoke(
                    this,
                    new LiveMeasurementUpdatedEventArgs(
                        new LiveMeasurementSnapshot
                        {
                            SessionState = _state,
                            RecordId = record.RecordId,
                            FirstEnergy = _lastFirstEnergy,
                            SecondEnergy = _lastSecondEnergy,
                            FirstAverage = update.RollingAverageFirst,
                            SecondAverage = update.RollingAverageSecond,
                            FirstStabilityMetric = update.FirstStabilityMetric,
                            SecondStabilityMetric = update.SecondStabilityMetric,
                            StabilityMetric = update.StabilityMetric,
                            IsFirstSourceStationary = update.IsFirstSourceStationary,
                            IsSecondSourceStationary = update.IsSecondSourceStationary,
                            IsStationary = update.IsStationary,
                            TimestampUtc = _clock.UtcNow
                        }));
            }
            catch (Exception ex)
            {
                HandlePipelineException("Measurement processing failed.", sample, ex);
            }
        }

        private void OnFaulted(object sender, DeviceFaultEventArgs e)
        {
            if (_disposed || _disposing || _sessionFinalized)
            {
                return;
            }

            DeviceFault fault = e != null ? e.Fault : null;
            string sourceId = fault != null && !string.IsNullOrWhiteSpace(fault.SourceId)
                ? fault.SourceId
                : "unknown source";
            string detail = fault != null && !string.IsNullOrWhiteSpace(fault.Message)
                ? fault.Message
                : "No additional fault details were provided.";
            string message = string.Format("Critical fault on {0}: {1}", sourceId, detail);

            _logger.Error(message);
            _notifier.ShowCritical(message);
            _faultCount += 1;
            _lastFaultUtc = _clock.UtcNow;

            try
            {
                RaiseEvent(
                    SessionEventType.Fault,
                    message,
                    null,
                    null,
                    fault != null && !string.IsNullOrWhiteSpace(fault.ReasonCode) ? fault.ReasonCode : "critical-fault");
            }
            catch
            {
            }

            TransitionTo(MeasurementSessionState.Faulted);
            StopSourcesSafely();
            AbortSession(
                message,
                MeasurementSessionState.Faulted.ToString(),
                fault != null && !string.IsNullOrWhiteSpace(fault.ReasonCode) ? fault.ReasonCode : "critical-fault");
        }

        private void HandleStartupFailure(Exception ex)
        {
            string message = "Measurement session failed to start: " + ex.Message;
            _logger.Error(message);
            StopSourcesSafely();
            AbortSession(message, MeasurementSessionState.Faulted.ToString(), "startup-failure");
            TransitionTo(MeasurementSessionState.Faulted);
        }

        private void HandlePipelineException(string contextMessage, MeasurementSample sample, Exception ex)
        {
            string sourceId = sample != null ? sample.SourceId : "pipeline";
            OnFaulted(
                this,
                new DeviceFaultEventArgs(
                    new DeviceFault
                    {
                        SourceId = sourceId,
                        Severity = FaultSeverity.Critical,
                        ReasonCode = "pipeline-failure",
                        Message = contextMessage + " " + ex.Message,
                        TimestampUtc = _clock.UtcNow,
                        Exception = ex
                    }));
        }

        private void RaiseEvent(SessionEventType eventType, string message, long? sequenceNumber, double? metricValue, string reasonCode)
        {
            SessionEvent sessionEvent = new SessionEvent
            {
                EventType = eventType,
                TimestampUtc = _clock.UtcNow,
                ReasonCode = reasonCode,
                Message = message,
                SequenceNumber = sequenceNumber,
                MetricValue = metricValue
            };

            _eventCount += 1;
            _exporter.WriteEvent(sessionEvent);
            SessionEventRaised?.Invoke(this, new SessionEventRaisedEventArgs(sessionEvent));
        }

        private void CompleteSession(
            bool completedNormally,
            string finalState,
            string terminationReasonCode,
            string terminationReason)
        {
            if (!_sessionStarted || _sessionFinalized)
            {
                return;
            }

            FinalizeActiveStationarySegment("Session completed.");
            _terminationReasonCode = terminationReasonCode;
            _terminationReason = terminationReason;
            SessionSummary summary = CreateSessionSummary(completedNormally, finalState);
            _sessionFinalized = true;
            _sessionStarted = false;
            _exporter.Complete(summary);
            SessionSummaryAvailable?.Invoke(this, new SessionSummaryAvailableEventArgs(summary));
            TransitionTo(MeasurementSessionState.Completed);
            ResetProcessingBuffers();
        }

        private void AbortSession(string reason, string finalState, string terminationReasonCode)
        {
            if (_sessionFinalized)
            {
                return;
            }

            FinalizeActiveStationarySegment(reason);
            _terminationReasonCode = terminationReasonCode;
            _terminationReason = reason;
            SessionSummary summary = CreateSessionSummary(false, finalState);
            _sessionFinalized = true;
            _sessionStarted = false;

            try
            {
                _exporter.Abort(summary, reason);
                SessionSummaryAvailable?.Invoke(this, new SessionSummaryAvailableEventArgs(summary));
            }
            finally
            {
                ResetProcessingBuffers();
            }
        }

        private void ResetSessionRuntimeState()
        {
            StopSourcesSafely();
            _metadata = null;
            _sessionStarted = false;
            _sessionFinalized = true;
            ResetProcessingBuffers();
        }

        private void ResetProcessingBuffers()
        {
            _sampleCount = 0;
            _eventCount = 0;
            _faultCount = 0;
            _stationarySegmentCount = 0;
            _closedStationarySegmentCount = 0;
            _metadata = null;
            _lastFaultUtc = null;
            _activeStationarySegment = null;
            _lastRecord = null;
            _lastUpdate = null;
            _lastFirstEnergy = null;
            _lastSecondEnergy = null;
            _terminationReasonCode = null;
            _terminationReason = null;
            _detector.Reset();
        }

        private bool IsAcceptingMeasurements()
        {
            return !_disposed &&
                _sessionStarted &&
                !_sessionFinalized &&
                (_state == MeasurementSessionState.Measuring || _state == MeasurementSessionState.Stationary);
        }

        private void StopSourcesSafely()
        {
            try
            {
                _firstSource.Stop();
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to stop first source cleanly: " + ex.Message);
            }

            try
            {
                _secondSource.Stop();
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to stop second source cleanly: " + ex.Message);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private void TransitionTo(MeasurementSessionState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke(this, new SessionStateChangedEventArgs(state));
        }

        private void EnsureSourcesReady(string operation)
        {
            bool firstReady = _firstSource.IsConnected;
            bool secondReady = _secondSource.IsConnected;
            if (firstReady && secondReady)
            {
                return;
            }

            string unavailableSources = BuildUnavailableSourcesList(firstReady, secondReady);
            string message;
            if (string.Equals(operation, "initialize", StringComparison.OrdinalIgnoreCase))
            {
                message = "Measurement sources were initialized, but not all acquisition paths are ready: " + unavailableSources + ".";
            }
            else
            {
                message = "Cannot start the measurement session because the following sources are not ready: " + unavailableSources + ".";
            }

            _logger.Warning(message);
            throw new InvalidOperationException(message);
        }

        private string BuildUnavailableSourcesList(bool firstReady, bool secondReady)
        {
            string unavailableSources = string.Empty;

            if (!firstReady)
            {
                unavailableSources = DescribeSource(_firstSource);
            }

            if (!secondReady)
            {
                if (unavailableSources.Length > 0)
                {
                    unavailableSources += ", ";
                }

                unavailableSources += DescribeSource(_secondSource);
            }

            return unavailableSources;
        }

        private static string DescribeSource(IMeasurementSource source)
        {
            if (source == null)
            {
                return "unknown source";
            }

            return string.IsNullOrWhiteSpace(source.SourceId)
                ? source.GetType().Name
                : source.SourceId;
        }

        private SessionSummary CreateSessionSummary(bool completedNormally, string finalState)
        {
            return new SessionSummary
            {
                StartedUtc = _metadata != null ? _metadata.StartedUtc : _clock.UtcNow,
                FinishedUtc = _clock.UtcNow,
                SampleCount = _sampleCount,
                EventCount = _eventCount,
                FaultCount = _faultCount,
                StationarySegmentCount = _stationarySegmentCount,
                ClosedStationarySegmentCount = _closedStationarySegmentCount,
                LastFaultUtc = _lastFaultUtc,
                CompletedNormally = completedNormally,
                FinalState = finalState,
                TerminationReasonCode = _terminationReasonCode,
                TerminationReason = _terminationReason
            };
        }

        private void OpenStationarySegment(MeasurementRecord record, StationarityUpdate update)
        {
            if (record == null || record.Sample == null || update == null)
            {
                return;
            }

            if (_activeStationarySegment != null)
            {
                FinalizeActiveStationarySegment("Superseded by a new stationary entry.");
            }

            _stationarySegmentCount += 1;
            _activeStationarySegment = new StationarySegmentResult
            {
                SegmentId = _stationarySegmentCount,
                EntryRecordId = record.RecordId,
                EntryTimestampUtc = record.Sample.TimestampUtc,
                EntryFirstEnergy = _lastFirstEnergy.GetValueOrDefault(),
                EntrySecondEnergy = _lastSecondEnergy.GetValueOrDefault(),
                EntryFirstAverage = update.RollingAverageFirst,
                EntrySecondAverage = update.RollingAverageSecond,
                EntryStabilityMetric = update.StabilityMetric
            };
        }

        private void CloseActiveStationarySegment(
            MeasurementRecord record,
            StationarityUpdate update,
            string exitReason)
        {
            if (_activeStationarySegment == null)
            {
                return;
            }

            if (record != null && record.Sample != null)
            {
                _activeStationarySegment.ExitRecordId = record.RecordId;
                _activeStationarySegment.ExitTimestampUtc = record.Sample.TimestampUtc;
            }
            else
            {
                _activeStationarySegment.ExitTimestampUtc = _clock.UtcNow;
            }

            if (update != null)
            {
                _activeStationarySegment.ExitStabilityMetric = update.StabilityMetric;
            }

            if (_activeStationarySegment.ExitTimestampUtc.HasValue)
            {
                _activeStationarySegment.DurationMs =
                    (_activeStationarySegment.ExitTimestampUtc.Value - _activeStationarySegment.EntryTimestampUtc).TotalMilliseconds;
            }

            _activeStationarySegment.ExitReason = exitReason;
            _exporter.WriteStationarySegment(_activeStationarySegment);
            StationarySegmentRecorded?.Invoke(
                this,
                new StationarySegmentRecordedEventArgs(CloneStationarySegment(_activeStationarySegment)));
            _closedStationarySegmentCount += 1;
            _activeStationarySegment = null;
        }

        private void FinalizeActiveStationarySegment(string exitReason)
        {
            CloseActiveStationarySegment(_lastRecord, _lastUpdate, exitReason);
        }

        private static StationarySegmentResult CloneStationarySegment(StationarySegmentResult segment)
        {
            if (segment == null)
            {
                return null;
            }

            return new StationarySegmentResult
            {
                SegmentId = segment.SegmentId,
                EntryRecordId = segment.EntryRecordId,
                EntryTimestampUtc = segment.EntryTimestampUtc,
                EntryFirstEnergy = segment.EntryFirstEnergy,
                EntrySecondEnergy = segment.EntrySecondEnergy,
                EntryFirstAverage = segment.EntryFirstAverage,
                EntrySecondAverage = segment.EntrySecondAverage,
                EntryStabilityMetric = segment.EntryStabilityMetric,
                ExitRecordId = segment.ExitRecordId,
                ExitTimestampUtc = segment.ExitTimestampUtc,
                ExitStabilityMetric = segment.ExitStabilityMetric,
                DurationMs = segment.DurationMs,
                ExitReason = segment.ExitReason
            };
        }
    }
}
