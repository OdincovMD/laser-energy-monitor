using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure;
using LaserEnergyMonitor.Infrastructure.BeamGage;
using LaserEnergyMonitor.Infrastructure.Excel;
using LaserEnergyMonitor.Infrastructure.Logging;
using LaserEnergyMonitor.Infrastructure.Ophir;

namespace LaserEnergyMonitor.Wpf
{
    public sealed class MeasurementSessionRuntimeFactory : IBeamGagePreflightProbe
    {
        private readonly string _logPath;
        private readonly IOperatorNotifier _notifier;
        private readonly IClock _clock;
        private readonly BeamGageMeasurementOptions _beamGageOptions;
        private readonly StarLabLogMeasurementOptions _starLabLogOptions;

        public MeasurementSessionRuntimeFactory(string logPath, IOperatorNotifier notifier, IClock clock)
        {
            _logPath = logPath;
            _notifier = notifier;
            _clock = clock;
            _beamGageOptions = LoadBeamGageOptions();
            _starLabLogOptions = LoadStarLabLogOptions();
        }

        public string ConfiguredBeamGageDataSource
        {
            get { return _beamGageOptions.DataSource; }
        }

        public string ConfiguredStarLabLogPath
        {
            get { return _starLabLogOptions.LogFilePath; }
        }

        public string ConfiguredStarLabEnergyColumnName
        {
            get { return _starLabLogOptions.EnergyColumnName; }
        }

        public IReadOnlyList<string> DiscoverBeamGagePhysicalDataSources()
        {
            BeamGageMeasurementOptions discoveryOptions = CloneBeamGageOptions(_beamGageOptions);
            discoveryOptions.DataSource = null;

            using (BeamGageMeasurementSource source = new BeamGageMeasurementSource(discoveryOptions))
            {
                source.Initialize();
                return source.AvailablePhysicalDataSources.ToArray();
            }
        }

        public void SelectBeamGagePhysicalDataSource(string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                throw new InvalidOperationException("Select a BeamGage data source before connecting.");
            }

            _beamGageOptions.DataSource = dataSource;
        }

        public void SelectStarLabLogFile(string logFilePath)
        {
            _starLabLogOptions.LogFilePath = logFilePath ?? string.Empty;
        }

        public MeasurementSessionService Create()
        {
            return new MeasurementSessionService(
                new BeamGageMeasurementSource(CloneBeamGageOptions(_beamGageOptions)),
                new StarLabLogMeasurementSource(CloneStarLabLogOptions(_starLabLogOptions)),
                new RollingStationarityDetector(),
                new AsyncMeasurementExporter(new PrototypeExcelExporter()),
                new AsyncApplicationLogger(new FileApplicationLogger(_logPath)),
                _notifier,
                _clock);
        }

        public BeamGagePreflightProbeResult Inspect()
        {
            try
            {
                IReadOnlyList<string> dataSources = DiscoverBeamGagePhysicalDataSources();
                return new BeamGagePreflightProbeResult
                {
                    DependencyAvailable = true,
                    PhysicalSources = dataSources,
                    Details = "BeamGage physical source scan completed."
                };
            }
            catch (Exception ex)
            {
                return new BeamGagePreflightProbeResult
                {
                    DependencyAvailable = false,
                    PhysicalSources = new string[0],
                    Details = ex.Message,
                    Exception = ex
                };
            }
        }

        public void LogPreflightReport(SessionPreflightReport report)
        {
            if (report == null)
            {
                return;
            }

            try
            {
                FileApplicationLogger logger = new FileApplicationLogger(_logPath);
                logger.Info("Self-Test started.");
                foreach (PreflightCheckResult check in report.Checks)
                {
                    string message = check.Name + " [" + check.Status + "] " + check.Message;
                    if (!string.IsNullOrWhiteSpace(check.Details))
                    {
                        message += " Details: " + check.Details;
                    }

                    if (check.Status == PreflightCheckStatus.Failed)
                    {
                        logger.Error(message);
                    }
                    else if (check.Status == PreflightCheckStatus.Warning)
                    {
                        logger.Warning(message);
                    }
                    else
                    {
                        logger.Info(message);
                    }
                }

                logger.Info("Self-Test finished.");
            }
            catch
            {
            }
        }

        private static StarLabLogMeasurementOptions LoadStarLabLogOptions()
        {
            StarLabLogMeasurementOptions options = StarLabLogMeasurementOptions.Default;
            string configuredPath = ReadAppSetting("MeasurementSources.StarLabLogPath");
            options.LogFilePath = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "StarLab",
                    "Data_log.txt")
                : configuredPath;

            options.EnergyColumnName = ReadAppSetting("MeasurementSources.StarLabLogEnergyColumn");
            if (string.IsNullOrWhiteSpace(options.EnergyColumnName))
            {
                options.EnergyColumnName = StarLabLogMeasurementOptions.Default.EnergyColumnName;
            }

            double pollIntervalMs = ParseDouble(ReadAppSetting("MeasurementSources.StarLabLogPollIntervalMs"), 1000.0d);
            if (pollIntervalMs < 100.0d)
            {
                pollIntervalMs = 100.0d;
            }

            options.PollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
            options.StartAtEnd = ParseBool(ReadAppSetting("MeasurementSources.StarLabLogStartAtEnd"), true);
            return options;
        }

        private static StarLabLogMeasurementOptions CloneStarLabLogOptions(StarLabLogMeasurementOptions options)
        {
            StarLabLogMeasurementOptions effectiveOptions = options ?? StarLabLogMeasurementOptions.Default;
            return new StarLabLogMeasurementOptions
            {
                LogFilePath = effectiveOptions.LogFilePath,
                EnergyColumnName = effectiveOptions.EnergyColumnName,
                PollInterval = effectiveOptions.PollInterval,
                StartAtEnd = effectiveOptions.StartAtEnd
            };
        }

        private static string ReadAppSetting(string key)
        {
            return ConfigurationManager.AppSettings[key];
        }

        private static BeamGageMeasurementOptions LoadBeamGageOptions()
        {
            BeamGageMeasurementOptions options = BeamGageMeasurementOptions.Default;
            options.AutomationInstanceId = ReadAppSetting("MeasurementSources.BeamGageInstanceId");
            options.ShowGui = ParseBool(ReadAppSetting("MeasurementSources.BeamGageShowGui"), BeamGageMeasurementOptions.Default.ShowGui);
            options.DataSource = ReadAppSetting("MeasurementSources.BeamGageDataSource");
            options.AllowBuiltInDataSources = ParseBool(ReadAppSetting("MeasurementSources.BeamGageAllowBuiltInDataSources"), true);
            options.PowerMeter = ReadAppSetting("MeasurementSources.BeamGagePowerMeter");
            options.WaveLength = ReadAppSetting("MeasurementSources.BeamGageWaveLength");
            options.TimestampStrategy = ParseBeamGageTimestampStrategy(ReadAppSetting("MeasurementSources.BeamGageTimestampStrategy"));
            options.FrameTimeout = ParseBeamGageFrameTimeout(ReadAppSetting("MeasurementSources.BeamGageFrameTimeoutMs"));
            options.PollingFallbackEnabled = ParseBool(ReadAppSetting("MeasurementSources.BeamGagePollingFallbackEnabled"), true);
            options.PollingFallbackInterval = ParseBeamGagePollingFallbackInterval(ReadAppSetting("MeasurementSources.BeamGagePollingFallbackIntervalMs"));
            options.ResetPowerEnergyCalibrationOnStart = ParseBool(ReadAppSetting("MeasurementSources.BeamGageResetPowerEnergyCalibrationOnStart"), false);
            options.PowerEnergyCalibrationValue = ParseNullableDouble(ReadAppSetting("MeasurementSources.BeamGagePowerEnergyCalibrationValue"));
            options.PowerEnergyCalibrationUnitBase = ReadAppSetting("MeasurementSources.BeamGagePowerEnergyCalibrationUnitBase");
            options.PowerEnergyCalibrationUnitQuantifier = ReadAppSetting("MeasurementSources.BeamGagePowerEnergyCalibrationUnitQuantifier");

            if (string.IsNullOrWhiteSpace(options.AutomationInstanceId))
            {
                options.AutomationInstanceId = BeamGageMeasurementOptions.Default.AutomationInstanceId;
            }

            return options;
        }

        private static BeamGageMeasurementOptions CloneBeamGageOptions(BeamGageMeasurementOptions options)
        {
            return new BeamGageMeasurementOptions
            {
                AutomationInstanceId = options.AutomationInstanceId,
                ShowGui = options.ShowGui,
                DataSource = options.DataSource,
                AllowBuiltInDataSources = options.AllowBuiltInDataSources,
                PowerMeter = options.PowerMeter,
                WaveLength = options.WaveLength,
                TimestampStrategy = options.TimestampStrategy,
                FrameTimeout = options.FrameTimeout,
                PollingFallbackEnabled = options.PollingFallbackEnabled,
                PollingFallbackInterval = options.PollingFallbackInterval,
                ResetPowerEnergyCalibrationOnStart = options.ResetPowerEnergyCalibrationOnStart,
                PowerEnergyCalibrationValue = options.PowerEnergyCalibrationValue,
                PowerEnergyCalibrationUnitBase = options.PowerEnergyCalibrationUnitBase,
                PowerEnergyCalibrationUnitQuantifier = options.PowerEnergyCalibrationUnitQuantifier
            };
        }

        private static TimeSpan ParseBeamGageFrameTimeout(string value)
        {
            double durationMs = ParseDouble(value, BeamGageMeasurementOptions.Default.FrameTimeout.TotalMilliseconds);
            if (durationMs < 500.0d)
            {
                durationMs = 500.0d;
            }

            return TimeSpan.FromMilliseconds(durationMs);
        }

        private static TimeSpan ParseBeamGagePollingFallbackInterval(string value)
        {
            double durationMs = ParseDouble(value, BeamGageMeasurementOptions.Default.PollingFallbackInterval.TotalMilliseconds);
            if (durationMs < 5.0d)
            {
                durationMs = 5.0d;
            }

            return TimeSpan.FromMilliseconds(durationMs);
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static double? ParseNullableDouble(string value)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : (double?)null;
        }

        private static BeamGageTimestampStrategy ParseBeamGageTimestampStrategy(string value)
        {
            BeamGageTimestampStrategy parsed;
            return Enum.TryParse(value, true, out parsed)
                ? parsed
                : BeamGageTimestampStrategy.HostArrivalUtc;
        }
    }
}
