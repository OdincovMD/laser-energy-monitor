using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class StarLabLogMeasurementSource : IMeasurementSource
    {
        private readonly object _gate = new object();
        private readonly StarLabLogMeasurementOptions _options;
        private CancellationTokenSource _cts;
        private Task _readerTask;
        private StarLabLogParser _parser;
        private long _position;
        private long _sequence;
        private string _pendingText;
        private bool _streaming;
        private bool _disposed;

        public StarLabLogMeasurementSource(StarLabLogMeasurementOptions options)
        {
            _options = options ?? throw new ArgumentNullException("options");
            _pendingText = string.Empty;
        }

        public string SourceId
        {
            get { return "Ophir"; }
        }

        public bool IsConnected { get; private set; }

        public event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        public event EventHandler<DeviceFaultEventArgs> Faulted;

        public void Initialize()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (IsConnected)
                {
                    return;
                }

                ValidateLogFilePath();
                IsConnected = true;
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!IsConnected)
                {
                    throw new InvalidOperationException("StarLab log source is not initialized.");
                }

                if (_streaming)
                {
                    return;
                }

                _parser = CreatePrimedParser(_options.LogFilePath);
                _position = _options.StartAtEnd ? GetFileLength(_options.LogFilePath) : 0L;
                _sequence = 0L;
                _pendingText = string.Empty;
                _cts = new CancellationTokenSource();
                _streaming = true;
                _readerTask = Task.Factory.StartNew(
                    () => ReadLoop(_cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        public void Stop()
        {
            CancellationTokenSource cts = null;
            Task readerTask = null;

            lock (_gate)
            {
                if (!_streaming)
                {
                    return;
                }

                _streaming = false;
                cts = _cts;
                readerTask = _readerTask;
                _cts = null;
                _readerTask = null;
                if (cts != null)
                {
                    cts.Cancel();
                }
            }

            if (readerTask != null)
            {
                try
                {
                    readerTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }
            }

            if (cts != null)
            {
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            IsConnected = false;
        }

        private void ReadLoop(CancellationToken token)
        {
            try
            {
                TimeSpan interval = GetEffectivePollInterval();
                while (!token.IsCancellationRequested)
                {
                    ReadAvailableLines();
                    if (token.WaitHandle.WaitOne(interval))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Faulted?.Invoke(
                    this,
                    new DeviceFaultEventArgs(
                        new DeviceFault
                        {
                            SourceId = SourceId,
                            Severity = FaultSeverity.Critical,
                            ReasonCode = "starlab-log-read-failed",
                            Message = "StarLab log read failed: " + ex.Message,
                            TimestampUtc = DateTime.UtcNow,
                            Exception = ex
                        }));
            }
        }

        private void ReadAvailableLines()
        {
            long length = GetFileLength(_options.LogFilePath);
            if (length < _position)
            {
                _parser = new StarLabLogParser(_options.EnergyColumnName);
                _position = 0L;
                _pendingText = string.Empty;
            }

            if (length == _position)
            {
                return;
            }

            string text = ReadTextFromPosition(_options.LogFilePath, _position, out long newPosition);
            _position = newPosition;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            PublishCompleteLines(text);
        }

        private void PublishCompleteLines(string text)
        {
            string combined = _pendingText + text;
            int lastLineEnd = Math.Max(combined.LastIndexOf('\n'), combined.LastIndexOf('\r'));
            if (lastLineEnd < 0)
            {
                _pendingText = combined;
                return;
            }

            string complete = combined.Substring(0, lastLineEnd + 1);
            _pendingText = combined.Substring(lastLineEnd + 1);

            string[] lines = complete.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                StarLabLogSample parsed;
                if (_parser.TryProcessLine(lines[i], out parsed))
                {
                    MeasurementReceived?.Invoke(
                        this,
                        new MeasurementReceivedEventArgs(
                            new MeasurementSample
                            {
                                SourceId = SourceId,
                                SequenceNumber = Interlocked.Increment(ref _sequence),
                                TimestampUtc = parsed.TimestampUtc,
                                MonotonicTicks = DateTime.UtcNow.Ticks,
                                Energy = parsed.Energy
                            }));
                }
            }
        }

        private StarLabLogParser CreatePrimedParser(string logFilePath)
        {
            StarLabLogParser parser = new StarLabLogParser(_options.EnergyColumnName);
            if (!File.Exists(logFilePath))
            {
                return parser;
            }

            string[] lines = ReadAllLinesShared(logFilePath);
            for (int i = 0; i < lines.Length; i++)
            {
                StarLabLogSample ignored;
                parser.TryProcessLine(lines[i], out ignored);
            }

            return parser;
        }

        private void ValidateLogFilePath()
        {
            if (string.IsNullOrWhiteSpace(_options.LogFilePath))
            {
                throw new InvalidOperationException("Select a StarLab log file before starting acquisition.");
            }

            if (!File.Exists(_options.LogFilePath))
            {
                throw new FileNotFoundException("StarLab log file was not found.", _options.LogFilePath);
            }
        }

        private TimeSpan GetEffectivePollInterval()
        {
            return _options.PollInterval > TimeSpan.Zero
                ? _options.PollInterval
                : StarLabLogMeasurementOptions.Default.PollInterval;
        }

        private static long GetFileLength(string path)
        {
            return new FileInfo(path).Length;
        }

        private static string ReadTextFromPosition(string path, long position, out long newPosition)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(position, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream, Encoding.Default, true))
                {
                    string text = reader.ReadToEnd();
                    newPosition = stream.Position;
                    return text;
                }
            }
        }

        private static string[] ReadAllLinesShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.Default, true))
            {
                List<string> lines = new List<string>();
                while (!reader.EndOfStream)
                {
                    lines.Add(reader.ReadLine());
                }

                return lines.ToArray();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }

    public sealed class StarLabLogParser
    {
        private static readonly string[] BaseTimestampFormats =
        {
            "dd/MM/yyyy 'at' HH:mm:ss.FFFFFF",
            "d/M/yyyy 'at' H:mm:ss.FFFFFF",
            "dd/MM/yyyy 'at' HH:mm:ss",
            "d/M/yyyy 'at' H:mm:ss"
        };

        private readonly string _preferredEnergyColumnName;
        private string[] _columns;
        private int _timestampIndex = -1;
        private int _energyIndex = -1;
        private DateTime? _firstPulseUtc;

        public StarLabLogParser(string preferredEnergyColumnName)
        {
            _preferredEnergyColumnName = string.IsNullOrWhiteSpace(preferredEnergyColumnName)
                ? StarLabLogMeasurementOptions.Default.EnergyColumnName
                : preferredEnergyColumnName.Trim();
        }

        public string[] Columns
        {
            get { return _columns ?? new string[0]; }
        }

        public string EnergyColumnName
        {
            get
            {
                return _energyIndex >= 0 && _columns != null && _energyIndex < _columns.Length
                    ? _columns[_energyIndex]
                    : string.Empty;
            }
        }

        public bool TryProcessLine(string line, out StarLabLogSample sample)
        {
            sample = null;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            TryReadFirstPulseTimestamp(line);
            if (TryReadHeader(line))
            {
                return false;
            }

            if (_timestampIndex < 0 || _energyIndex < 0)
            {
                return false;
            }

            string[] cells = SplitColumns(line);
            if (cells.Length <= Math.Max(_timestampIndex, _energyIndex))
            {
                return false;
            }

            double relativeSeconds;
            double energy;
            if (!double.TryParse(cells[_timestampIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out relativeSeconds) ||
                !double.TryParse(cells[_energyIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out energy))
            {
                return false;
            }

            DateTime timestampUtc = _firstPulseUtc.HasValue
                ? _firstPulseUtc.Value.AddSeconds(relativeSeconds)
                : DateTime.UtcNow;

            sample = new StarLabLogSample
            {
                TimestampUtc = timestampUtc,
                Energy = energy
            };
            return true;
        }

        private void TryReadFirstPulseTimestamp(string line)
        {
            const string prefix = ";First Pulse Arrived :";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string value = line.Substring(prefix.Length).Trim();
            DateTime localTime;
            if (DateTime.TryParseExact(
                value,
                BaseTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out localTime))
            {
                _firstPulseUtc = localTime.ToUniversalTime();
            }
        }

        private bool TryReadHeader(string line)
        {
            string[] cells = SplitColumns(line);
            int timestampIndex = IndexOf(cells, "Timestamp");
            if (timestampIndex < 0)
            {
                return false;
            }

            int energyIndex = ResolveEnergyColumn(cells);
            if (energyIndex < 0)
            {
                return false;
            }

            _columns = cells;
            _timestampIndex = timestampIndex;
            _energyIndex = energyIndex;
            return true;
        }

        private int ResolveEnergyColumn(string[] cells)
        {
            int preferred = IndexOf(cells, _preferredEnergyColumnName);
            if (preferred >= 0)
            {
                return preferred;
            }

            int math = IndexOf(cells, "Math M");
            if (math >= 0)
            {
                return math;
            }

            int channelA = IndexOf(cells, "Channel A");
            if (channelA >= 0)
            {
                return channelA;
            }

            return cells.Length > 1 ? cells.Length - 1 : -1;
        }

        private static int IndexOf(string[] cells, string value)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (string.Equals(cells[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string[] SplitColumns(string line)
        {
            string[] raw = line.IndexOf('\t') >= 0
                ? line.Split('\t')
                : Regex.Split(line.Trim(), @"\s{2,}");

            string[] cells = raw
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToArray();

            if (cells.Length <= 1 && line.IndexOf('\t') < 0)
            {
                cells = Regex.Split(line.Trim(), @"\s+")
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToArray();
            }

            return cells;
        }
    }

    public sealed class StarLabLogSample
    {
        public DateTime TimestampUtc { get; set; }

        public double Energy { get; set; }
    }
}
