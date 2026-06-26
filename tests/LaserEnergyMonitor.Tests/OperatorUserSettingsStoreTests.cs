using System;
using System.IO;
using LaserEnergyMonitor.Infrastructure;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class OperatorUserSettingsStoreTests
    {
        [Fact]
        public void Load_WhenSettingsFileDoesNotExist_ReturnsEmptySettings()
        {
            OperatorUserSettingsStore store = new OperatorUserSettingsStore(CreateSettingsPath());

            OperatorUserSettings settings = store.Load();

            Assert.NotNull(settings);
            Assert.True(string.IsNullOrEmpty(settings.OutputPath));
            Assert.True(string.IsNullOrEmpty(settings.StarLabLogPath));
            Assert.True(string.IsNullOrEmpty(settings.BeamGageDataSource));
        }

        [Fact]
        public void SaveAndLoad_RoundTripsOperatorSettings()
        {
            string path = CreateSettingsPath();
            OperatorUserSettingsStore store = new OperatorUserSettingsStore(path);

            store.Save(
                new OperatorUserSettings
                {
                    StarLabLogPath = @"C:\StarLab\Data_log.txt",
                    OutputPath = @"C:\Results\measurement-session.xlsx",
                    SessionName = "Prototype Session",
                    RollingWindowSize = "20",
                    EnterThresholdPercent = "0.5",
                    ExitThresholdPercent = "1.0",
                    BeamGageDataSource = "Beam Source 1"
                });

            OperatorUserSettings loaded = new OperatorUserSettingsStore(path).Load();

            Assert.Equal(@"C:\StarLab\Data_log.txt", loaded.StarLabLogPath);
            Assert.Equal(@"C:\Results\measurement-session.xlsx", loaded.OutputPath);
            Assert.Equal("Prototype Session", loaded.SessionName);
            Assert.Equal("20", loaded.RollingWindowSize);
            Assert.Equal("0.5", loaded.EnterThresholdPercent);
            Assert.Equal("1.0", loaded.ExitThresholdPercent);
            Assert.Equal("Beam Source 1", loaded.BeamGageDataSource);
        }

        private static string CreateSettingsPath()
        {
            string directory = Path.Combine(Path.GetTempPath(), "LaserEnergyMonitorTests", Guid.NewGuid().ToString("N"));
            return Path.Combine(directory, "operator-settings.xml");
        }
    }
}
