using System;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Application
{
    public sealed class MeasurementSessionService : IDisposable
    {
        private readonly IMeasurementSource _firstSource;
        private readonly IMeasurementSource _secondSource;
        private readonly IMeasurementSynchronizer _synchronizer;
        private readonly IStationarityDetector _detector;
        private readonly IMeasurementExporter _exporter;
        private readonly IApplicationLogger _logger;
        private readonly IOperatorNotifier _notifier;
        private readonly IClock _clock;

        private MeasurementSessionState _state;
        private SessionMetadata _metadata;
        private int _pairCount;
        private int _eventCount;
        private int _desynchronizationCount;
        private int _consecutiveDesynchronizationCount;
        private int _faultCount;
        private int _stationarySegmentCount;
        private int _closedStationarySegmentCount;
        private DateTime? _lastDesynchronizationUtc;
        private DateTime? _lastFaultUtc;
        private SessionSettings _currentSettings;
        private StationarySegmentResult _activeStationarySegment;
        private SynchronizedMeasurementPair _lastPair;
        private StationarityUpdate _lastUpdate;
        private string _terminationReasonCode;
        private string _terminationReason;
        private bool _sessionStarted;
        private bool _sessionFinalized;
        private bool _disposing;
        private bool _disposed;

        public MeasurementSessionService(
            IMeasurementSource firstSource,
            IMeasurementSource secondSource,
            IMeasurementSynchronizer synchronizer,
            IStationarityDetector detector,
            IMeasurementExporter exporter,
            IApplicationLogger logger,
            IOperatorNotifier notifier,
            IClock clock)
        {
            _firstSource = firstSource ?? throw new ArgumentNullException("firstSource");
            _secondSource = secondSource ?? throw new ArgumentNullException("secondSource");
            _synchronizer = synchronizer ?? throw new ArgumentNullException("synchronizer");
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
            _synchronizer.PairReady += OnPairReady;
            _synchronizer.Desynchronized += OnDesynchronized;
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
            _synchronizer.Configure(validatedSettings.SynchronizationDelta, _firstSource.SourceId, _secondSource.SourceId);
            _detector.Configure(validatedSettings);

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
                _firstSource.Start();
                _secondSource.Start();
                TransitionTo(MeasurementSessionState.Measuring);
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
                _synchronizer.PairReady -= OnPairReady;
                _synchronizer.Desynchronized -= OnDesynchronized;

                _exporter.Dispose();
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
                _synchronizer.Push(e.Sample);
            }
            catch (Exception ex)
            {
                HandlePipelineException("Synchronization pipeline failed.", e != null ? e.Sample : null, ex);
            }
        }

        private void OnPairReady(object sender, SynchronizedMeasurementPairEventArgs e)
        {
            if (!IsAcceptingMeasurements())
            {
                return;
            }

            try
            {
                StationarityUpdate update = _detector.Evaluate(e.Pair);
                _lastPair = e.Pair;
                _lastUpdate = update;
                _consecutiveDesynchronizationCount = 0;
                _pairCount += 1;
                _exporter.WriteMeasurement(e.Pair, update);

                if (update.EnteredStationaryState)
                {
                    OpenStationarySegment(e.Pair, update);
                    RaiseEvent(SessionEventType.StationaryEntered, "Stationary mode detected.", e.Pair.PairId, update.StabilityMetric, "stationary-entered");
                    TransitionTo(MeasurementSessionState.Stationary);
                }
                else if (update.ExitedStationaryState)
                {
                    CloseActiveStationarySegment(e.Pair, update, "Stationary mode lost.");
                    RaiseEvent(SessionEventType.StationaryExited, "Stationary mode lost.", e.Pair.PairId, update.StabilityMetric, "stationary-exited");
                    TransitionTo(MeasurementSessionState.Measuring);
                }

                LiveMeasurementUpdated?.Invoke(
                    this,
                    new LiveMeasurementUpdatedEventArgs(
                        new LiveMeasurementSnapshot
                        {
                            SessionState = _state,
                            PairId = e.Pair.PairId,
                            FirstEnergy = e.Pair.FirstSample.Energy,
                            SecondEnergy = e.Pair.SecondSample.Energy,
                            FirstAverage = update.RollingAverageFirst,
                            SecondAverage = update.RollingAverageSecond,
                            StabilityMetric = update.StabilityMetric,
                            IsStationary = update.IsStationary,
                            TimestampUtc = _clock.UtcNow
                        }));
            }
            catch (Exception ex)
            {
                HandlePipelineException("Measurement processing failed.", e != null ? e.Pair != null ? e.Pair.FirstSample : null : null, ex);
            }
        }

        private void OnDesynchronized(object sender, DesynchronizationEventArgs e)
        {
            if (!IsAcceptingMeasurements())
            {
                return;
            }

            try
            {
                _desynchronizationCount += 1;
                _consecutiveDesynchronizationCount += 1;
                _lastDesynchronizationUtc = _clock.UtcNow;
                RaiseEvent(SessionEventType.Desynchronized, "Desynchronization detected: " + e.Reason, e.Sample.SequenceNumber, null, "sample-unmatched");
                EnforceDesynchronizationPolicyIfNeeded();
            }
            catch (Exception ex)
            {
                HandlePipelineException("Desynchronization handling failed.", e != null ? e.Sample : null, ex);
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
            _pairCount = 0;
            _eventCount = 0;
            _desynchronizationCount = 0;
            _consecutiveDesynchronizationCount = 0;
            _faultCount = 0;
            _stationarySegmentCount = 0;
            _closedStationarySegmentCount = 0;
            _metadata = null;
            _lastDesynchronizationUtc = null;
            _lastFaultUtc = null;
            _activeStationarySegment = null;
            _lastPair = null;
            _lastUpdate = null;
            _terminationReasonCode = null;
            _terminationReason = null;
            _synchronizer.Reset();
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
                PairCount = _pairCount,
                EventCount = _eventCount,
                DesynchronizationCount = _desynchronizationCount,
                FaultCount = _faultCount,
                StationarySegmentCount = _stationarySegmentCount,
                ClosedStationarySegmentCount = _closedStationarySegmentCount,
                LastDesynchronizationUtc = _lastDesynchronizationUtc,
                LastFaultUtc = _lastFaultUtc,
                CompletedNormally = completedNormally,
                FinalState = finalState,
                TerminationReasonCode = _terminationReasonCode,
                TerminationReason = _terminationReason
            };
        }

        private void EnforceDesynchronizationPolicyIfNeeded()
        {
            int threshold = _currentSettings != null ? _currentSettings.MaxConsecutiveDesynchronizations : 0;
            if (threshold <= 0 || _consecutiveDesynchronizationCount < threshold)
            {
                return;
            }

            string message = string.Format(
                "Desynchronization threshold reached: {0} consecutive unmatched samples.",
                _consecutiveDesynchronizationCount);
            DesynchronizationPolicyAction action = _currentSettings != null
                ? _currentSettings.DesynchronizationPolicyAction
                : DesynchronizationPolicyAction.LogOnly;

            if (action == DesynchronizationPolicyAction.LogOnly)
            {
                return;
            }

            if (action == DesynchronizationPolicyAction.StopGracefully)
            {
                StopSourcesSafely();
                string stopMessage = message + " The session will stop gracefully.";
                RaiseEvent(
                    SessionEventType.SessionStopped,
                    stopMessage,
                    null,
                    _consecutiveDesynchronizationCount,
                    "desynchronization-threshold-graceful-stop");
                CompleteSession(false, MeasurementSessionState.Completed.ToString(), "desynchronization-threshold-graceful-stop", stopMessage);
                return;
            }

            OnFaulted(
                this,
                new DeviceFaultEventArgs(
                    new DeviceFault
                    {
                        SourceId = "Synchronizer",
                        Severity = FaultSeverity.Critical,
                        ReasonCode = "desynchronization-threshold-fault",
                        Message = message + " The session will be faulted.",
                        TimestampUtc = _clock.UtcNow
                    }));
        }

        private void OpenStationarySegment(SynchronizedMeasurementPair pair, StationarityUpdate update)
        {
            if (pair == null || update == null)
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
                EntryPairId = pair.PairId,
                EntryTimestampUtc = pair.FirstSample.TimestampUtc >= pair.SecondSample.TimestampUtc
                    ? pair.FirstSample.TimestampUtc
                    : pair.SecondSample.TimestampUtc,
                EntryFirstEnergy = pair.FirstSample.Energy,
                EntrySecondEnergy = pair.SecondSample.Energy,
                EntryFirstAverage = update.RollingAverageFirst,
                EntrySecondAverage = update.RollingAverageSecond,
                EntryStabilityMetric = update.StabilityMetric
            };
        }

        private void CloseActiveStationarySegment(
            SynchronizedMeasurementPair pair,
            StationarityUpdate update,
            string exitReason)
        {
            if (_activeStationarySegment == null)
            {
                return;
            }

            if (pair != null)
            {
                _activeStationarySegment.ExitPairId = pair.PairId;
                _activeStationarySegment.ExitTimestampUtc = pair.FirstSample.TimestampUtc >= pair.SecondSample.TimestampUtc
                    ? pair.FirstSample.TimestampUtc
                    : pair.SecondSample.TimestampUtc;
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
            CloseActiveStationarySegment(_lastPair, _lastUpdate, exitReason);
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
                EntryPairId = segment.EntryPairId,
                EntryTimestampUtc = segment.EntryTimestampUtc,
                EntryFirstEnergy = segment.EntryFirstEnergy,
                EntrySecondEnergy = segment.EntrySecondEnergy,
                EntryFirstAverage = segment.EntryFirstAverage,
                EntrySecondAverage = segment.EntrySecondAverage,
                EntryStabilityMetric = segment.EntryStabilityMetric,
                ExitPairId = segment.ExitPairId,
                ExitTimestampUtc = segment.ExitTimestampUtc,
                ExitStabilityMetric = segment.ExitStabilityMetric,
                DurationMs = segment.DurationMs,
                ExitReason = segment.ExitReason
            };
        }
    }
}
