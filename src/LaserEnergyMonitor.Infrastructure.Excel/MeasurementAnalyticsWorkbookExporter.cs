using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace LaserEnergyMonitor.Infrastructure.Excel
{
    public sealed class MeasurementAnalyticsWorkbookExporter
    {
        public void Export(AnalyticsReport report, string path)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Select an analytics export path.", nameof(path));
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using (SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;

                AppendSheet(workbookPart, sheets, ref sheetId, "Overview", BuildOverviewRows(report));
                AppendSheet(workbookPart, sheets, ref sheetId, "SourceStats", BuildSourceRows(report));
                AppendSheet(workbookPart, sheets, ref sheetId, "Comparison", BuildComparisonRows(report));
                AppendSheet(workbookPart, sheets, ref sheetId, "StationaryAnalysis", BuildStationaryRows(report));
                AppendSheet(workbookPart, sheets, ref sheetId, "ChartData", BuildChartRows(report));
                workbookPart.Workbook.Save();
            }
        }

        private static List<string[]> BuildOverviewRows(AnalyticsReport report)
        {
            List<string[]> rows = new List<string[]>();
            rows.Add(new[] { "Metric", "Value" });
            foreach (AnalyticsMetric metric in report.OverviewMetrics)
            {
                rows.Add(new[] { metric.Name, metric.Value });
            }

            rows.Add(new[] { "Source workbook", report.SourcePath ?? string.Empty });
            rows.Add(new[] { "Started UTC", FormatDate(report.StartedUtc) });
            rows.Add(new[] { "Finished UTC", FormatDate(report.FinishedUtc) });
            if (report.Warnings.Count > 0)
            {
                rows.Add(new[] { "Warnings", string.Join(" | ", report.Warnings) });
            }

            return rows;
        }

        private static List<string[]> BuildSourceRows(AnalyticsReport report)
        {
            List<string[]> rows = new List<string[]>();
            rows.Add(new[]
            {
                "Slot",
                "SourceId",
                "Count",
                "Min",
                "Max",
                "Mean",
                "Median",
                "StdDev",
                "CVPercent",
                "PeakToPeak",
                "First",
                "Last",
                "DriftPercent",
                "Missing",
                "Zero",
                "MeanInstabilityPercent",
                "MaxInstabilityPercent",
                "MeanStabilityScorePercent",
                "WorstStabilityScorePercent",
                "StationaryPercent"
            });
            AppendSource(rows, report.FirstSource);
            AppendSource(rows, report.SecondSource);
            return rows;
        }

        private static void AppendSource(List<string[]> rows, SourceStatistics source)
        {
            if (source == null)
            {
                return;
            }

            rows.Add(new[]
            {
                source.Slot,
                source.SourceId,
                source.Count.ToString(CultureInfo.InvariantCulture),
                FormatNumber(source.Min),
                FormatNumber(source.Max),
                FormatNumber(source.Mean),
                FormatNumber(source.Median),
                FormatNumber(source.StandardDeviation),
                FormatNumber(source.CoefficientOfVariationPercent),
                FormatNumber(source.PeakToPeak),
                FormatNumber(source.FirstValue),
                FormatNumber(source.LastValue),
                FormatNumber(source.DriftPercent),
                source.MissingCount.ToString(CultureInfo.InvariantCulture),
                source.ZeroCount.ToString(CultureInfo.InvariantCulture),
                FormatNumber(source.MeanStabilityPercent),
                FormatNumber(source.MaxStabilityPercent),
                FormatNumber(ToStabilityScore(source.MeanStabilityPercent)),
                FormatNumber(ToStabilityScore(source.MaxStabilityPercent)),
                FormatNumber(source.StationaryPercent)
            });
        }

        private static List<string[]> BuildComparisonRows(AnalyticsReport report)
        {
            ComparisonStatistics comparison = report.Comparison ?? new ComparisonStatistics();
            return new List<string[]>
            {
                new[] { "Metric", "Value" },
                new[] { "PairedCount", comparison.PairedCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "MeanRatio", FormatNumber(comparison.MeanRatio) },
                new[] { "MeanDelta", FormatNumber(comparison.MeanDelta) },
                new[] { "MeanDeltaPercent", FormatNumber(comparison.MeanDeltaPercent) },
                new[] { "Correlation", FormatNumber(comparison.Correlation) },
                new[] { "AverageAbsoluteDelta", FormatNumber(comparison.AverageAbsoluteDelta) }
            };
        }

        private static List<string[]> BuildStationaryRows(AnalyticsReport report)
        {
            StationaryStatistics stationary = report.Stationary ?? new StationaryStatistics();
            return new List<string[]>
            {
                new[] { "Metric", "Value" },
                new[] { "SegmentCount", stationary.SegmentCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "TotalDurationSeconds", FormatNumber(stationary.TotalDurationSeconds) },
                new[] { "LongestDurationSeconds", FormatNumber(stationary.LongestDurationSeconds) },
                new[] { "AverageDurationSeconds", FormatNumber(stationary.AverageDurationSeconds) },
                new[] { "AverageEntryInstabilityPercent", FormatNumber(stationary.AverageEntryStabilityPercent) },
                new[] { "AverageEntryStabilityScorePercent", FormatNumber(ToStabilityScore(stationary.AverageEntryStabilityPercent)) },
                new[] { "AverageExitInstabilityPercent", FormatNumber(stationary.AverageExitStabilityPercent) },
                new[] { "AverageExitStabilityScorePercent", FormatNumber(ToStabilityScore(stationary.AverageExitStabilityPercent)) },
                new[] { "BothStationaryPercent", FormatNumber(stationary.BothStationaryPercent) }
            };
        }

        private static List<string[]> BuildChartRows(AnalyticsReport report)
        {
            List<string[]> rows = new List<string[]>();
            rows.Add(
                new[]
                {
                    "RecordId",
                    "TimestampUtc",
                    "FirstEnergy",
                    "SecondEnergy",
                    "FirstInstabilityPercent",
                    "SecondInstabilityPercent",
                    "OverallInstabilityPercent",
                    "FirstStabilityScorePercent",
                    "SecondStabilityScorePercent",
                    "OverallStabilityScorePercent"
                });
            foreach (AnalyticsTimePoint point in report.ChartPoints)
            {
                rows.Add(new[]
                {
                    point.RecordId.ToString(CultureInfo.InvariantCulture),
                    FormatDate(point.TimestampUtc),
                    FormatNumber(point.FirstEnergy),
                    FormatNumber(point.SecondEnergy),
                    FormatNumber(point.FirstStabilityPercent),
                    FormatNumber(point.SecondStabilityPercent),
                    FormatNumber(point.OverallStabilityPercent),
                    FormatNumber(ToStabilityScore(point.FirstStabilityPercent)),
                    FormatNumber(ToStabilityScore(point.SecondStabilityPercent)),
                    FormatNumber(ToStabilityScore(point.OverallStabilityPercent))
                });
            }

            return rows;
        }

        private static void AppendSheet(WorkbookPart workbookPart, Sheets sheets, ref uint sheetId, string sheetName, List<string[]> rows)
        {
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            using (OpenXmlWriter writer = OpenXmlWriter.Create(worksheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());

                uint rowIndex = 1;
                foreach (string[] row in rows)
                {
                    writer.WriteStartElement(new Row { RowIndex = rowIndex });
                    for (int i = 0; i < row.Length; i++)
                    {
                        WriteCell(writer, row[i]);
                    }

                    writer.WriteEndElement();
                    rowIndex += 1;
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            sheets.Append(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = sheetId,
                    Name = sheetName
                });

            sheetId += 1;
        }

        private static void WriteCell(OpenXmlWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteElement(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(string.Empty)) });
                return;
            }

            double numericValue;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numericValue))
            {
                writer.WriteElement(new Cell { DataType = CellValues.Number, CellValue = new CellValue(value) });
                return;
            }

            writer.WriteElement(
                new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve })
                });
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string FormatNumber(double? value)
        {
            return value.HasValue ? value.Value.ToString("G17", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static double? ToStabilityScore(double? instabilityPercent)
        {
            if (!instabilityPercent.HasValue)
            {
                return null;
            }

            if (double.IsNaN(instabilityPercent.Value) || double.IsInfinity(instabilityPercent.Value))
            {
                return 0.0d;
            }

            return Math.Max(0.0d, Math.Min(100.0d, 100.0d - instabilityPercent.Value));
        }
    }
}
