using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Excel
{
    public sealed class PrototypeExcelExporter : IMeasurementExporter
    {
        private StreamWriter _rawWriter;
        private StreamWriter _eventsWriter;
        private StreamWriter _summaryWriter;
        private StreamWriter _stationaryWriter;
        private string _basePath;
        private string _requestedPath;

        public void StartSession(SessionMetadata metadata, SessionSettings settings)
        {
            _requestedPath = settings != null ? settings.OutputPath : null;
            if (string.IsNullOrWhiteSpace(_requestedPath))
            {
                _requestedPath = Path.Combine(Environment.CurrentDirectory, "output", "measurement-session.xlsx");
            }

            string directory = Path.GetDirectoryName(_requestedPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            Directory.CreateDirectory(directory);
            _basePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(_requestedPath));
            _rawWriter = new StreamWriter(_basePath + ".RawData.csv", false);
            _eventsWriter = new StreamWriter(_basePath + ".Events.csv", false);
            _summaryWriter = new StreamWriter(_basePath + ".Summary.csv", false);
            _stationaryWriter = new StreamWriter(_basePath + ".Stationary.csv", false);

            _rawWriter.WriteLine("PairId,TimestampUtc,FirstSourceId,FirstSequence,FirstEnergy,SecondSourceId,SecondSequence,SecondEnergy,DeltaMs,FirstAverage,SecondAverage,StabilityMetric,IsStationary");
            _eventsWriter.WriteLine("TimestampUtc,EventType,ReasonCode,SequenceNumber,MetricValue,Message");
            _summaryWriter.WriteLine("Key,Value");
            _stationaryWriter.WriteLine("SegmentId,EntryPairId,EntryTimestampUtc,EntryFirstEnergy,EntrySecondEnergy,EntryFirstAverage,EntrySecondAverage,EntryStabilityMetric,ExitPairId,ExitTimestampUtc,ExitStabilityMetric,DurationMs,ExitReason");
            _summaryWriter.WriteLine("PrototypeExporter,CSV shadow files are written until Open XML integration is connected.");
            _summaryWriter.WriteLine("SessionName," + Escape(metadata != null ? metadata.SessionName : "Measurement Session"));
            _summaryWriter.WriteLine("StartedUtc," + (metadata != null ? metadata.StartedUtc.ToString("u") : string.Empty));
            _summaryWriter.WriteLine("DesynchronizationPolicyAction," + Escape(settings != null ? settings.DesynchronizationPolicyAction.ToString() : string.Empty));
            _summaryWriter.WriteLine("MaxConsecutiveDesynchronizations," + (settings != null ? settings.MaxConsecutiveDesynchronizations.ToString(CultureInfo.InvariantCulture) : string.Empty));
        }

        public void WriteMeasurement(SynchronizedMeasurementPair pair, StationarityUpdate update)
        {
            if (_rawWriter == null)
            {
                return;
            }

            _rawWriter.WriteLine(
                string.Join(
                    ",",
                    pair.PairId.ToString(CultureInfo.InvariantCulture),
                    pair.FirstSample.TimestampUtc.ToString("u"),
                    Escape(pair.FirstSample.SourceId),
                    pair.FirstSample.SequenceNumber.ToString(CultureInfo.InvariantCulture),
                    pair.FirstSample.Energy.ToString("G17", CultureInfo.InvariantCulture),
                    Escape(pair.SecondSample.SourceId),
                    pair.SecondSample.SequenceNumber.ToString(CultureInfo.InvariantCulture),
                    pair.SecondSample.Energy.ToString("G17", CultureInfo.InvariantCulture),
                    pair.Delta.TotalMilliseconds.ToString("G17", CultureInfo.InvariantCulture),
                    update.RollingAverageFirst.ToString("G17", CultureInfo.InvariantCulture),
                    update.RollingAverageSecond.ToString("G17", CultureInfo.InvariantCulture),
                    update.StabilityMetric.ToString("G17", CultureInfo.InvariantCulture),
                    update.IsStationary ? "1" : "0"));
        }

        public void WriteEvent(SessionEvent sessionEvent)
        {
            if (_eventsWriter == null)
            {
                return;
            }

            _eventsWriter.WriteLine(
                string.Join(
                    ",",
                    sessionEvent.TimestampUtc.ToString("u"),
                    sessionEvent.EventType.ToString(),
                    Escape(sessionEvent.ReasonCode),
                    sessionEvent.SequenceNumber.HasValue ? sessionEvent.SequenceNumber.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    sessionEvent.MetricValue.HasValue ? sessionEvent.MetricValue.Value.ToString("G17", CultureInfo.InvariantCulture) : string.Empty,
                    Escape(sessionEvent.Message)));
        }

        public void WriteStationarySegment(StationarySegmentResult segment)
        {
            if (_stationaryWriter == null || segment == null)
            {
                return;
            }

            _stationaryWriter.WriteLine(
                string.Join(
                    ",",
                    segment.SegmentId.ToString(CultureInfo.InvariantCulture),
                    segment.EntryPairId.ToString(CultureInfo.InvariantCulture),
                    segment.EntryTimestampUtc.ToString("u"),
                    segment.EntryFirstEnergy.ToString("G17", CultureInfo.InvariantCulture),
                    segment.EntrySecondEnergy.ToString("G17", CultureInfo.InvariantCulture),
                    segment.EntryFirstAverage.ToString("G17", CultureInfo.InvariantCulture),
                    segment.EntrySecondAverage.ToString("G17", CultureInfo.InvariantCulture),
                    segment.EntryStabilityMetric.ToString("G17", CultureInfo.InvariantCulture),
                    segment.ExitPairId.HasValue ? segment.ExitPairId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    segment.ExitTimestampUtc.HasValue ? segment.ExitTimestampUtc.Value.ToString("u") : string.Empty,
                    segment.ExitStabilityMetric.HasValue ? segment.ExitStabilityMetric.Value.ToString("G17", CultureInfo.InvariantCulture) : string.Empty,
                    segment.DurationMs.HasValue ? segment.DurationMs.Value.ToString("G17", CultureInfo.InvariantCulture) : string.Empty,
                    Escape(segment.ExitReason)));
        }

        public void Complete(SessionSummary summary)
        {
            if (_summaryWriter != null && summary != null)
            {
                _summaryWriter.WriteLine("FinishedUtc," + summary.FinishedUtc.ToString("u"));
                _summaryWriter.WriteLine("PairCount," + summary.PairCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("EventCount," + summary.EventCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("DesynchronizationCount," + summary.DesynchronizationCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("FaultCount," + summary.FaultCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("StationarySegmentCount," + summary.StationarySegmentCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("ClosedStationarySegmentCount," + summary.ClosedStationarySegmentCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("LastDesynchronizationUtc," + (summary.LastDesynchronizationUtc.HasValue ? summary.LastDesynchronizationUtc.Value.ToString("u") : string.Empty));
                _summaryWriter.WriteLine("LastFaultUtc," + (summary.LastFaultUtc.HasValue ? summary.LastFaultUtc.Value.ToString("u") : string.Empty));
                _summaryWriter.WriteLine("CompletedNormally," + (summary.CompletedNormally ? "1" : "0"));
                _summaryWriter.WriteLine("FinalState," + Escape(summary.FinalState));
                _summaryWriter.WriteLine("TerminationReasonCode," + Escape(summary.TerminationReasonCode));
                _summaryWriter.WriteLine("TerminationReason," + Escape(summary.TerminationReason));
            }

            CloseShadowWriters();
            BuildWorkbookFromShadowFiles();
        }

        public void Abort(SessionSummary summary, string reason)
        {
            if (_summaryWriter != null)
            {
                if (summary != null)
                {
                    _summaryWriter.WriteLine("FinishedUtc," + summary.FinishedUtc.ToString("u"));
                    _summaryWriter.WriteLine("PairCount," + summary.PairCount.ToString(CultureInfo.InvariantCulture));
                    _summaryWriter.WriteLine("EventCount," + summary.EventCount.ToString(CultureInfo.InvariantCulture));
                    _summaryWriter.WriteLine("DesynchronizationCount," + summary.DesynchronizationCount.ToString(CultureInfo.InvariantCulture));
                    _summaryWriter.WriteLine("FaultCount," + summary.FaultCount.ToString(CultureInfo.InvariantCulture));
                    _summaryWriter.WriteLine("StationarySegmentCount," + summary.StationarySegmentCount.ToString(CultureInfo.InvariantCulture));
                    _summaryWriter.WriteLine("ClosedStationarySegmentCount," + summary.ClosedStationarySegmentCount.ToString(CultureInfo.InvariantCulture));
                    _summaryWriter.WriteLine("LastDesynchronizationUtc," + (summary.LastDesynchronizationUtc.HasValue ? summary.LastDesynchronizationUtc.Value.ToString("u") : string.Empty));
                    _summaryWriter.WriteLine("LastFaultUtc," + (summary.LastFaultUtc.HasValue ? summary.LastFaultUtc.Value.ToString("u") : string.Empty));
                    _summaryWriter.WriteLine("CompletedNormally," + (summary.CompletedNormally ? "1" : "0"));
                    _summaryWriter.WriteLine("FinalState," + Escape(summary.FinalState));
                    _summaryWriter.WriteLine("TerminationReasonCode," + Escape(summary.TerminationReasonCode));
                    _summaryWriter.WriteLine("TerminationReason," + Escape(summary.TerminationReason));
                }

                _summaryWriter.WriteLine("Aborted,1");
                _summaryWriter.WriteLine("AbortReason," + Escape(reason));
            }

            CloseShadowWriters();
            BuildWorkbookFromShadowFiles();
        }

        public void Dispose()
        {
            _requestedPath = null;
            _basePath = null;
            CloseShadowWriters();
        }

        private void CloseShadowWriters()
        {
            DisposeWriter(ref _rawWriter);
            DisposeWriter(ref _eventsWriter);
            DisposeWriter(ref _summaryWriter);
            DisposeWriter(ref _stationaryWriter);
        }

        private void BuildWorkbookFromShadowFiles()
        {
            if (string.IsNullOrWhiteSpace(_requestedPath) || string.IsNullOrWhiteSpace(_basePath))
            {
                return;
            }

            string workbookPath = _requestedPath;
            string workbookDirectory = Path.GetDirectoryName(workbookPath);
            if (!string.IsNullOrWhiteSpace(workbookDirectory))
            {
                Directory.CreateDirectory(workbookDirectory);
            }

            if (File.Exists(workbookPath))
            {
                File.Delete(workbookPath);
            }

            using (SpreadsheetDocument document = SpreadsheetDocument.Create(workbookPath, SpreadsheetDocumentType.Workbook))
            {
                WorkbookPart workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;

                AppendSheetFromCsv(workbookPart, sheets, ref sheetId, "RawData", _basePath + ".RawData.csv");
                AppendSheetFromCsv(workbookPart, sheets, ref sheetId, "Events", _basePath + ".Events.csv");
                AppendSheetFromCsv(workbookPart, sheets, ref sheetId, "Summary", _basePath + ".Summary.csv");
                AppendSheetFromCsv(workbookPart, sheets, ref sheetId, "Stationary", _basePath + ".Stationary.csv");
                workbookPart.Workbook.Save();
            }
        }

        private static void AppendSheetFromCsv(
            WorkbookPart workbookPart,
            Sheets sheets,
            ref uint sheetId,
            string sheetName,
            string csvPath)
        {
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            using (OpenXmlWriter writer = OpenXmlWriter.Create(worksheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());

                if (File.Exists(csvPath))
                {
                    using (StreamReader reader = new StreamReader(csvPath))
                    {
                        string line;
                        uint rowIndex = 1;
                        while ((line = reader.ReadLine()) != null)
                        {
                            writer.WriteStartElement(new Row { RowIndex = rowIndex });
                            string[] values = ParseCsvLine(line);
                            for (int i = 0; i < values.Length; i++)
                            {
                                WriteCell(writer, values[i]);
                            }

                            writer.WriteEndElement();
                            rowIndex += 1;
                        }
                    }
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
                writer.WriteElement(
                    new Cell
                    {
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(string.Empty))
                    });
                return;
            }

            double numericValue;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numericValue))
            {
                writer.WriteElement(
                    new Cell
                    {
                        DataType = CellValues.Number,
                        CellValue = new CellValue(value)
                    });
                return;
            }

            writer.WriteElement(
                new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(
                        new Text(value)
                        {
                            Space = SpaceProcessingModeValues.Preserve
                        })
                });
        }

        private static string[] ParseCsvLine(string line)
        {
            if (line == null)
            {
                return Array.Empty<string>();
            }

            List<string> values = new List<string>();
            bool insideQuotes = false;
            int segmentStart = 0;
            System.Text.StringBuilder current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char currentChar = line[i];
                if (currentChar == '"')
                {
                    if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i += 1;
                        continue;
                    }

                    insideQuotes = !insideQuotes;
                    if (segmentStart == i)
                    {
                        segmentStart = i + 1;
                    }

                    continue;
                }

                if (currentChar == ',' && !insideQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                    segmentStart = i + 1;
                    continue;
                }

                current.Append(currentChar);
            }

            values.Add(current.ToString());
            return values.ToArray();
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.Contains(",") || value.Contains("\""))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static void DisposeWriter(ref StreamWriter writer)
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }
    }
}
