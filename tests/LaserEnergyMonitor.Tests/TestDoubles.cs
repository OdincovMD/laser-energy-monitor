using System;
using System.Collections.Generic;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Tests
{
    internal sealed class ManualMeasurementSource : IMeasurementSource
    {
        private readonly bool _connectOnInitialize;

        public ManualMeasurementSource(string sourceId, bool connectOnInitialize)
        {
            SourceId = sourceId;
            _connectOnInitialize = connectOnInitialize;
        }

        public string SourceId { get; private set; }

        public bool IsConnected { get; private set; }

        public bool IsStarted { get; private set; }

        public bool IsDisposed { get; private set; }

        public event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        public event EventHandler<DeviceFaultEventArgs> Faulted;

        public void Initialize()
        {
            IsConnected = _connectOnInitialize;
        }

        public void Start()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Source is not connected.");
            }

            IsStarted = true;
        }

        public void Stop()
        {
            IsStarted = false;
        }

        public void Dispose()
        {
            IsDisposed = true;
            IsStarted = false;
        }

        public void EmitMeasurement(long sequenceNumber, DateTime timestampUtc, double energy)
        {
            MeasurementReceived?.Invoke(
                this,
                new MeasurementReceivedEventArgs(
                    new MeasurementSample
                    {
                        SourceId = SourceId,
                        SequenceNumber = sequenceNumber,
                        TimestampUtc = timestampUtc,
                        MonotonicTicks = timestampUtc.Ticks,
                        Energy = energy
                    }));
        }

        public void EmitFault(string message)
        {
            Faulted?.Invoke(
                this,
                new DeviceFaultEventArgs(
                    new DeviceFault
                    {
                        SourceId = SourceId,
                        Severity = FaultSeverity.Critical,
                        Message = message,
                        TimestampUtc = DateTime.UtcNow
                    }));
        }
    }

    internal sealed class RecordingMeasurementExporter : IMeasurementExporter
    {
        public SessionMetadata StartedMetadata { get; private set; }

        public SessionSettings StartedSettings { get; private set; }

        public List<SynchronizedMeasurementPair> Measurements { get; } = new List<SynchronizedMeasurementPair>();

        public List<StationarityUpdate> Updates { get; } = new List<StationarityUpdate>();

        public List<SessionEvent> Events { get; } = new List<SessionEvent>();

        public List<StationarySegmentResult> StationarySegments { get; } = new List<StationarySegmentResult>();

        public SessionSummary CompletedSummary { get; private set; }

        public SessionSummary AbortedSummary { get; private set; }

        public string AbortReason { get; private set; }

        public bool IsDisposed { get; private set; }

        public void StartSession(SessionMetadata metadata, SessionSettings settings)
        {
            StartedMetadata = metadata;
            StartedSettings = settings;
        }

        public void WriteMeasurement(SynchronizedMeasurementPair pair, StationarityUpdate update)
        {
            Measurements.Add(pair);
            Updates.Add(update);
        }

        public void WriteEvent(SessionEvent sessionEvent)
        {
            Events.Add(sessionEvent);
        }

        public void WriteStationarySegment(StationarySegmentResult segment)
        {
            StationarySegments.Add(segment);
        }

        public void Complete(SessionSummary summary)
        {
            CompletedSummary = summary;
        }

        public void Abort(SessionSummary summary, string reason)
        {
            AbortedSummary = summary;
            AbortReason = reason;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    internal sealed class RecordingLogger : IApplicationLogger
    {
        public List<string> Infos { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        public void Info(string message)
        {
            Infos.Add(message);
        }

        public void Warning(string message)
        {
            Warnings.Add(message);
        }

        public void Error(string message)
        {
            Errors.Add(message);
        }
    }

    internal sealed class RecordingNotifier : IOperatorNotifier
    {
        public List<string> Infos { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();

        public List<string> Criticals { get; } = new List<string>();

        public void ShowInfo(string message)
        {
            Infos.Add(message);
        }

        public void ShowWarning(string message)
        {
            Warnings.Add(message);
        }

        public void ShowCritical(string message)
        {
            Criticals.Add(message);
        }
    }

    internal sealed class AdjustableClock : IClock
    {
        public AdjustableClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }
    }
}
