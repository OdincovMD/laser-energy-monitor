using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LaserEnergyMonitor.Infrastructure.Excel
{
    public sealed class MeasurementAnalyticsAnalyzer
    {
        private const int DefaultMaxChartPoints = 500;

        public AnalyticsReport Analyze(MeasurementAnalyticsWorkbook workbook)
        {
            return Analyze(workbook, DefaultMaxChartPoints);
        }

        public AnalyticsReport Analyze(MeasurementAnalyticsWorkbook workbook, int maxChartPoints)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            if (workbook.RawRows.Count == 0)
            {
                throw new InvalidOperationException("RawData sheet does not contain measurement rows.");
            }

            List<AnalyticsRawDataRow> orderedRows = workbook.RawRows
                .OrderBy(row => row.RecordId)
                .ToList();

            AnalyticsReport report = new AnalyticsReport
            {
                SourcePath = workbook.SourcePath,
                SessionName = ReadSummaryString(workbook, "SessionName") ?? "Measurement Session",
                StartedUtc = ReadSummaryDate(workbook, "StartedUtc") ?? orderedRows.Select(row => row.TimestampUtc).Where(value => value.HasValue).Select(value => value.Value).DefaultIfEmpty().Min(),
                FinishedUtc = ReadSummaryDate(workbook, "FinishedUtc") ?? orderedRows.Select(row => row.TimestampUtc).Where(value => value.HasValue).Select(value => value.Value).DefaultIfEmpty().Max(),
                SampleCount = ReadSummaryInt(workbook, "SampleCount") ?? orderedRows.Count,
                EventCount = ReadSummaryInt(workbook, "EventCount") ?? workbook.Events.Count,
                FaultCount = ReadSummaryInt(workbook, "FaultCount") ?? CountFaults(workbook.Events),
                StationarySegmentCount = ReadSummaryInt(workbook, "ClosedStationarySegmentCount") ?? workbook.StationarySegments.Count
            };

            if (report.StartedUtc.HasValue && report.FinishedUtc.HasValue && report.FinishedUtc.Value >= report.StartedUtc.Value)
            {
                report.DurationSeconds = (report.FinishedUtc.Value - report.StartedUtc.Value).TotalSeconds;
            }

            foreach (string warning in workbook.Warnings)
            {
                report.Warnings.Add(warning);
            }

            report.FirstSource = BuildSourceStatistics(orderedRows, "First");
            report.SecondSource = BuildSourceStatistics(orderedRows, "Second");
            report.Comparison = BuildComparison(orderedRows);
            report.Stationary = BuildStationaryStatistics(orderedRows, workbook.StationarySegments);
            report.ChartPoints.AddRange(Downsample(BuildChartPoints(orderedRows), maxChartPoints));
            BuildOverviewMetrics(report);
            return report;
        }

        private static SourceStatistics BuildSourceStatistics(List<AnalyticsRawDataRow> rows, string slot)
        {
            List<AnalyticsRawDataRow> sourceRows = rows
                .Where(row => string.Equals(row.SourceSlot, slot, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.RecordId)
                .ToList();
            List<double> values = sourceRows.Where(row => row.Energy.HasValue).Select(row => row.Energy.Value).ToList();
            List<double> stabilityValues = rows
                .Select(row => string.Equals(slot, "First", StringComparison.OrdinalIgnoreCase) ? row.FirstStabilityMetricPercent : row.SecondStabilityMetricPercent)
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToList();
            List<bool> stationaryValues = rows
                .Select(row => string.Equals(slot, "First", StringComparison.OrdinalIgnoreCase) ? row.FirstIsStationary : row.SecondIsStationary)
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToList();

            double? first = values.Count > 0 ? values[0] : (double?)null;
            double? last = values.Count > 0 ? values[values.Count - 1] : (double?)null;
            double? mean = Mean(values);
            double? standardDeviation = StandardDeviation(values);

            return new SourceStatistics
            {
                Slot = slot,
                SourceId = sourceRows.Select(row => row.SourceId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? slot,
                Count = values.Count,
                Min = values.Count > 0 ? values.Min() : (double?)null,
                Max = values.Count > 0 ? values.Max() : (double?)null,
                Mean = mean,
                Median = Median(values),
                StandardDeviation = standardDeviation,
                CoefficientOfVariationPercent = mean.HasValue && standardDeviation.HasValue && Math.Abs(mean.Value) > double.Epsilon
                    ? standardDeviation.Value / Math.Abs(mean.Value) * 100.0d
                    : (double?)null,
                PeakToPeak = values.Count > 0 ? values.Max() - values.Min() : (double?)null,
                FirstValue = first,
                LastValue = last,
                DriftPercent = first.HasValue && last.HasValue && Math.Abs(first.Value) > double.Epsilon
                    ? (last.Value - first.Value) / Math.Abs(first.Value) * 100.0d
                    : (double?)null,
                MissingCount = sourceRows.Count(row => !row.Energy.HasValue),
                ZeroCount = values.Count(value => Math.Abs(value) <= double.Epsilon),
                MeanStabilityPercent = Mean(stabilityValues),
                MaxStabilityPercent = stabilityValues.Count > 0 ? stabilityValues.Max() : (double?)null,
                StationaryPercent = stationaryValues.Count > 0
                    ? stationaryValues.Count(value => value) * 100.0d / stationaryValues.Count
                    : (double?)null
            };
        }

        private static ComparisonStatistics BuildComparison(List<AnalyticsRawDataRow> rows)
        {
            List<double> first = rows
                .Where(row => string.Equals(row.SourceSlot, "First", StringComparison.OrdinalIgnoreCase) && row.Energy.HasValue)
                .OrderBy(row => row.RecordId)
                .Select(row => row.Energy.Value)
                .ToList();
            List<double> second = rows
                .Where(row => string.Equals(row.SourceSlot, "Second", StringComparison.OrdinalIgnoreCase) && row.Energy.HasValue)
                .OrderBy(row => row.RecordId)
                .Select(row => row.Energy.Value)
                .ToList();

            int pairedCount = Math.Min(first.Count, second.Count);
            List<double> deltas = new List<double>();
            for (int i = 0; i < pairedCount; i++)
            {
                deltas.Add(first[i] - second[i]);
            }

            double? firstMean = Mean(first);
            double? secondMean = Mean(second);
            double? meanDelta = Mean(deltas);

            return new ComparisonStatistics
            {
                PairedCount = pairedCount,
                MeanRatio = firstMean.HasValue && secondMean.HasValue && Math.Abs(secondMean.Value) > double.Epsilon
                    ? firstMean.Value / secondMean.Value
                    : (double?)null,
                MeanDelta = meanDelta,
                MeanDeltaPercent = meanDelta.HasValue && secondMean.HasValue && Math.Abs(secondMean.Value) > double.Epsilon
                    ? meanDelta.Value / Math.Abs(secondMean.Value) * 100.0d
                    : (double?)null,
                Correlation = Correlation(first, second, pairedCount),
                AverageAbsoluteDelta = deltas.Count > 0 ? deltas.Select(Math.Abs).Average() : (double?)null
            };
        }

        private static StationaryStatistics BuildStationaryStatistics(List<AnalyticsRawDataRow> rows, List<AnalyticsStationarySegmentRow> segments)
        {
            List<double> durationsSeconds = segments
                .Where(segment => segment.DurationMs.HasValue)
                .Select(segment => segment.DurationMs.Value / 1000.0d)
                .ToList();
            List<double> entryStability = segments
                .Where(segment => segment.EntryStabilityMetric.HasValue)
                .Select(segment => segment.EntryStabilityMetric.Value)
                .ToList();
            List<double> exitStability = segments
                .Where(segment => segment.ExitStabilityMetric.HasValue)
                .Select(segment => segment.ExitStabilityMetric.Value)
                .ToList();
            List<bool> bothStationary = rows
                .Where(row => row.IsStationary.HasValue)
                .Select(row => row.IsStationary.Value)
                .ToList();

            return new StationaryStatistics
            {
                SegmentCount = segments.Count,
                TotalDurationSeconds = durationsSeconds.Count > 0 ? durationsSeconds.Sum() : (double?)null,
                LongestDurationSeconds = durationsSeconds.Count > 0 ? durationsSeconds.Max() : (double?)null,
                AverageDurationSeconds = Mean(durationsSeconds),
                AverageEntryStabilityPercent = Mean(entryStability),
                AverageExitStabilityPercent = Mean(exitStability),
                BothStationaryPercent = bothStationary.Count > 0
                    ? bothStationary.Count(value => value) * 100.0d / bothStationary.Count
                    : (double?)null
            };
        }

        private static List<AnalyticsTimePoint> BuildChartPoints(List<AnalyticsRawDataRow> rows)
        {
            List<AnalyticsTimePoint> points = new List<AnalyticsTimePoint>();
            double? latestFirst = null;
            double? latestSecond = null;
            foreach (AnalyticsRawDataRow row in rows)
            {
                if (string.Equals(row.SourceSlot, "First", StringComparison.OrdinalIgnoreCase) && row.Energy.HasValue)
                {
                    latestFirst = row.Energy.Value;
                }

                if (string.Equals(row.SourceSlot, "Second", StringComparison.OrdinalIgnoreCase) && row.Energy.HasValue)
                {
                    latestSecond = row.Energy.Value;
                }

                points.Add(
                    new AnalyticsTimePoint
                    {
                        RecordId = row.RecordId,
                        TimestampUtc = row.TimestampUtc,
                        FirstEnergy = latestFirst,
                        SecondEnergy = latestSecond,
                        FirstStabilityPercent = row.FirstStabilityMetricPercent,
                        SecondStabilityPercent = row.SecondStabilityMetricPercent,
                        OverallStabilityPercent = row.StabilityMetric
                    });
            }

            return points;
        }

        private static List<AnalyticsTimePoint> Downsample(List<AnalyticsTimePoint> points, int maxPoints)
        {
            if (maxPoints <= 0 || points.Count <= maxPoints)
            {
                return points;
            }

            List<AnalyticsTimePoint> result = new List<AnalyticsTimePoint>();
            double step = (points.Count - 1) / (double)(maxPoints - 1);
            int previousIndex = -1;
            for (int i = 0; i < maxPoints; i++)
            {
                int index = (int)Math.Round(i * step);
                if (index == previousIndex)
                {
                    continue;
                }

                result.Add(points[index]);
                previousIndex = index;
            }

            return result;
        }

        private static void BuildOverviewMetrics(AnalyticsReport report)
        {
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Session", Value = report.SessionName });
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Duration", Value = FormatDuration(report.DurationSeconds) });
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Samples", Value = report.SampleCount.ToString(CultureInfo.InvariantCulture) });
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Events", Value = report.EventCount.ToString(CultureInfo.InvariantCulture) });
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Faults", Value = report.FaultCount.ToString(CultureInfo.InvariantCulture) });
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Stationary segments", Value = report.StationarySegmentCount.ToString(CultureInfo.InvariantCulture) });
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Both stationary", Value = FormatPercent(report.Stationary.BothStationaryPercent) });
            report.OverviewMetrics.Add(new AnalyticsMetric { Name = "Mean ratio", Value = FormatNumber(report.Comparison.MeanRatio) });
        }

        private static int CountFaults(List<AnalyticsEventRow> events)
        {
            return events.Count(item => string.Equals(item.EventType, "Fault", StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadSummaryString(MeasurementAnalyticsWorkbook workbook, string key)
        {
            string value;
            return workbook.Summary.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        }

        private static int? ReadSummaryInt(MeasurementAnalyticsWorkbook workbook, string key)
        {
            string value;
            int parsed;
            return workbook.Summary.TryGetValue(key, out value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : (int?)null;
        }

        private static DateTime? ReadSummaryDate(MeasurementAnalyticsWorkbook workbook, string key)
        {
            string value;
            DateTime parsed;
            return workbook.Summary.TryGetValue(key, out value) &&
                   DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed)
                ? parsed
                : (DateTime?)null;
        }

        private static double? Mean(List<double> values)
        {
            return values.Count > 0 ? values.Average() : (double?)null;
        }

        private static double? Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            List<double> sorted = values.OrderBy(value => value).ToList();
            int middle = sorted.Count / 2;
            if (sorted.Count % 2 == 1)
            {
                return sorted[middle];
            }

            return (sorted[middle - 1] + sorted[middle]) / 2.0d;
        }

        private static double? StandardDeviation(List<double> values)
        {
            if (values.Count < 2)
            {
                return null;
            }

            double mean = values.Average();
            double variance = values.Sum(value => Math.Pow(value - mean, 2.0d)) / (values.Count - 1);
            return Math.Sqrt(variance);
        }

        private static double? Correlation(List<double> first, List<double> second, int count)
        {
            if (count < 2)
            {
                return null;
            }

            double firstMean = first.Take(count).Average();
            double secondMean = second.Take(count).Average();
            double numerator = 0.0d;
            double firstSum = 0.0d;
            double secondSum = 0.0d;
            for (int i = 0; i < count; i++)
            {
                double firstOffset = first[i] - firstMean;
                double secondOffset = second[i] - secondMean;
                numerator += firstOffset * secondOffset;
                firstSum += firstOffset * firstOffset;
                secondSum += secondOffset * secondOffset;
            }

            double denominator = Math.Sqrt(firstSum * secondSum);
            return denominator > double.Epsilon ? numerator / denominator : (double?)null;
        }

        private static string FormatDuration(double? seconds)
        {
            if (!seconds.HasValue)
            {
                return "-";
            }

            TimeSpan duration = TimeSpan.FromSeconds(seconds.Value);
            return duration.ToString(duration.TotalHours >= 1.0d ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatPercent(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) + "%" : "-";
        }

        private static string FormatNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : "-";
        }
    }
}
