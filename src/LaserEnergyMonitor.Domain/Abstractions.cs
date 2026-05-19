using System;
using System.Collections.Generic;

namespace LaserEnergyMonitor.Domain
{
    public interface IMeasurementSource : IDisposable
    {
        string SourceId { get; }
        bool IsConnected { get; }

        event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        event EventHandler<DeviceFaultEventArgs> Faulted;

        void Initialize();
        void Start();
        void Stop();
    }

    public interface IMeasurementSynchronizer
    {
        event EventHandler<SynchronizedMeasurementPairEventArgs> PairReady;
        event EventHandler<DesynchronizationEventArgs> Desynchronized;

        void Configure(TimeSpan maxDelta, string firstSourceId, string secondSourceId);
        void Push(MeasurementSample sample);
        void Reset();
    }

    public interface IStationarityDetector
    {
        void Configure(SessionSettings settings);
        StationarityUpdate Evaluate(SynchronizedMeasurementPair pair);
        void Reset();
    }

    public interface IMeasurementExporter : IDisposable
    {
        void StartSession(SessionMetadata metadata, SessionSettings settings);
        void WriteMeasurement(SynchronizedMeasurementPair pair, StationarityUpdate update);
        void WriteEvent(SessionEvent sessionEvent);
        void Complete(SessionSummary summary);
        void Abort(string reason);
    }

    public interface IApplicationLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    public interface IOperatorNotifier
    {
        void ShowInfo(string message);
        void ShowWarning(string message);
        void ShowCritical(string message);
    }

    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class MeasurementReceivedEventArgs : EventArgs
    {
        public MeasurementReceivedEventArgs(MeasurementSample sample)
        {
            Sample = sample;
        }

        public MeasurementSample Sample { get; private set; }
    }

    public sealed class DeviceFaultEventArgs : EventArgs
    {
        public DeviceFaultEventArgs(DeviceFault fault)
        {
            Fault = fault;
        }

        public DeviceFault Fault { get; private set; }
    }

    public sealed class SynchronizedMeasurementPairEventArgs : EventArgs
    {
        public SynchronizedMeasurementPairEventArgs(SynchronizedMeasurementPair pair)
        {
            Pair = pair;
        }

        public SynchronizedMeasurementPair Pair { get; private set; }
    }

    public sealed class DesynchronizationEventArgs : EventArgs
    {
        public DesynchronizationEventArgs(MeasurementSample sample, string reason)
        {
            Sample = sample;
            Reason = reason;
        }

        public MeasurementSample Sample { get; private set; }

        public string Reason { get; private set; }
    }

    public sealed class SessionStateChangedEventArgs : EventArgs
    {
        public SessionStateChangedEventArgs(MeasurementSessionState state)
        {
            State = state;
        }

        public MeasurementSessionState State { get; private set; }
    }

    public sealed class LiveMeasurementUpdatedEventArgs : EventArgs
    {
        public LiveMeasurementUpdatedEventArgs(LiveMeasurementSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public LiveMeasurementSnapshot Snapshot { get; private set; }
    }

    public sealed class SessionEventRaisedEventArgs : EventArgs
    {
        public SessionEventRaisedEventArgs(SessionEvent sessionEvent)
        {
            SessionEvent = sessionEvent;
        }

        public SessionEvent SessionEvent { get; private set; }
    }
}
