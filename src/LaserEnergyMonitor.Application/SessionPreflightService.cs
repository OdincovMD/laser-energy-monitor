using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Application
{
    public enum PreflightCheckStatus
    {
        Passed = 0,
        Warning = 1,
        Failed = 2
    }

    public sealed class PreflightCheckResult
    {
        public string Name { get; set; }

        public PreflightCheckStatus Status { get; set; }

        public string Message { get; set; }

        public string Details { get; set; }
    }

    public sealed class SessionPreflightReport
    {
        public SessionPreflightReport(IReadOnlyList<PreflightCheckResult> checks)
        {
            Checks = checks ?? new PreflightCheckResult[0];
        }

        public IReadOnlyList<PreflightCheckResult> Checks { get; private set; }

        public bool HasFailures
        {
            get { return Checks.Any(check => check.Status == PreflightCheckStatus.Failed); }
        }

        public bool HasWarnings
        {
            get { return Checks.Any(check => check.Status == PreflightCheckStatus.Warning); }
        }
    }

    public sealed class SessionPreflightRequest
    {
        public SessionSettings Settings { get; set; }

        public string StarLabLogPath { get; set; }

        public string StarLabEnergyColumnName { get; set; }

        public string BeamGageSelectedSource { get; set; }

        public string BuildConfiguration { get; set; }
    }

    public interface IStarLabLogPreflightProbe
    {
        StarLabLogPreflightProbeResult Inspect(string logFilePath, string preferredEnergyColumnName);
    }

    public sealed class StarLabLogPreflightProbeResult
    {
        public bool FileExists { get; set; }

        public bool CanRead { get; set; }

        public bool HeaderFound { get; set; }

        public bool PreferredEnergyColumnFound { get; set; }

        public bool FallbackEnergyColumnFound { get; set; }

        public string ResolvedEnergyColumnName { get; set; }

        public string Details { get; set; }

        public Exception Exception { get; set; }
    }

    public interface IBeamGagePreflightProbe
    {
        BeamGagePreflightProbeResult Inspect();
    }

    public sealed class BeamGagePreflightProbeResult
    {
        public bool DependencyAvailable { get; set; }

        public IReadOnlyList<string> PhysicalSources { get; set; }

        public string Details { get; set; }

        public Exception Exception { get; set; }
    }

    public sealed class SessionPreflightService
    {
        private readonly IStarLabLogPreflightProbe _starLabProbe;
        private readonly IBeamGagePreflightProbe _beamGageProbe;

        public SessionPreflightService(IStarLabLogPreflightProbe starLabProbe, IBeamGagePreflightProbe beamGageProbe)
        {
            _starLabProbe = starLabProbe ?? throw new ArgumentNullException("starLabProbe");
            _beamGageProbe = beamGageProbe ?? throw new ArgumentNullException("beamGageProbe");
        }

        public SessionPreflightReport Run(SessionPreflightRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            List<PreflightCheckResult> checks = new List<PreflightCheckResult>();
            checks.Add(CheckRuntime(request.BuildConfiguration));
            checks.Add(CheckOutputPath(request.Settings != null ? request.Settings.OutputPath : null));
            checks.Add(CheckStarLabLog(request.StarLabLogPath, request.StarLabEnergyColumnName));
            checks.Add(CheckBeamGage(request.BeamGageSelectedSource));
            return new SessionPreflightReport(checks);
        }

        public string BuildFailureMessage(SessionPreflightReport report)
        {
            if (report == null || !report.HasFailures)
            {
                return string.Empty;
            }

            return "Self-Test failed. Fix failed checks before starting:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    report.Checks
                        .Where(check => check.Status == PreflightCheckStatus.Failed)
                        .Select(check => "- " + check.Name + ": " + check.Message));
        }

        private static PreflightCheckResult CheckRuntime(string buildConfiguration)
        {
            if (Environment.Is64BitProcess)
            {
                return Result(
                    "Runtime",
                    PreflightCheckStatus.Failed,
                    "Run the x86 application build.",
                    "BeamGage and legacy vendor automation are expected to run in a 32-bit process.");
            }

            if (!string.Equals(buildConfiguration, "Release", StringComparison.OrdinalIgnoreCase))
            {
                return Result(
                    "Runtime",
                    PreflightCheckStatus.Warning,
                    "Current build is not Release.",
                    "Use Release|x86 for delivery and final stand validation.");
            }

            return Result(
                "Runtime",
                PreflightCheckStatus.Passed,
                "Release x86 process is active.",
                "Build configuration: " + buildConfiguration + "; Is64BitProcess=False.");
        }

        private static PreflightCheckResult CheckOutputPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return Result(
                    "Export path",
                    PreflightCheckStatus.Failed,
                    "Select an Excel export file before starting.",
                    "Output path is empty.");
            }

            string trimmed = outputPath.Trim();
            try
            {
                string fileName = Path.GetFileName(trimmed);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return Result(
                        "Export path",
                        PreflightCheckStatus.Failed,
                        "Export path must include a file name.",
                        trimmed);
                }

                string directory = Path.GetDirectoryName(trimmed);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = Environment.CurrentDirectory;
                }

                if (!Directory.Exists(directory))
                {
                    string parent = Directory.GetParent(directory) != null
                        ? Directory.GetParent(directory).FullName
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                    {
                        return Result(
                            "Export path",
                            PreflightCheckStatus.Failed,
                            "Export folder parent does not exist.",
                            directory);
                    }

                    return Result(
                        "Export path",
                        PreflightCheckStatus.Warning,
                        "Export folder does not exist yet and will be created on start.",
                        directory);
                }

                if (File.Exists(trimmed))
                {
                    using (File.Open(trimmed, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
                else
                {
                    string probePath = Path.Combine(directory, ".laser-energy-monitor-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (File.Create(probePath))
                    {
                    }

                    File.Delete(probePath);
                }

                return Result(
                    "Export path",
                    PreflightCheckStatus.Passed,
                    "Export folder is writable.",
                    directory);
            }
            catch (Exception ex)
            {
                return Result(
                    "Export path",
                    PreflightCheckStatus.Failed,
                    "Export file or folder is not writable. Close Excel or choose another path.",
                    ex.Message);
            }
        }

        private PreflightCheckResult CheckStarLabLog(string logFilePath, string energyColumnName)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Failed,
                    "Select StarLab Data_log.txt before starting.",
                    "Path is empty.");
            }

            StarLabLogPreflightProbeResult inspection;
            try
            {
                inspection = _starLabProbe.Inspect(logFilePath, energyColumnName);
            }
            catch (Exception ex)
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Failed,
                    "StarLab log check failed.",
                    ex.Message);
            }

            if (inspection == null)
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Failed,
                    "StarLab log check did not return a result.",
                    string.Empty);
            }

            if (!inspection.FileExists)
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Failed,
                    "StarLab Data_log.txt was not found.",
                    logFilePath);
            }

            if (!inspection.CanRead)
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Failed,
                    "StarLab log is not readable. Check StarLab logging and file permissions.",
                    inspection.Exception != null ? inspection.Exception.Message : inspection.Details);
            }

            if (!inspection.HeaderFound)
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Failed,
                    "StarLab header was not found. Enable StarLab logging and wait for Data_log.txt headers.",
                    inspection.Details);
            }

            if (inspection.PreferredEnergyColumnFound)
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Passed,
                    "Preferred StarLab energy column is available: " + inspection.ResolvedEnergyColumnName + ".",
                    inspection.Details);
            }

            if (inspection.FallbackEnergyColumnFound)
            {
                return Result(
                    "StarLab log",
                    PreflightCheckStatus.Warning,
                    "Preferred energy column was not found; using fallback: " + inspection.ResolvedEnergyColumnName + ".",
                    inspection.Details);
            }

            return Result(
                "StarLab log",
                PreflightCheckStatus.Failed,
                "No usable StarLab energy column was found.",
                inspection.Details);
        }

        private PreflightCheckResult CheckBeamGage(string selectedSource)
        {
            if (string.IsNullOrWhiteSpace(selectedSource))
            {
                return Result(
                    "BeamGage",
                    PreflightCheckStatus.Failed,
                    "Scan BeamGage sources and select a physical source.",
                    "No selected BeamGage source.");
            }

            BeamGagePreflightProbeResult inspection;
            try
            {
                inspection = _beamGageProbe.Inspect();
            }
            catch (Exception ex)
            {
                return Result(
                    "BeamGage",
                    PreflightCheckStatus.Failed,
                    "BeamGage SDK/probe is not available.",
                    ex.Message);
            }

            if (inspection == null || !inspection.DependencyAvailable)
            {
                return Result(
                    "BeamGage",
                    PreflightCheckStatus.Failed,
                    "BeamGage SDK/probe is not available.",
                    inspection != null && inspection.Exception != null ? inspection.Exception.Message : inspection != null ? inspection.Details : string.Empty);
            }

            IReadOnlyList<string> sources = inspection.PhysicalSources ?? new string[0];
            if (sources.Count == 0)
            {
                return Result(
                    "BeamGage",
                    PreflightCheckStatus.Warning,
                    "BeamGage SDK is available, but no physical sources were found.",
                    inspection.Details);
            }

            bool selectedFound = sources.Any(source => string.Equals(source, selectedSource, StringComparison.OrdinalIgnoreCase));
            if (!selectedFound)
            {
                return Result(
                    "BeamGage",
                    PreflightCheckStatus.Warning,
                    "Selected BeamGage source was not found in the latest scan.",
                    "Selected: " + selectedSource + ". Found: " + string.Join(", ", sources));
            }

            return Result(
                "BeamGage",
                PreflightCheckStatus.Passed,
                "Selected BeamGage source is available.",
                "Selected: " + selectedSource + ". Found: " + sources.Count.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static PreflightCheckResult Result(string name, PreflightCheckStatus status, string message, string details)
        {
            return new PreflightCheckResult
            {
                Name = name,
                Status = status,
                Message = message,
                Details = details ?? string.Empty
            };
        }
    }
}
