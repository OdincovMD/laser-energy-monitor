using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure.Excel;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class MeasurementAnalyticsTests
    {
        [Fact]
        public void MeasurementAnalyticsAnalyzer_CalculatesSourceComparisonAndStationarity()
        {
            MeasurementAnalyticsWorkbook workbook = new MeasurementAnalyticsWorkbook();
            DateTime start = new DateTime(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);
            workbook.Summary["SessionName"] = "Analytics Test";
            workbook.Summary["StartedUtc"] = start.ToString("u");
            workbook.Summary["FinishedUtc"] = start.AddSeconds(4).ToString("u");
            workbook.RawRows.Add(CreateRawRow(1, start, "First", "BeamGage", 10.0d, 0.4d, 0.8d, true, false, false));
            workbook.RawRows.Add(CreateRawRow(2, start.AddSeconds(1), "Second", "Ophir", 5.0d, 0.4d, 0.6d, true, true, true));
            workbook.RawRows.Add(CreateRawRow(3, start.AddSeconds(2), "First", "BeamGage", 12.0d, 0.7d, 1.2d, false, false, false));
            workbook.RawRows.Add(CreateRawRow(4, start.AddSeconds(3), "Second", "Ophir", 7.0d, 0.6d, 0.9d, true, true, true));
            workbook.StationarySegments.Add(
                new AnalyticsStationarySegmentRow
                {
                    SegmentId = 1,
                    DurationMs = 2000.0d,
                    EntryStabilityMetric = 0.6d,
                    ExitStabilityMetric = 0.9d
                });

            AnalyticsReport report = new MeasurementAnalyticsAnalyzer().Analyze(workbook, 10);

            Assert.Equal("Analytics Test", report.SessionName);
            Assert.Equal(4, report.SampleCount);
            Assert.Equal(11.0d, report.FirstSource.Mean.Value, 6);
            Assert.Equal(6.0d, report.SecondSource.Mean.Value, 6);
            Assert.Equal(2, report.Comparison.PairedCount);
            Assert.Equal(5.0d, report.Comparison.MeanDelta.Value, 6);
            Assert.Equal(11.0d / 6.0d, report.Comparison.MeanRatio.Value, 6);
            Assert.Equal(2.0d, report.Stationary.TotalDurationSeconds.Value, 6);
            Assert.Equal(50.0d, report.Stationary.BothStationaryPercent.Value, 6);
        }

        [Fact]
        public void AnalyticsReaderAndExporter_ProcessMeasurementWorkbook()
        {
            string directory = Path.Combine(Path.GetTempPath(), "LaserEnergyMonitorAnalyticsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string measurementPath = Path.Combine(directory, "measurement-session.xlsx");
                PrototypeExcelExporter measurementExporter = new PrototypeExcelExporter();
                DateTime start = new DateTime(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);
                measurementExporter.StartSession(
                    new SessionMetadata { SessionName = "Workbook Test", StartedUtc = start },
                    new SessionSettings { SessionName = "Workbook Test", OutputPath = measurementPath });
                measurementExporter.WriteMeasurement(
                    CreateRecord(1, "BeamGage", 1, start, 10.0d, true),
                    CreateUpdate(10.0d, 0.0d, 0.4d, 0.8d, false));
                measurementExporter.WriteMeasurement(
                    CreateRecord(2, "Ophir", 1, start.AddSeconds(1), 5.0d, false),
                    CreateUpdate(10.0d, 5.0d, 0.4d, 0.6d, true));
                measurementExporter.WriteEvent(
                    new SessionEvent
                    {
                        TimestampUtc = start.AddSeconds(1),
                        EventType = SessionEventType.StationaryEntered,
                        ReasonCode = "stationary-entered",
                        Message = "Stationary mode detected.",
                        SequenceNumber = 2,
                        MetricValue = 0.6d
                    });
                measurementExporter.WriteStationarySegment(
                    new StationarySegmentResult
                    {
                        SegmentId = 1,
                        EntryRecordId = 2,
                        EntryTimestampUtc = start.AddSeconds(1),
                        EntryFirstEnergy = 10.0d,
                        EntrySecondEnergy = 5.0d,
                        EntryFirstAverage = 10.0d,
                        EntrySecondAverage = 5.0d,
                        EntryStabilityMetric = 0.6d,
                        ExitRecordId = 2,
                        ExitTimestampUtc = start.AddSeconds(3),
                        ExitStabilityMetric = 0.7d,
                        DurationMs = 2000.0d,
                        ExitReason = "manual-stop"
                    });
                measurementExporter.Complete(
                    new SessionSummary
                    {
                        StartedUtc = start,
                        FinishedUtc = start.AddSeconds(3),
                        SampleCount = 2,
                        EventCount = 1,
                        FaultCount = 0,
                        StationarySegmentCount = 1,
                        ClosedStationarySegmentCount = 1,
                        CompletedNormally = true,
                        FinalState = "Completed"
                    });
                measurementExporter.Dispose();

                MeasurementAnalyticsWorkbook workbook = new MeasurementAnalyticsWorkbookReader().Read(measurementPath);
                AnalyticsReport report = new MeasurementAnalyticsAnalyzer().Analyze(workbook);
                string analyticsPath = Path.Combine(directory, "measurement-session.Analytics.xlsx");
                new MeasurementAnalyticsWorkbookExporter().Export(report, analyticsPath);

                Assert.True(File.Exists(analyticsPath));
                Assert.Equal(2, workbook.RawRows.Count);
                Assert.Single(workbook.Events);
                Assert.Single(workbook.StationarySegments);
                Assert.Contains(GetSheetNames(analyticsPath), name => name == "Overview");
                Assert.Contains(GetSheetNames(analyticsPath), name => name == "ChartData");
                Assert.Contains("OverallStabilityScorePercent", GetFirstRowValues(analyticsPath, "ChartData"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static AnalyticsRawDataRow CreateRawRow(
            long recordId,
            DateTime timestampUtc,
            string slot,
            string sourceId,
            double energy,
            double firstStability,
            double secondStability,
            bool firstStationary,
            bool secondStationary,
            bool stationary)
        {
            return new AnalyticsRawDataRow
            {
                RecordId = recordId,
                TimestampUtc = timestampUtc,
                SourceSlot = slot,
                SourceId = sourceId,
                Energy = energy,
                FirstStabilityMetricPercent = firstStability,
                SecondStabilityMetricPercent = secondStability,
                StabilityMetric = Math.Max(firstStability, secondStability),
                FirstIsStationary = firstStationary,
                SecondIsStationary = secondStationary,
                IsStationary = stationary
            };
        }

        private static MeasurementRecord CreateRecord(long recordId, string sourceId, long sequence, DateTime timestampUtc, double energy, bool first)
        {
            return new MeasurementRecord
            {
                RecordId = recordId,
                IsFirstSource = first,
                IsSecondSource = !first,
                Sample = new MeasurementSample
                {
                    SourceId = sourceId,
                    SequenceNumber = sequence,
                    TimestampUtc = timestampUtc,
                    Energy = energy
                }
            };
        }

        private static StationarityUpdate CreateUpdate(double firstAverage, double secondAverage, double firstStability, double secondStability, bool stationary)
        {
            return new StationarityUpdate
            {
                RollingAverageFirst = firstAverage,
                RollingAverageSecond = secondAverage,
                FirstStabilityMetric = firstStability,
                SecondStabilityMetric = secondStability,
                StabilityMetric = Math.Max(firstStability, secondStability),
                IsFirstSourceStationary = stationary,
                IsSecondSourceStationary = stationary,
                IsStationary = stationary
            };
        }

        private static string[] GetSheetNames(string path)
        {
            using (SpreadsheetDocument document = SpreadsheetDocument.Open(path, false))
            {
                return document.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Select(sheet => sheet.Name.Value).ToArray();
            }
        }

        private static string[] GetFirstRowValues(string path, string sheetName)
        {
            using (SpreadsheetDocument document = SpreadsheetDocument.Open(path, false))
            {
                Sheet sheet = document.WorkbookPart.Workbook.Sheets.Elements<Sheet>().First(item => item.Name.Value == sheetName);
                WorksheetPart worksheetPart = (WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id.Value);
                Row firstRow = worksheetPart.Worksheet.Descendants<Row>().First();
                return firstRow.Elements<Cell>().Select(ReadCellText).ToArray();
            }
        }

        private static string ReadCellText(Cell cell)
        {
            if (cell.InlineString != null)
            {
                return cell.InlineString.Text != null ? cell.InlineString.Text.Text : string.Empty;
            }

            return cell.CellValue != null ? cell.CellValue.Text : string.Empty;
        }
    }
}
