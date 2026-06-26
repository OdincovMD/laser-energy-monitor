using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure.Ophir;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class SessionPreflightServiceTests
    {
        [Fact]
        public void Run_WhenStarLabLogIsMissing_ReturnsFailedStarLabCheck()
        {
            SessionPreflightReport report = RunPreflight(Path.Combine(CreateTempDirectory(), "missing.txt"));

            PreflightCheckResult check = FindCheck(report, "StarLab log");

            Assert.Equal(PreflightCheckStatus.Failed, check.Status);
            Assert.Contains("not found", check.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Run_WhenStarLabLogIsSharedReadableAndMathColumnExists_ReturnsPassedStarLabCheck()
        {
            string path = CreateTempLogFile(CreateHeader("Math M"));
            using (FileStream lockedByWriter = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                SessionPreflightReport report = RunPreflight(path);

                PreflightCheckResult check = FindCheck(report, "StarLab log");

                Assert.Equal(PreflightCheckStatus.Passed, check.Status);
                Assert.Contains("Math M", check.Message);
            }
        }

        [Fact]
        public void Run_WhenPreferredStarLabColumnIsMissingButFallbackExists_ReturnsWarning()
        {
            string path = CreateTempLogFile(CreateHeader("Channel A"));

            SessionPreflightReport report = RunPreflight(path);

            PreflightCheckResult check = FindCheck(report, "StarLab log");
            Assert.Equal(PreflightCheckStatus.Warning, check.Status);
            Assert.Contains("fallback", check.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Channel A", check.Message);
        }

        [Fact]
        public void Run_WhenStarLabEnergyColumnIsMissing_ReturnsFailedStarLabCheck()
        {
            string path = CreateTempLogFile(
                ";PC Software:StarLab Version 2.40 Build 8" + Environment.NewLine +
                "Timestamp\tNot Energy" + Environment.NewLine);

            SessionPreflightReport report = RunPreflight(path);

            PreflightCheckResult check = FindCheck(report, "StarLab log");
            Assert.Equal(PreflightCheckStatus.Failed, check.Status);
            Assert.Contains("No usable", check.Message);
        }

        [Fact]
        public void Run_WhenExportFolderExists_ReturnsPassedExportCheck()
        {
            string directory = CreateTempDirectory();

            SessionPreflightReport report = RunPreflight(CreateTempLogFile(CreateHeader("Math M")), Path.Combine(directory, "session.xlsx"));

            PreflightCheckResult check = FindCheck(report, "Export path");
            Assert.Equal(PreflightCheckStatus.Passed, check.Status);
        }

        [Fact]
        public void Run_WhenExportFolderDoesNotExist_ReturnsWarningExportCheck()
        {
            string directory = Path.Combine(CreateTempDirectory(), "new-output");

            SessionPreflightReport report = RunPreflight(CreateTempLogFile(CreateHeader("Math M")), Path.Combine(directory, "session.xlsx"));

            PreflightCheckResult check = FindCheck(report, "Export path");
            Assert.Equal(PreflightCheckStatus.Warning, check.Status);
            Assert.Contains("will be created", check.Message);
        }

        [Fact]
        public void Run_WhenExportWorkbookIsLocked_ReturnsFailedExportCheck()
        {
            string outputPath = Path.Combine(CreateTempDirectory(), "locked.xlsx");
            File.WriteAllText(outputPath, "locked");
            using (FileStream lockedFile = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                SessionPreflightReport report = RunPreflight(CreateTempLogFile(CreateHeader("Math M")), outputPath);

                PreflightCheckResult check = FindCheck(report, "Export path");
                Assert.Equal(PreflightCheckStatus.Failed, check.Status);
                Assert.Contains("Close Excel", check.Message);
            }
        }

        private static SessionPreflightReport RunPreflight(string starLabLogPath)
        {
            return RunPreflight(starLabLogPath, Path.Combine(CreateTempDirectory(), "session.xlsx"));
        }

        private static SessionPreflightReport RunPreflight(string starLabLogPath, string outputPath)
        {
            SessionPreflightService service = new SessionPreflightService(
                new StarLabLogPreflightProbe(),
                new FixedBeamGageProbe(new[] { "Source A" }));

            return service.Run(
                new SessionPreflightRequest
                {
                    Settings = new SessionSettings
                    {
                        SessionName = "Test",
                        RollingWindowSize = 20,
                        EnterThresholdPercent = 0.5d,
                        ExitThresholdPercent = 1.0d,
                        OutputPath = outputPath
                    },
                    StarLabLogPath = starLabLogPath,
                    StarLabEnergyColumnName = "Math M",
                    BeamGageSelectedSource = "Source A",
                    BuildConfiguration = "Release"
                });
        }

        private static PreflightCheckResult FindCheck(SessionPreflightReport report, string name)
        {
            return report.Checks.Single(check => check.Name == name);
        }

        private static string CreateTempLogFile(string contents)
        {
            string path = Path.Combine(CreateTempDirectory(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, contents);
            return path;
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "LaserEnergyMonitorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string CreateHeader(string energyColumn)
        {
            return
                ";PC Software:StarLab Version 2.40 Build 8" + Environment.NewLine +
                ";First Pulse Arrived : 16/06/2026 at 10:43:36.983876" + Environment.NewLine +
                "Timestamp\t" + energyColumn + Environment.NewLine;
        }

        private sealed class FixedBeamGageProbe : IBeamGagePreflightProbe
        {
            private readonly IReadOnlyList<string> _sources;

            public FixedBeamGageProbe(IReadOnlyList<string> sources)
            {
                _sources = sources;
            }

            public BeamGagePreflightProbeResult Inspect()
            {
                return new BeamGagePreflightProbeResult
                {
                    DependencyAvailable = true,
                    PhysicalSources = _sources,
                    Details = "Test probe."
                };
            }
        }
    }
}
