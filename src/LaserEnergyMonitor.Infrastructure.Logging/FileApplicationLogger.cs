using System;
using System.IO;
using System.Text;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Logging
{
    public sealed class FileApplicationLogger : IApplicationLogger
    {
        private readonly object _gate = new object();
        private readonly string _logPath;

        public FileApplicationLogger(string logPath)
        {
            _logPath = logPath;
            string directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warning(string message)
        {
            Write("WARN", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            lock (_gate)
            {
                File.AppendAllText(
                    _logPath,
                    string.Format(
                        "{0:u} [{1}] {2}{3}",
                        DateTime.UtcNow,
                        level,
                        message,
                        Environment.NewLine),
                    Encoding.UTF8);
            }
        }
    }
}
