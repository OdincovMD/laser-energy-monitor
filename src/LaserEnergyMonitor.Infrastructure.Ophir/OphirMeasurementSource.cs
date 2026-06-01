using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class OphirMeasurementSource : IMeasurementSource
    {
        private const int MaxStatusPreviewEntries = 12;
        private readonly object _gate = new object();
        private readonly OphirMeasurementOptions _options;
        private readonly List<int> _statusPreview = new List<int>();
        private CancellationTokenSource _cts;
        private Task _pollingTask;
        private StaWorker _staWorker;
        private IOphirRuntimeSession _session;
        private OphirCaptureWriter _captureWriter;
        private string _lastCapturePath;
        private int _rawSampleCount;
        private int _acceptedSampleCount;
        private int _nonZeroStatusCount;
        private long _sequence;
        private bool _streaming;
        private bool _disposed;

        public OphirMeasurementSource()
            : this(null)
        {
        }

        public OphirMeasurementSource(OphirMeasurementOptions options)
        {
            _options = options ?? OphirMeasurementOptions.Default;
        }

        public string SourceId
        {
            get { return "Ophir"; }
        }

        public bool IsConnected { get; private set; }

        public string CurrentSerialNumber { get; private set; }

        public int? CurrentChannel { get; private set; }

        public string LastCapturePath
        {
            get
            {
                lock (_gate)
                {
                    return _lastCapturePath;
                }
            }
        }

        public int RawSampleCount
        {
            get
            {
                lock (_gate)
                {
                    return _rawSampleCount;
                }
            }
        }

        public int AcceptedSampleCount
        {
            get
            {
                lock (_gate)
                {
                    return _acceptedSampleCount;
                }
            }
        }

        public int NonZeroStatusCount
        {
            get
            {
                lock (_gate)
                {
                    return _nonZeroStatusCount;
                }
            }
        }

        public string StatusPreview
        {
            get
            {
                lock (_gate)
                {
                    return BuildStatusPreview();
                }
            }
        }

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

                if (_staWorker == null)
                {
                    _staWorker = new StaWorker("Ophir COM worker");
                }

                try
                {
                    _session = _staWorker.Invoke(
                        delegate
                        {
                            return OpenRuntimeSession();
                        });
                    CurrentSerialNumber = _session.SerialNumber;
                    CurrentChannel = _session.Channel;
                    IsConnected = true;
                }
                catch
                {
                    if (_session == null && _staWorker != null)
                    {
                        _staWorker.Dispose();
                        _staWorker = null;
                    }

                    throw;
                }
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!IsConnected || _session == null)
                {
                    throw new InvalidOperationException("Ophir source is not initialized.");
                }

                if (_streaming)
                {
                    return;
                }

                EnsureCaptureWriter();
                _staWorker.Invoke(
                    delegate
                    {
                        _session.StartStream();
                    });
                _cts = new CancellationTokenSource();
                _pollingTask = Task.Factory.StartNew(
                    () => PollMeasurements(_cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                _streaming = true;
            }
        }

        public void Stop()
        {
            CancellationTokenSource cts = null;
            Task pollingTask = null;
            bool canWaitForTask = true;

            lock (_gate)
            {
                cts = _cts;
                pollingTask = _pollingTask;
                _cts = null;
                _pollingTask = null;
                _streaming = false;
                canWaitForTask = pollingTask == null || !Task.CurrentId.HasValue || pollingTask.Id != Task.CurrentId.Value;

                if (cts != null)
                {
                    cts.Cancel();
                }
            }

            if (pollingTask != null && canWaitForTask)
            {
                try
                {
                    pollingTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (cts != null)
            {
                cts.Dispose();
            }

            lock (_gate)
            {
                if (_session != null)
                {
                    _staWorker.Invoke(
                        delegate
                        {
                            _session.StopStream();
                        });
                }

                DisposeCaptureWriter();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;

            StaWorker staWorker = null;
            IOphirRuntimeSession session = null;
            lock (_gate)
            {
                staWorker = _staWorker;
                session = _session;
                _staWorker = null;
                _session = null;
                IsConnected = false;
            }

            if (staWorker != null)
            {
                if (session != null)
                {
                    staWorker.Invoke(
                        delegate
                        {
                            session.Dispose();
                        });
                }

                staWorker.Dispose();
            }
        }

        private void PollMeasurements(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    OphirDataBatch batch;
                    lock (_gate)
                    {
                        if (_session == null || _staWorker == null)
                        {
                            break;
                        }

                        batch = _staWorker.Invoke(
                            delegate
                            {
                                return _session.GetDataBatch();
                            });
                    }

                    if (batch.Count > 0)
                    {
                        PublishSamples(batch);
                    }

                    if (token.WaitHandle.WaitOne(_options.PollInterval))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Faulted?.Invoke(
                        this,
                        new DeviceFaultEventArgs(
                            new DeviceFault
                            {
                                SourceId = SourceId,
                                Severity = FaultSeverity.Critical,
                                Message = "Ophir streaming failed: " + ex.Message,
                                TimestampUtc = DateTime.UtcNow,
                                Exception = ex
                            }));
                    break;
                }
            }
        }

        private IOphirRuntimeSession OpenRuntimeSession()
        {
            if (_options.RuntimeBackend == OphirRuntimeBackend.PulsarFastX)
            {
                return OphirFastXRuntimeSession.Open(_options);
            }

            return OphirRuntimeSession.Open(_options);
        }

        private void PublishSamples(OphirDataBatch batch)
        {
            int sampleCount = batch.Count;
            for (int i = 0; i < sampleCount; i++)
            {
                int rawStatus = batch.Statuses[i];
                int measurementType = rawStatus / 0x10000;
                int status = rawStatus % 0x10000;
                bool accepted = measurementType == 0 && status == 0;
                DateTime publishedUtc = ResolveTimestampUtc(batch.RecordedUtc, batch.Timestamps[i]);
                long sequenceNumber = Interlocked.Increment(ref _sequence);
                RecordRawStatus(rawStatus, accepted);

                OphirCaptureWriter captureWriter = _captureWriter;
                if (captureWriter != null)
                {
                    captureWriter.Append(sequenceNumber, batch.RecordedUtc, publishedUtc, batch.Timestamps[i], rawStatus, batch.Energies[i]);
                }

                if (!accepted)
                {
                    continue;
                }

                MeasurementReceived?.Invoke(
                    this,
                        new MeasurementReceivedEventArgs(
                        new MeasurementSample
                        {
                            SourceId = SourceId,
                            SequenceNumber = sequenceNumber,
                            TimestampUtc = publishedUtc,
                            MonotonicTicks = batch.RecordedUtc.Ticks,
                            Energy = batch.Energies[i]
                        }));
            }
        }

        private DateTime ResolveTimestampUtc(DateTime recordedUtc, double vendorTimestampSeconds)
        {
            if (_options.TimestampStrategy == OphirTimestampStrategy.VendorSecondsFromLocalMidnight)
            {
                return ConvertVendorTimestampToUtc(recordedUtc, vendorTimestampSeconds);
            }

            return recordedUtc;
        }

        private static DateTime ConvertVendorTimestampToUtc(DateTime recordedUtc, double vendorTimestampSeconds)
        {
            if (vendorTimestampSeconds <= 0)
            {
                return recordedUtc;
            }

            try
            {
                DateTime localDate = recordedUtc.ToLocalTime().Date;
                return DateTime.SpecifyKind(localDate.AddSeconds(vendorTimestampSeconds), DateTimeKind.Local).ToUniversalTime();
            }
            catch
            {
                return recordedUtc;
            }
        }

        private void EnsureCaptureWriter()
        {
            if (_captureWriter != null || string.IsNullOrWhiteSpace(_options.CaptureDirectoryPath))
            {
                return;
            }

            _captureWriter = new OphirCaptureWriter(_options.CaptureDirectoryPath);
            _lastCapturePath = _captureWriter.CapturePath;
        }

        private void DisposeCaptureWriter()
        {
            if (_captureWriter == null)
            {
                return;
            }

            _captureWriter.Dispose();
            _captureWriter = null;
        }

        private void RecordRawStatus(int rawStatus, bool accepted)
        {
            lock (_gate)
            {
                _rawSampleCount += 1;
                if (accepted)
                {
                    _acceptedSampleCount += 1;
                }
                else
                {
                    _nonZeroStatusCount += 1;
                }

                if (_statusPreview.Count < MaxStatusPreviewEntries)
                {
                    _statusPreview.Add(rawStatus);
                }
            }
        }

        private string BuildStatusPreview()
        {
            if (_statusPreview.Count == 0)
            {
                return "n/a";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < _statusPreview.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                int rawStatus = _statusPreview[i];
                int measurementType = rawStatus / 0x10000;
                int statusCode = rawStatus % 0x10000;
                builder.Append(rawStatus.ToString(CultureInfo.InvariantCulture));
                builder.Append(" (type=");
                builder.Append(measurementType.ToString(CultureInfo.InvariantCulture));
                builder.Append(", status=");
                builder.Append(statusCode.ToString(CultureInfo.InvariantCulture));
                builder.Append(")");
            }

            return builder.ToString();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
