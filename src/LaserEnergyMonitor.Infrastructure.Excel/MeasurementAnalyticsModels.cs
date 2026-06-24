using System;
using System.Collections.Generic;

namespace LaserEnergyMonitor.Infrastructure.Excel
{
    public sealed class MeasurementAnalyticsWorkbook
    {
        public MeasurementAnalyticsWorkbook()
        {
            RawRows = new List<AnalyticsRawDataRow>();
            Summary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Events = new List<AnalyticsEventRow>();
            StationarySegments = new List<AnalyticsStationarySegmentRow>();
            Warnings = new List<string>();
        }

        public string SourcePath { get; set; }

        public List<AnalyticsRawDataRow> RawRows { get; private set; }

        public Dictionary<string, string> Summary { get; private set; }

        public List<AnalyticsEventRow> Events { get; private set; }

        public List<AnalyticsStationarySegmentRow> StationarySegments { get; private set; }

        public List<string> Warnings { get; private set; }
    }

    public sealed class AnalyticsRawDataRow
    {
        public long RecordId { get; set; }
        public DateTime? TimestampUtc { get; set; }
        public string SourceId { get; set; }
        public long? Sequence { get; set; }
        public double? Energy { get; set; }
        public string SourceSlot { get; set; }
        public double? FirstAverage { get; set; }
        public double? SecondAverage { get; set; }
        public double? FirstStabilityMetricPercent { get; set; }
        public double? SecondStabilityMetricPercent { get; set; }
        public double? StabilityMetric { get; set; }
        public bool? FirstIsStationary { get; set; }
        public bool? SecondIsStationary { get; set; }
        public bool? IsStationary { get; set; }
    }

    public sealed class AnalyticsEventRow
    {
        public DateTime? TimestampUtc { get; set; }
        public string EventType { get; set; }
        public string ReasonCode { get; set; }
        public long? SequenceNumber { get; set; }
        public double? MetricValue { get; set; }
        public string Message { get; set; }
    }

    public sealed class AnalyticsStationarySegmentRow
    {
        public int SegmentId { get; set; }
        public long? EntryRecordId { get; set; }
        public DateTime? EntryTimestampUtc { get; set; }
        public double? EntryFirstEnergy { get; set; }
        public double? EntrySecondEnergy { get; set; }
        public double? EntryFirstAverage { get; set; }
        public double? EntrySecondAverage { get; set; }
        public double? EntryStabilityMetric { get; set; }
        public long? ExitRecordId { get; set; }
        public DateTime? ExitTimestampUtc { get; set; }
        public double? ExitStabilityMetric { get; set; }
        public double? DurationMs { get; set; }
        public string ExitReason { get; set; }
    }

    public sealed class AnalyticsReport
    {
        public AnalyticsReport()
        {
            Warnings = new List<string>();
            ChartPoints = new List<AnalyticsTimePoint>();
            OverviewMetrics = new List<AnalyticsMetric>();
        }

        public string SourcePath { get; set; }
        public string SessionName { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
        public double? DurationSeconds { get; set; }
        public int SampleCount { get; set; }
        public int EventCount { get; set; }
        public int FaultCount { get; set; }
        public int StationarySegmentCount { get; set; }
        public SourceStatistics FirstSource { get; set; }
        public SourceStatistics SecondSource { get; set; }
        public ComparisonStatistics Comparison { get; set; }
        public StationaryStatistics Stationary { get; set; }
        public List<AnalyticsTimePoint> ChartPoints { get; private set; }
        public List<AnalyticsMetric> OverviewMetrics { get; private set; }
        public List<string> Warnings { get; private set; }
    }

    public sealed class AnalyticsMetric
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public sealed class SourceStatistics
    {
        public string Slot { get; set; }
        public string SourceId { get; set; }
        public int Count { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Mean { get; set; }
        public double? Median { get; set; }
        public double? StandardDeviation { get; set; }
        public double? CoefficientOfVariationPercent { get; set; }
        public double? PeakToPeak { get; set; }
        public double? FirstValue { get; set; }
        public double? LastValue { get; set; }
        public double? DriftPercent { get; set; }
        public int MissingCount { get; set; }
        public int ZeroCount { get; set; }
        public double? MeanStabilityPercent { get; set; }
        public double? MaxStabilityPercent { get; set; }
        public double? StationaryPercent { get; set; }
    }

    public sealed class ComparisonStatistics
    {
        public int PairedCount { get; set; }
        public double? MeanRatio { get; set; }
        public double? MeanDelta { get; set; }
        public double? MeanDeltaPercent { get; set; }
        public double? Correlation { get; set; }
        public double? AverageAbsoluteDelta { get; set; }
    }

    public sealed class StationaryStatistics
    {
        public int SegmentCount { get; set; }
        public double? TotalDurationSeconds { get; set; }
        public double? LongestDurationSeconds { get; set; }
        public double? AverageDurationSeconds { get; set; }
        public double? AverageEntryStabilityPercent { get; set; }
        public double? AverageExitStabilityPercent { get; set; }
        public double? BothStationaryPercent { get; set; }
    }

    public sealed class AnalyticsTimePoint
    {
        public long RecordId { get; set; }
        public DateTime? TimestampUtc { get; set; }
        public double? FirstEnergy { get; set; }
        public double? SecondEnergy { get; set; }
        public double? FirstStabilityPercent { get; set; }
        public double? SecondStabilityPercent { get; set; }
        public double? OverallStabilityPercent { get; set; }
    }
}
