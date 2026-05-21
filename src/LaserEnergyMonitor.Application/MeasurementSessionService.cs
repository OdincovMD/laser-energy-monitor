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
        private SessionSettings _currentSettings;
        private bool _sessionStarted;
        private bool _sessionFinalized;
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
                RaiseEvent(SessionEventType.SessionStarted, "Measurement session started.", null, null);
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
                RaiseEvent(SessionEventType.SessionStopped, "Measurement session stopped.", null, null);
                CompleteSession();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _firstSource.MeasurementReceived -= OnMeasurementReceived;
            _secondSource.MeasurementReceived -= OnMeasurementReceived;
            _firstSource.Faulted -= OnFaulted;
            _secondSource.Faulted -= OnFaulted;
            _synchronizer.PairReady -= OnPairReady;
            _synchronizer.Desynchronized -= OnDesynchronized;

            StopSourcesSafely();
            _exporter.Dispose();
            _firstSource.Dispose();
            _secondSource.Dispose();
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
                _pairCount += 1;
                _exporter.WriteMeasurement(e.Pair, update);

                if (update.EnteredStationaryState)
                {
                    RaiseEvent(SessionEventType.StationaryEntered, "Stationary mode detected.", e.Pair.PairId, update.StabilityMetric);
                    TransitionTo(MeasurementSessionState.Stationary);
                }
                else if (update.ExitedStationaryState)
                {
                    RaiseEvent(SessionEventType.StationaryExited, "Stationary mode lost.", e.Pair.PairId, update.StabilityMetric);
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
                RaiseEvent(SessionEventType.Desynchronized, "Desynchronization detected: " + e.Reason, e.Sample.SequenceNumber, null);
            }
            catch (Exception ex)
            {
                HandlePipelineException("Desynchronization handling failed.", e != null ? e.Sample : null, ex);
            }
        }

        private void OnFaulted(object sender, DeviceFaultEventArgs e)
        {
            if (_disposed || _sessionFinalized)
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

            try
            {
                RaiseEvent(SessionEventType.Fault, message, null, null);
            }
            catch
            {
            }

            TransitionTo(MeasurementSessionState.Faulted);
            StopSourcesSafely();
            AbortSession(message);
        }

        private void HandleStartupFailure(Exception ex)
        {
            string message = "Measurement session failed to start: " + ex.Message;
            _logger.Error(message);
            StopSourcesSafely();
            AbortSession(message);
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
                        Message = contextMessage + " " + ex.Message,
                        TimestampUtc = _clock.UtcNow,
                        Exception = ex
                    }));
        }

        private void RaiseEvent(SessionEventType eventType, string message, long? sequenceNumber, double? metricValue)
        {
            SessionEvent sessionEvent = new SessionEvent
            {
                EventType = eventType,
                TimestampUtc = _clock.UtcNow,
                Message = message,
                SequenceNumber = sequenceNumber,
                MetricValue = metricValue
            };

            _eventCount += 1;
            _exporter.WriteEvent(sessionEvent);
            SessionEventRaised?.Invoke(this, new SessionEventRaisedEventArgs(sessionEvent));
        }

        private void CompleteSession()
        {
            if (!_sessionStarted || _sessionFinalized)
            {
                return;
            }

            SessionSummary summary = new SessionSummary
            {
                StartedUtc = _metadata != null ? _metadata.StartedUtc : _clock.UtcNow,
                FinishedUtc = _clock.UtcNow,
                PairCount = _pairCount,
                EventCount = _eventCount,
                CompletedNormally = true,
                FinalState = _state.ToString()
            };

            _sessionFinalized = true;
            _sessionStarted = false;
            _exporter.Complete(summary);
            TransitionTo(MeasurementSessionState.Completed);
            ResetProcessingBuffers();
        }

        private void AbortSession(string reason)
        {
            if (_sessionFinalized)
            {
                return;
            }

            _sessionFinalized = true;
            _sessionStarted = false;

            try
            {
                _exporter.Abort(reason);
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
            _metadata = null;
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
    }
}
