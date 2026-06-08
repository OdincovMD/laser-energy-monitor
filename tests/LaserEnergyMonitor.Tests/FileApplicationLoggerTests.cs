using System;
using System.IO;
using System.Text;
using LaserEnergyMonitor.Infrastructure.Logging;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class FileApplicationLoggerTests
    {
        [Fact]
        public void Info_WritesUtf8Text()
        {
            string directory = Path.Combine(Path.GetTempPath(), "LaserEnergyMonitorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string logPath = Path.Combine(directory, "application.log");

            try
            {
                FileApplicationLogger logger = new FileApplicationLogger(logPath);
                logger.Info("Capture directory: C:\\Users\\User\\Desktop\\Ваня тест");

                string text = File.ReadAllText(logPath, Encoding.UTF8);

                Assert.Contains("Ваня тест", text);
                Assert.Contains("[INFO]", text);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
