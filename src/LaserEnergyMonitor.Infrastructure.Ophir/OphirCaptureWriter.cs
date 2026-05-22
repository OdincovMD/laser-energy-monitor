using System;
using System.Globalization;
using System.IO;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal sealed class OphirCaptureWriter : IDisposable
    {
        private readonly object _gate = new object();
        private readonly string _capturePath;
        private bool _disposed;

        public OphirCaptureWriter(string captureDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(captureDirectoryPath))
            {
                throw new ArgumentException("Capture directory path is required.", "captureDirectoryPath");
            }

            Directory.CreateDirectory(captureDirectoryPath);
            _capturePath = Path.Combine(
                captureDirectoryPath,
                "ophir-capture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");

            File.WriteAllText(
                _capturePath,
                "sequence,recorded_utc,published_utc,vendor_timestamp_seconds,status_raw,measurement_type,status_code,energy" + Environment.NewLine);
        }

        public string CapturePath
        {
            get { return _capturePath; }
        }

        public void Append(long sequenceNumber, DateTime recordedUtc, DateTime publishedUtc, double vendorTimestampSeconds, int rawStatus, double energy)
        {
            int measurementType = rawStatus / 0x10000;
            int statusCode = rawStatus % 0x10000;

            lock (_gate)
            {
                ThrowIfDisposed();
                File.AppendAllText(
                    _capturePath,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1:O},{2:O},{3:0.000000},{4},{5},{6},{7:0.000000}{8}",
                        sequenceNumber,
                        recordedUtc,
                        publishedUtc,
                        vendorTimestampSeconds,
                        rawStatus,
                        measurementType,
                        statusCode,
                        energy,
                        Environment.NewLine));
            }
        }

        public void Dispose()
        {
            _disposed = true;
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
