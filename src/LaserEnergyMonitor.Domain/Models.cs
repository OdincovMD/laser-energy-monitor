using System;
using System.Collections.Generic;

namespace LaserEnergyMonitor.Domain
{
    public sealed class MeasurementSample
    {
        public string SourceId { get; set; }
        public long SequenceNumber { get; set; }
        public DateTime TimestampUtc { get; set; }
        public long MonotonicTicks { get; set; }
        public double Energy { get; set; }
    }

    public sealed class SynchronizedMeasurementPair
    {
        public long PairId { get; set; }
        public MeasurementSample FirstSample { get; set; }
        public MeasurementSample SecondSample { get; set; }
        public TimeSpan Delta { get; set; }
    }

    public sealed class StationarityUpdate
    {
        public bool HasEnoughData { get; set; }
        public bool IsStationary { get; set; }
        public bool EnteredStationaryState { get; set; }
        public bool ExitedStationaryState { get; set; }
        public double RollingAverageFirst { get; set; }
        public double RollingAverageSecond { get; set; }
        public double StabilityMetric { get; set; }
    }

    public sealed class SessionSettings
    {
        public int RollingWindowSize { get; set; }
        public double EnterThresholdPercent { get; set; }
        public double ExitThresholdPercent { get; set; }
        public TimeSpan SynchronizationDelta { get; set; }
        public string OutputPath { get; set; }
        public string SessionName { get; set; }
    }

    public sealed class SessionMetadata
    {
        public string SessionName { get; set; }
        public DateTime StartedUtc { get; set; }
        public string FirstSourceId { get; set; }
        public string SecondSourceId { get; set; }
    }

    public sealed class SessionSummary
    {
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public int PairCount { get; set; }
        public int EventCount { get; set; }
        public bool CompletedNormally { get; set; }
        public string FinalState { get; set; }
    }

    public sealed class SessionEvent
    {
        public SessionEventType EventType { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Message { get; set; }
        public long? SequenceNumber { get; set; }
        public double? MetricValue { get; set; }
    }

    public sealed class DeviceFault
    {
        public string SourceId { get; set; }
        public FaultSeverity Severity { get; set; }
        public string Message { get; set; }
        public DateTime TimestampUtc { get; set; }
        public Exception Exception { get; set; }
    }

    public sealed class LiveMeasurementSnapshot
    {
        public MeasurementSessionState SessionState { get; set; }
        public long PairId { get; set; }
        public double? FirstEnergy { get; set; }
        public double? SecondEnergy { get; set; }
        public double? FirstAverage { get; set; }
        public double? SecondAverage { get; set; }
        public double? StabilityMetric { get; set; }
        public bool IsStationary { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    public enum MeasurementSessionState
    {
        Idle = 0,
        Initialized = 1,
        Measuring = 2,
        Stationary = 3,
        Faulted = 4,
        Completed = 5
    }

    public enum SessionEventType
    {
        Info = 0,
        StationaryEntered = 1,
        StationaryExited = 2,
        Desynchronized = 3,
        Fault = 4,
        SessionStarted = 5,
        SessionStopped = 6
    }

    public enum FaultSeverity
    {
        Warning = 0,
        Critical = 1
    }
}
