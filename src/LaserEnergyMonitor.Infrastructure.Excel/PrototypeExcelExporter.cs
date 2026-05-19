using System;
using System.Globalization;
using System.IO;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Excel
{
    public sealed class PrototypeExcelExporter : IMeasurementExporter
    {
        private StreamWriter _rawWriter;
        private StreamWriter _eventsWriter;
        private StreamWriter _summaryWriter;
        private string _basePath;

        public void StartSession(SessionMetadata metadata, SessionSettings settings)
        {
            string requestedPath = settings != null ? settings.OutputPath : null;
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                requestedPath = Path.Combine(Environment.CurrentDirectory, "output", "measurement-session.xlsx");
            }

            string directory = Path.GetDirectoryName(requestedPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            Directory.CreateDirectory(directory);
            _basePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(requestedPath));
            _rawWriter = new StreamWriter(_basePath + ".RawData.csv", false);
            _eventsWriter = new StreamWriter(_basePath + ".Events.csv", false);
            _summaryWriter = new StreamWriter(_basePath + ".Summary.csv", false);

            _rawWriter.WriteLine("PairId,TimestampUtc,FirstSourceId,FirstSequence,FirstEnergy,SecondSourceId,SecondSequence,SecondEnergy,DeltaMs,FirstAverage,SecondAverage,StabilityMetric,IsStationary");
            _eventsWriter.WriteLine("TimestampUtc,EventType,SequenceNumber,MetricValue,Message");
            _summaryWriter.WriteLine("Key,Value");
            _summaryWriter.WriteLine("PrototypeExporter,CSV shadow files are written until Open XML integration is connected.");
            _summaryWriter.WriteLine("SessionName," + Escape(metadata != null ? metadata.SessionName : "Measurement Session"));
            _summaryWriter.WriteLine("StartedUtc," + (metadata != null ? metadata.StartedUtc.ToString("u") : string.Empty));
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
                    sessionEvent.SequenceNumber.HasValue ? sessionEvent.SequenceNumber.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    sessionEvent.MetricValue.HasValue ? sessionEvent.MetricValue.Value.ToString("G17", CultureInfo.InvariantCulture) : string.Empty,
                    Escape(sessionEvent.Message)));
        }

        public void Complete(SessionSummary summary)
        {
            if (_summaryWriter != null && summary != null)
            {
                _summaryWriter.WriteLine("FinishedUtc," + summary.FinishedUtc.ToString("u"));
                _summaryWriter.WriteLine("PairCount," + summary.PairCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("EventCount," + summary.EventCount.ToString(CultureInfo.InvariantCulture));
                _summaryWriter.WriteLine("CompletedNormally," + (summary.CompletedNormally ? "1" : "0"));
                _summaryWriter.WriteLine("FinalState," + Escape(summary.FinalState));
            }

            Dispose();
        }

        public void Abort(string reason)
        {
            if (_summaryWriter != null)
            {
                _summaryWriter.WriteLine("Aborted,1");
                _summaryWriter.WriteLine("AbortReason," + Escape(reason));
            }

            Dispose();
        }

        public void Dispose()
        {
            DisposeWriter(ref _rawWriter);
            DisposeWriter(ref _eventsWriter);
            DisposeWriter(ref _summaryWriter);
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
