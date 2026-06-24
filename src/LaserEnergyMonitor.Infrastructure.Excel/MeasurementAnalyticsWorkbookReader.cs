using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace LaserEnergyMonitor.Infrastructure.Excel
{
    public sealed class MeasurementAnalyticsWorkbookReader
    {
        public MeasurementAnalyticsWorkbook Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Select a measurement workbook first.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Measurement workbook was not found.", path);
            }

            MeasurementAnalyticsWorkbook workbook = new MeasurementAnalyticsWorkbook { SourcePath = path };
            using (SpreadsheetDocument document = SpreadsheetDocument.Open(path, false))
            {
                WorkbookPart workbookPart = document.WorkbookPart;
                if (workbookPart == null || workbookPart.Workbook == null)
                {
                    throw new InvalidOperationException("The selected workbook does not contain a readable workbook part.");
                }

                List<string[]> rawRows = ReadSheet(workbookPart, "RawData");
                if (rawRows.Count == 0)
                {
                    throw new InvalidOperationException("RawData sheet was not found or is empty.");
                }

                ParseRawData(rawRows, workbook.RawRows);
                ParseSummary(ReadSheet(workbookPart, "Summary"), workbook);
                ParseEvents(ReadSheet(workbookPart, "Events"), workbook);
                ParseStationary(ReadSheet(workbookPart, "Stationary"), workbook);
            }

            if (workbook.RawRows.Count == 0)
            {
                throw new InvalidOperationException("RawData sheet does not contain measurement rows.");
            }

            return workbook;
        }

        private static List<string[]> ReadSheet(WorkbookPart workbookPart, string sheetName)
        {
            Sheet sheet = workbookPart.Workbook.Sheets.Elements<Sheet>()
                .FirstOrDefault(item => string.Equals(item.Name, sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet == null)
            {
                return new List<string[]>();
            }

            WorksheetPart worksheetPart = workbookPart.GetPartById(sheet.Id) as WorksheetPart;
            if (worksheetPart == null || worksheetPart.Worksheet == null)
            {
                return new List<string[]>();
            }

            SharedStringTable sharedStrings = workbookPart.SharedStringTablePart != null
                ? workbookPart.SharedStringTablePart.SharedStringTable
                : null;

            List<string[]> rows = new List<string[]>();
            SheetData sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
            if (sheetData == null)
            {
                return rows;
            }

            foreach (Row row in sheetData.Elements<Row>())
            {
                List<string> values = new List<string>();
                foreach (Cell cell in row.Elements<Cell>())
                {
                    values.Add(GetCellText(cell, sharedStrings));
                }

                rows.Add(values.ToArray());
            }

            return rows;
        }

        private static string GetCellText(Cell cell, SharedStringTable sharedStrings)
        {
            if (cell == null)
            {
                return string.Empty;
            }

            if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
            {
                return cell.InlineString != null ? cell.InlineString.InnerText : string.Empty;
            }

            string text = cell.CellValue != null ? cell.CellValue.Text : string.Empty;
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                int index;
                if (sharedStrings != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                {
                    SharedStringItem item = sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(index);
                    return item != null ? item.InnerText : string.Empty;
                }
            }

            return text ?? string.Empty;
        }

        private static void ParseRawData(List<string[]> rows, List<AnalyticsRawDataRow> output)
        {
            Dictionary<string, int> headers = BuildHeaderIndex(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row.Length == 0)
                {
                    continue;
                }

                output.Add(
                    new AnalyticsRawDataRow
                    {
                        RecordId = ParseLong(Get(row, headers, "RecordId")) ?? 0L,
                        TimestampUtc = ParseDate(Get(row, headers, "TimestampUtc")),
                        SourceId = Get(row, headers, "SourceId"),
                        Sequence = ParseLong(Get(row, headers, "Sequence")),
                        Energy = ParseDouble(Get(row, headers, "Energy")),
                        SourceSlot = Get(row, headers, "SourceSlot"),
                        FirstAverage = ParseDouble(Get(row, headers, "FirstAverage")),
                        SecondAverage = ParseDouble(Get(row, headers, "SecondAverage")),
                        FirstStabilityMetricPercent = ParseDouble(Get(row, headers, "FirstStabilityMetricPercent")),
                        SecondStabilityMetricPercent = ParseDouble(Get(row, headers, "SecondStabilityMetricPercent")),
                        StabilityMetric = ParseDouble(Get(row, headers, "StabilityMetric")),
                        FirstIsStationary = ParseBool(Get(row, headers, "FirstIsStationary")),
                        SecondIsStationary = ParseBool(Get(row, headers, "SecondIsStationary")),
                        IsStationary = ParseBool(Get(row, headers, "IsStationary"))
                    });
            }
        }

        private static void ParseSummary(List<string[]> rows, MeasurementAnalyticsWorkbook workbook)
        {
            if (rows.Count == 0)
            {
                workbook.Warnings.Add("Summary sheet is missing.");
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Length >= 2 && !string.IsNullOrWhiteSpace(rows[i][0]))
                {
                    workbook.Summary[rows[i][0]] = rows[i][1];
                }
            }
        }

        private static void ParseEvents(List<string[]> rows, MeasurementAnalyticsWorkbook workbook)
        {
            if (rows.Count == 0)
            {
                workbook.Warnings.Add("Events sheet is missing.");
                return;
            }

            Dictionary<string, int> headers = BuildHeaderIndex(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                string[] row = rows[i];
                workbook.Events.Add(
                    new AnalyticsEventRow
                    {
                        TimestampUtc = ParseDate(Get(row, headers, "TimestampUtc")),
                        EventType = Get(row, headers, "EventType"),
                        ReasonCode = Get(row, headers, "ReasonCode"),
                        SequenceNumber = ParseLong(Get(row, headers, "SequenceNumber")),
                        MetricValue = ParseDouble(Get(row, headers, "MetricValue")),
                        Message = Get(row, headers, "Message")
                    });
            }
        }

        private static void ParseStationary(List<string[]> rows, MeasurementAnalyticsWorkbook workbook)
        {
            if (rows.Count == 0)
            {
                workbook.Warnings.Add("Stationary sheet is missing.");
                return;
            }

            Dictionary<string, int> headers = BuildHeaderIndex(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                string[] row = rows[i];
                workbook.StationarySegments.Add(
                    new AnalyticsStationarySegmentRow
                    {
                        SegmentId = (int)(ParseLong(Get(row, headers, "SegmentId")) ?? 0L),
                        EntryRecordId = ParseLong(Get(row, headers, "EntryRecordId")),
                        EntryTimestampUtc = ParseDate(Get(row, headers, "EntryTimestampUtc")),
                        EntryFirstEnergy = ParseDouble(Get(row, headers, "EntryFirstEnergy")),
                        EntrySecondEnergy = ParseDouble(Get(row, headers, "EntrySecondEnergy")),
                        EntryFirstAverage = ParseDouble(Get(row, headers, "EntryFirstAverage")),
                        EntrySecondAverage = ParseDouble(Get(row, headers, "EntrySecondAverage")),
                        EntryStabilityMetric = ParseDouble(Get(row, headers, "EntryStabilityMetric")),
                        ExitRecordId = ParseLong(Get(row, headers, "ExitRecordId")),
                        ExitTimestampUtc = ParseDate(Get(row, headers, "ExitTimestampUtc")),
                        ExitStabilityMetric = ParseDouble(Get(row, headers, "ExitStabilityMetric")),
                        DurationMs = ParseDouble(Get(row, headers, "DurationMs")),
                        ExitReason = Get(row, headers, "ExitReason")
                    });
            }
        }

        private static Dictionary<string, int> BuildHeaderIndex(string[] headerRow)
        {
            Dictionary<string, int> headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headerRow.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(headerRow[i]) && !headers.ContainsKey(headerRow[i]))
                {
                    headers.Add(headerRow[i], i);
                }
            }

            return headers;
        }

        private static string Get(string[] row, Dictionary<string, int> headers, string name)
        {
            int index;
            if (!headers.TryGetValue(name, out index) || index < 0 || index >= row.Length)
            {
                return string.Empty;
            }

            return row[index];
        }

        private static double? ParseDouble(string value)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : (double?)null;
        }

        private static long? ParseLong(string value)
        {
            long parsed;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : (long?)null;
        }

        private static DateTime? ParseDate(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed)
                    ? parsed
                    : (DateTime?)null;
        }

        private static bool? ParseBool(string value)
        {
            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }
    }
}
