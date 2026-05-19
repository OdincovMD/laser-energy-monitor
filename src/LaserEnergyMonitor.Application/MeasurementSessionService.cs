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
            _firstSource = firstSource;
            _secondSource = secondSource;
            _synchronizer = synchronizer;
            _detector = detector;
            _exporter = exporter;
            _logger = logger;
            _notifier = notifier;
            _clock = clock;
            _state = MeasurementSessionState.Idle;

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
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            _currentSettings = settings;
            _logger.Info("Initializing measurement sources.");
            _firstSource.Initialize();
            _secondSource.Initialize();
            _synchronizer.Configure(settings.SynchronizationDelta, _firstSource.SourceId, _secondSource.SourceId);
            _detector.Configure(settings);
            TransitionTo(MeasurementSessionState.Initialized);
        }

        public void Start()
        {
            if (_currentSettings == null)
            {
                throw new InvalidOperationException("Session settings were not initialized.");
            }

            _pairCount = 0;
            _eventCount = 0;
            _metadata = new SessionMetadata
            {
                SessionName = string.IsNullOrWhiteSpace(_currentSettings.SessionName) ? "Measurement Session" : _currentSettings.SessionName,
                StartedUtc = _clock.UtcNow,
                FirstSourceId = _firstSource.SourceId,
                SecondSourceId = _secondSource.SourceId
            };

            _exporter.StartSession(_metadata, _currentSettings);
            RaiseEvent(SessionEventType.SessionStarted, "Measurement session started.", null, null);
            _firstSource.Start();
            _secondSource.Start();
            TransitionTo(MeasurementSessionState.Measuring);
        }

        public void Stop()
        {
            _firstSource.Stop();
            _secondSource.Stop();
            RaiseEvent(SessionEventType.SessionStopped, "Measurement session stopped.", null, null);

            SessionSummary summary = new SessionSummary
            {
                StartedUtc = _metadata != null ? _metadata.StartedUtc : _clock.UtcNow,
                FinishedUtc = _clock.UtcNow,
                PairCount = _pairCount,
                EventCount = _eventCount,
                CompletedNormally = _state != MeasurementSessionState.Faulted,
                FinalState = _state.ToString()
            };

            _exporter.Complete(summary);
            TransitionTo(MeasurementSessionState.Completed);
        }

        public void Dispose()
        {
            _firstSource.Dispose();
            _secondSource.Dispose();
            _exporter.Dispose();
        }

        private void OnMeasurementReceived(object sender, MeasurementReceivedEventArgs e)
        {
            _synchronizer.Push(e.Sample);
        }

        private void OnPairReady(object sender, SynchronizedMeasurementPairEventArgs e)
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

        private void OnDesynchronized(object sender, DesynchronizationEventArgs e)
        {
            RaiseEvent(SessionEventType.Desynchronized, "Desynchronization detected: " + e.Reason, e.Sample.SequenceNumber, null);
        }

        private void OnFaulted(object sender, DeviceFaultEventArgs e)
        {
            string message = string.Format("Critical fault on {0}: {1}", e.Fault.SourceId, e.Fault.Message);
            _logger.Error(message);
            _notifier.ShowCritical(message);
            RaiseEvent(SessionEventType.Fault, message, null, null);
            TransitionTo(MeasurementSessionState.Faulted);
            _firstSource.Stop();
            _secondSource.Stop();
            _exporter.Abort(message);
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

        private void TransitionTo(MeasurementSessionState state)
        {
            _state = state;
            StateChanged?.Invoke(this, new SessionStateChangedEventArgs(state));
        }
    }
}
