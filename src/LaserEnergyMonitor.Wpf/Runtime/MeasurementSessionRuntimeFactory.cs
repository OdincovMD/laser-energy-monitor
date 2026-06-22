using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure;
using LaserEnergyMonitor.Infrastructure.BeamGage;
using LaserEnergyMonitor.Infrastructure.Excel;
using LaserEnergyMonitor.Infrastructure.Logging;
using LaserEnergyMonitor.Infrastructure.Ophir;

namespace LaserEnergyMonitor.Wpf
{
    public sealed class MeasurementSessionRuntimeFactory
    {
        private const string BeamGageSdkSourceKey = "beam-sdk";
        private const string StarLabLogSourceKey = "starlab-log";
        private readonly string _logPath;
        private readonly IOperatorNotifier _notifier;
        private readonly IClock _clock;
        private readonly IReadOnlyList<MeasurementSourceOption> _firstSourceOptions;
        private readonly IReadOnlyList<MeasurementSourceOption> _secondSourceOptions;
        private readonly BeamGageMeasurementOptions _beamGageOptions;
        private readonly TimeSpan _beamGageSmokeTestDuration;
        private readonly StarLabLogMeasurementOptions _starLabLogOptions;

        public MeasurementSessionRuntimeFactory(string logPath, IOperatorNotifier notifier, IClock clock)
        {
            _logPath = logPath;
            _notifier = notifier;
            _clock = clock;
            _beamGageOptions = LoadBeamGageOptions();
            _beamGageSmokeTestDuration = LoadBeamGageSmokeTestDuration();
            _starLabLogOptions = LoadStarLabLogOptions();

            _firstSourceOptions = new[]
            {
                new MeasurementSourceOption(
                    BeamGageSdkSourceKey,
                    "BeamGage SDK",
                    true,
                    () => new BeamGageMeasurementSource(CloneBeamGageOptions(_beamGageOptions)),
                    BeamGageRuntimeProbe.Probe)
            };

            _secondSourceOptions = new[]
            {
                new MeasurementSourceOption(
                    StarLabLogSourceKey,
                    "StarLab Log File",
                    true,
                    () => new StarLabLogMeasurementSource(CloneStarLabLogOptions(_starLabLogOptions)),
                    () => ProbeStarLabLog(_starLabLogOptions))
            };
        }

        public IReadOnlyList<MeasurementSourceOption> FirstSourceOptions
        {
            get { return _firstSourceOptions; }
        }

        public IReadOnlyList<MeasurementSourceOption> SecondSourceOptions
        {
            get { return _secondSourceOptions; }
        }

        public string ConfiguredBeamGageDataSource
        {
            get { return _beamGageOptions.DataSource; }
        }

        public string ConfiguredStarLabLogPath
        {
            get { return _starLabLogOptions.LogFilePath; }
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

        public MeasurementSessionService Create(string firstSourceKey, string secondSourceKey)
        {
            MeasurementSourceOption firstOption = GetFirstSourceOption(firstSourceKey);
            MeasurementSourceOption secondOption = GetSecondSourceOption(secondSourceKey);

            ValidateImplementation(firstOption);
            ValidateImplementation(secondOption);

            return new MeasurementSessionService(
                firstOption.CreateSource(),
                secondOption.CreateSource(),
                new RollingStationarityDetector(),
                new AsyncMeasurementExporter(new PrototypeExcelExporter()),
                new AsyncApplicationLogger(new FileApplicationLogger(_logPath)),
                _notifier,
                _clock);
        }

        public string BuildDiagnostics(string firstSourceKey, string secondSourceKey)
        {
            MeasurementSourceOption firstOption = GetFirstSourceOption(firstSourceKey);
            MeasurementSourceOption secondOption = GetSecondSourceOption(secondSourceKey);

            return BuildDiagnosticsReport(firstOption, secondOption);
        }

        public IReadOnlyList<MeasurementSourceDiagnostic> GetDiagnostics(string firstSourceKey, string secondSourceKey)
        {
            MeasurementSourceOption firstOption = GetFirstSourceOption(firstSourceKey);
            MeasurementSourceOption secondOption = GetSecondSourceOption(secondSourceKey);

            return new[]
            {
                CreateDiagnostic("BeamGage", firstOption),
                CreateDiagnostic("Ophir", secondOption)
            };
        }

        public string RunSelfTest(string firstSourceKey, string secondSourceKey)
        {
            MeasurementSourceOption firstOption = GetFirstSourceOption(firstSourceKey);
            MeasurementSourceOption secondOption = GetSecondSourceOption(secondSourceKey);

            StringBuilder builder = new StringBuilder();
            builder.Append("Hardware self-test generated at ");
            builder.Append(_clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(BuildDiagnosticsReport(firstOption, secondOption));

            string report = builder.ToString().Trim();
            new FileApplicationLogger(_logPath).Info("Hardware self-test completed." + Environment.NewLine + report);
            WriteSelfTestReport(report);
            return report;
        }

        public string RunUsbInventory()
        {
            string report = UsbDeviceInventory.BuildReport(_clock.UtcNow.ToLocalTime());
            new FileApplicationLogger(_logPath).Info("USB inventory completed." + Environment.NewLine + report);
            WriteSmokeTestReport(report, "usb-inventory");
            return report;
        }

        public string RunBeamGageSmokeTest(string selectedFirstSourceKey)
        {
            MeasurementSourceOption selectedOption = GetFirstSourceOption(selectedFirstSourceKey);
            MeasurementSourceOption beamGageSdkOption = GetFirstSourceOption(BeamGageSdkSourceKey);
            MeasurementSourceRuntimeProbeResult probe = beamGageSdkOption.ProbeRuntime();
            StringBuilder builder = new StringBuilder();
            builder.Append("BeamGage smoke-test generated at ");
            builder.Append(_clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.Append("Selected UI source: ");
            builder.AppendLine(selectedOption.DisplayName);
            builder.Append("Executed source: ");
            builder.AppendLine(beamGageSdkOption.DisplayName);
            builder.AppendLine("Mode: Forced real SDK smoke-test");
            builder.Append("Duration: ");
            builder.AppendLine(_beamGageSmokeTestDuration.ToString());
            builder.Append("Configured automation instance: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_beamGageOptions.AutomationInstanceId) ? "default" : _beamGageOptions.AutomationInstanceId);
            builder.Append("Configured show GUI: ");
            builder.AppendLine(_beamGageOptions.ShowGui ? "true" : "false");
            builder.Append("Configured data source: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_beamGageOptions.DataSource) ? "auto-first-available" : _beamGageOptions.DataSource);
            builder.Append("Allow built-in data sources: ");
            builder.AppendLine(_beamGageOptions.AllowBuiltInDataSources ? "true" : "false");
            builder.Append("Configured power meter: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_beamGageOptions.PowerMeter) ? "auto-first-available" : _beamGageOptions.PowerMeter);
            builder.Append("Configured wavelength: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_beamGageOptions.WaveLength) ? "auto-first-available" : _beamGageOptions.WaveLength);
            builder.Append("Configured timestamp strategy: ");
            builder.AppendLine(_beamGageOptions.TimestampStrategy.ToString());
            builder.Append("Configured frame timeout: ");
            builder.AppendLine(_beamGageOptions.FrameTimeout.ToString());
            builder.Append("Configured polling fallback: ");
            builder.AppendLine(_beamGageOptions.PollingFallbackEnabled ? "enabled" : "disabled");
            builder.Append("Configured polling interval: ");
            builder.AppendLine(_beamGageOptions.PollingFallbackInterval.ToString());
            builder.Append("Configured power/energy calibration: ");
            builder.AppendLine(FormatBeamGageCalibration(_beamGageOptions));
            builder.AppendLine();
            builder.Append("Runtime probe summary: ");
            builder.AppendLine(probe.Summary);
            AppendProbeSteps(builder, probe);
            builder.Append("Runtime probe details: ");
            builder.AppendLine(probe.Details);
            builder.AppendLine();

            int sampleCount = 0;
            double? minEnergy = null;
            double? maxEnergy = null;
            DateTime? firstTimestampUtc = null;
            DateTime? lastTimestampUtc = null;
            DeviceFault fault = null;
            string resolvedDataSource = string.Empty;
            string resolvedPowerMeter = string.Empty;
            string resolvedWaveLength = string.Empty;
            string resolvedStatus = string.Empty;
            bool resolvedOnline = false;
            string[] resolvedAllDataSources = new string[0];
            string[] resolvedSelectableDataSources = new string[0];
            string resolvedEnergyUnitBase = string.Empty;
            string resolvedEnergyUnitQuantifier = string.Empty;
            double? resolvedScaleMultiplier = null;
            long onNewFrameCallbackCount = 0L;
            long eventFrameCount = 0L;
            long pollFrameAttemptCount = 0L;
            long polledFrameCount = 0L;
            long duplicateFrameSkipCount = 0L;
            long lastObservedFrameId = 0L;
            string lastFrameReadError = string.Empty;
            string outcome;
            Exception executionException = null;

            if (!probe.DependencyAvailable)
            {
                outcome = "Runtime unavailable. Acquisition was not attempted.";
            }
            else
            {
                try
                {
                    BeamGageMeasurementSource source = (BeamGageMeasurementSource)beamGageSdkOption.CreateSource();
                    using (source)
                    {
                        source.MeasurementReceived += delegate(object sender, MeasurementReceivedEventArgs args)
                        {
                            MeasurementSample sample = args.Sample;
                            sampleCount += 1;
                            if (!firstTimestampUtc.HasValue)
                            {
                                firstTimestampUtc = sample.TimestampUtc;
                            }

                            lastTimestampUtc = sample.TimestampUtc;
                            minEnergy = !minEnergy.HasValue ? sample.Energy : Math.Min(minEnergy.Value, sample.Energy);
                            maxEnergy = !maxEnergy.HasValue ? sample.Energy : Math.Max(maxEnergy.Value, sample.Energy);
                        };

                        source.Faulted += delegate(object sender, DeviceFaultEventArgs args)
                        {
                            fault = args != null ? args.Fault : null;
                        };

                        source.Initialize();
                        resolvedDataSource = source.CurrentDataSource;
                        resolvedPowerMeter = source.CurrentPowerMeter;
                        resolvedWaveLength = source.CurrentWaveLength;
                        resolvedStatus = source.CurrentDataSourceStatus;
                        resolvedOnline = source.IsSourceOnline;
                        resolvedAllDataSources = source.AvailableDataSources.ToArray();
                        resolvedSelectableDataSources = source.AvailablePhysicalDataSources.ToArray();
                        source.Start();
                        Thread.Sleep(_beamGageSmokeTestDuration);
                        resolvedEnergyUnitBase = source.CurrentEnergyUnitBase;
                        resolvedEnergyUnitQuantifier = source.CurrentEnergyUnitQuantifier;
                        resolvedScaleMultiplier = source.CurrentScaleMultiplier;
                        onNewFrameCallbackCount = source.OnNewFrameCallbackCount;
                        eventFrameCount = source.EventFrameCount;
                        pollFrameAttemptCount = source.PollFrameAttemptCount;
                        polledFrameCount = source.PolledFrameCount;
                        duplicateFrameSkipCount = source.DuplicateFrameSkipCount;
                        lastObservedFrameId = source.LastObservedFrameId;
                        lastFrameReadError = source.LastFrameReadError;
                        source.Stop();
                    }

                    if (fault != null)
                    {
                        outcome = "Streaming fault reported by BeamGage source.";
                    }
                    else if (sampleCount > 0)
                    {
                        outcome = "Live samples were received from the BeamGage SDK source.";
                    }
                    else
                    {
                        outcome = "BeamGage SDK started, but no samples were received during the smoke-test window.";
                    }
                }
                catch (Exception ex)
                {
                    executionException = ex;
                    outcome = "Acquisition attempt failed before live samples were confirmed.";
                }
            }

            builder.Append("Resolved data source: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(resolvedDataSource) ? "n/a" : resolvedDataSource);
            builder.Append("Resolved power meter: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(resolvedPowerMeter) ? "n/a" : resolvedPowerMeter);
            builder.Append("Resolved wavelength: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(resolvedWaveLength) ? "n/a" : resolvedWaveLength);
            builder.Append("Resolved source status: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(resolvedStatus) ? "n/a" : resolvedStatus);
            builder.Append("Resolved online flag: ");
            builder.AppendLine(resolvedStatus == string.Empty && !resolvedOnline ? "n/a" : (resolvedOnline ? "true" : "false"));
            builder.Append("Resolved all data sources: ");
            builder.AppendLine(FormatStringList(resolvedAllDataSources));
            builder.Append("Resolved selectable data sources: ");
            builder.AppendLine(FormatStringList(resolvedSelectableDataSources));
            builder.Append("Resolved energy units: ");
            builder.AppendLine(FormatBeamGageUnits(resolvedEnergyUnitBase, resolvedEnergyUnitQuantifier));
            builder.Append("Resolved scale multiplier: ");
            builder.AppendLine(resolvedScaleMultiplier.HasValue ? resolvedScaleMultiplier.Value.ToString("G17") : "n/a");
            builder.Append("OnNewFrame callbacks: ");
            builder.AppendLine(onNewFrameCallbackCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Event frames accepted: ");
            builder.AppendLine(eventFrameCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Polling attempts: ");
            builder.AppendLine(pollFrameAttemptCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Polling frames accepted: ");
            builder.AppendLine(polledFrameCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Duplicate frame skips: ");
            builder.AppendLine(duplicateFrameSkipCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Last observed vendor frame ID: ");
            builder.AppendLine(lastObservedFrameId > 0L ? lastObservedFrameId.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append("Last frame read error: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(lastFrameReadError) ? "none" : lastFrameReadError);
            builder.AppendLine();
            builder.Append("Outcome: ");
            builder.AppendLine(outcome);
            builder.Append("Samples captured: ");
            builder.AppendLine(sampleCount.ToString());
            builder.Append("First sample UTC: ");
            builder.AppendLine(firstTimestampUtc.HasValue ? firstTimestampUtc.Value.ToString("O") : "n/a");
            builder.Append("Last sample UTC: ");
            builder.AppendLine(lastTimestampUtc.HasValue ? lastTimestampUtc.Value.ToString("O") : "n/a");
            builder.Append("Energy min/max: ");
            builder.AppendLine(
                minEnergy.HasValue && maxEnergy.HasValue
                    ? minEnergy.Value.ToString("0.000000") + " / " + maxEnergy.Value.ToString("0.000000")
                    : "n/a");

            if (fault != null)
            {
                builder.Append("Fault: ");
                builder.AppendLine(fault.Message);
            }
            else
            {
                builder.AppendLine("Fault: none");
            }

            if (executionException != null)
            {
                builder.Append("Exception: ");
                builder.AppendLine(executionException.Message);
            }

            string report = builder.ToString().Trim();
            new FileApplicationLogger(_logPath).Info("BeamGage smoke-test completed." + Environment.NewLine + report);
            WriteSmokeTestReport(report, "beamgage-smoke-test");
            return report;
        }

        public string RunBeamGageVendorProbe(string selectedFirstSourceKey)
        {
            MeasurementSourceOption selectedOption = GetFirstSourceOption(selectedFirstSourceKey);
            MeasurementSourceRuntimeProbeResult probe = GetFirstSourceOption(BeamGageSdkSourceKey).ProbeRuntime();
            StringBuilder builder = new StringBuilder();
            builder.Append("BeamGage vendor probe generated at ");
            builder.Append(_clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.Append("Selected UI source: ");
            builder.AppendLine(selectedOption.DisplayName);
            builder.AppendLine("Executed source: BeamGage SDK");
            builder.AppendLine("Mode: Minimal vendor automation probe");
            builder.Append("Duration: ");
            builder.AppendLine(_beamGageSmokeTestDuration.ToString());
            builder.AppendLine("Reference flow: AutomatedBeamGage(showGui=true), AutomationFrameEvents(ResultsPriorityFrame).OnNewFrame, DataSource.Start/Stop, Instance.Shutdown");
            builder.Append("Configured automation instance: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_beamGageOptions.AutomationInstanceId) ? "default" : _beamGageOptions.AutomationInstanceId);
            builder.Append("Configured data source: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_beamGageOptions.DataSource) ? "auto-first-available" : _beamGageOptions.DataSource);
            builder.Append("Allow built-in data sources: ");
            builder.AppendLine(_beamGageOptions.AllowBuiltInDataSources ? "true" : "false");
            builder.AppendLine();
            builder.Append("Runtime probe summary: ");
            builder.AppendLine(probe.Summary);
            builder.Append("Runtime probe details: ");
            builder.AppendLine(probe.Details);
            builder.AppendLine();

            string outcome;
            Exception executionException = null;
            BeamGageVendorProbeResult result = null;
            if (!probe.DependencyAvailable)
            {
                outcome = "Runtime unavailable. Vendor probe was not attempted.";
            }
            else
            {
                try
                {
                    result = BeamGageVendorProbe.Run(CloneBeamGageOptions(_beamGageOptions), _beamGageSmokeTestDuration);
                    outcome = result.CallbackCount > 0
                        ? "Vendor automation OnNewFrame callbacks were received."
                        : "Vendor automation started, but no OnNewFrame callbacks were received during the probe window.";
                }
                catch (Exception ex)
                {
                    executionException = ex;
                    outcome = "Vendor automation probe failed before callbacks were confirmed.";
                }
            }

            if (result != null)
            {
                builder.Append("Install path: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.InstallPath) ? "n/a" : result.InstallPath);
                builder.Append("All data sources: ");
                builder.AppendLine(FormatStringList(result.DataSources));
                builder.Append("Selectable data sources: ");
                builder.AppendLine(FormatStringList(result.PhysicalDataSources));
                builder.Append("Selected data source: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.SelectedDataSource) ? "n/a" : result.SelectedDataSource);
                builder.Append("Current data source: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.CurrentDataSource) ? "n/a" : result.CurrentDataSource);
                builder.Append("Status before start: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.StatusBeforeStart) ? "n/a" : result.StatusBeforeStart);
                builder.Append("Status after stop: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.StatusAfterStop) ? "n/a" : result.StatusAfterStop);
                builder.Append("Power meter: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.PowerMeter) ? "n/a" : result.PowerMeter);
                builder.Append("Wavelength: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.WaveLength) ? "n/a" : result.WaveLength);
                builder.Append("OnNewFrame callbacks: ");
                builder.AppendLine(result.CallbackCount.ToString(CultureInfo.InvariantCulture));
                builder.Append("Frame read errors: ");
                builder.AppendLine(result.ReadErrorCount.ToString(CultureInfo.InvariantCulture));
                builder.Append("First vendor frame ID: ");
                builder.AppendLine(result.FirstFrameId > 0L ? result.FirstFrameId.ToString(CultureInfo.InvariantCulture) : "n/a");
                builder.Append("Last vendor frame ID: ");
                builder.AppendLine(result.LastFrameId > 0L ? result.LastFrameId.ToString(CultureInfo.InvariantCulture) : "n/a");
                builder.Append("First callback UTC: ");
                builder.AppendLine(result.FirstCallbackUtc.HasValue ? result.FirstCallbackUtc.Value.ToString("O") : "n/a");
                builder.Append("Last callback UTC: ");
                builder.AppendLine(result.LastCallbackUtc.HasValue ? result.LastCallbackUtc.Value.ToString("O") : "n/a");
                builder.Append("Energy min/max: ");
                builder.AppendLine(
                    result.MinEnergy.HasValue && result.MaxEnergy.HasValue
                        ? result.MinEnergy.Value.ToString("0.000000") + " / " + result.MaxEnergy.Value.ToString("0.000000")
                        : "n/a");
                builder.Append("Last read error: ");
                builder.AppendLine(string.IsNullOrWhiteSpace(result.LastReadError) ? "none" : result.LastReadError);
                builder.AppendLine();
            }

            builder.Append("Outcome: ");
            builder.AppendLine(outcome);
            if (executionException != null)
            {
                builder.Append("Exception: ");
                builder.AppendLine(executionException.Message);
            }

            string report = builder.ToString().Trim();
            new FileApplicationLogger(_logPath).Info("BeamGage vendor probe completed." + Environment.NewLine + report);
            WriteSmokeTestReport(report, "beamgage-vendor-probe");
            return report;
        }

        private static string BuildDiagnosticsReport(MeasurementSourceOption firstOption, MeasurementSourceOption secondOption)
        {
            StringBuilder builder = new StringBuilder();
            AppendDiagnostic(builder, "BeamGage", firstOption);
            builder.AppendLine();
            AppendDiagnostic(builder, "Ophir", secondOption);
            return builder.ToString().Trim();
        }

        private static void AppendDiagnostic(StringBuilder builder, string slotName, MeasurementSourceOption option)
        {
            MeasurementSourceRuntimeProbeResult probe = option.ProbeRuntime();
            builder.Append(slotName);
            builder.Append(": ");
            builder.Append(option.DisplayName);
            builder.AppendLine();
            builder.Append("  Status: ");
            builder.AppendLine(probe.Summary);
            if (probe.Steps != null)
            {
                builder.AppendLine("  Steps:");
                for (int i = 0; i < probe.Steps.Count; i++)
                {
                    MeasurementSourceRuntimeProbeStep step = probe.Steps[i];
                    builder.Append("    [");
                    builder.Append(step.Status ?? "UNKNOWN");
                    builder.Append("] ");
                    builder.Append(step.Name ?? "Step");
                    if (!string.IsNullOrWhiteSpace(step.Details))
                    {
                        builder.Append(" - ");
                        builder.Append(step.Details);
                    }

                    builder.AppendLine();
                }
            }
            builder.Append("  Details: ");
            builder.AppendLine(probe.Details);
            builder.Append("  Live acquisition: ");
            builder.AppendLine(option.IsImplemented ? "implemented in this build" : "not wired yet in this build");
        }

        private static MeasurementSourceDiagnostic CreateDiagnostic(string slotName, MeasurementSourceOption option)
        {
            return new MeasurementSourceDiagnostic
            {
                SlotName = slotName,
                DisplayName = option.DisplayName,
                IsImplemented = option.IsImplemented,
                Probe = option.ProbeRuntime()
            };
        }

        private static void AppendProbeSteps(StringBuilder builder, MeasurementSourceRuntimeProbeResult probe)
        {
            if (probe == null || probe.Steps == null || probe.Steps.Count == 0)
            {
                return;
            }

            builder.AppendLine("Runtime probe steps:");
            for (int i = 0; i < probe.Steps.Count; i++)
            {
                MeasurementSourceRuntimeProbeStep step = probe.Steps[i];
                builder.Append("  [");
                builder.Append(step.Status ?? "UNKNOWN");
                builder.Append("] ");
                builder.Append(step.Name ?? "Step");
                if (!string.IsNullOrWhiteSpace(step.Details))
                {
                    builder.Append(" - ");
                    builder.Append(step.Details);
                }

                builder.AppendLine();
            }
        }

        private MeasurementSourceOption GetFirstSourceOption(string key)
        {
            return GetOption(_firstSourceOptions, key, "first");
        }

        private MeasurementSourceOption GetSecondSourceOption(string key)
        {
            return GetOption(_secondSourceOptions, key, "second");
        }

        private static MeasurementSourceOption GetOption(IReadOnlyList<MeasurementSourceOption> options, string key, string slotName)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return options[i];
                }
            }

            throw new InvalidOperationException("Unknown " + slotName + " source option: " + key);
        }

        private static void ValidateImplementation(MeasurementSourceOption option)
        {
            if (option.IsImplemented)
            {
                return;
            }

            MeasurementSourceRuntimeProbeResult probe = option.ProbeRuntime();
            StringBuilder message = new StringBuilder();
            message.Append(option.DisplayName);
            message.Append(" is available for diagnostics, but live acquisition is not connected in this build.");
            message.AppendLine();
            message.Append(probe.Summary);
            if (!string.IsNullOrWhiteSpace(probe.Details))
            {
                message.AppendLine();
                message.Append(probe.Details);
            }

            throw new InvalidOperationException(message.ToString());
        }

        private void WriteSelfTestReport(string report)
        {
            WriteSmokeTestReport(report, "hardware-self-test");
        }

        private void WriteSmokeTestReport(string report, string filePrefix)
        {
            string logDirectory = Path.GetDirectoryName(_logPath);
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                return;
            }

            Directory.CreateDirectory(logDirectory);
            string fileName = filePrefix + "-" + _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".txt";
            string reportPath = Path.Combine(logDirectory, fileName);
            File.WriteAllText(reportPath, report, Encoding.UTF8);
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

        private static TimeSpan LoadBeamGageSmokeTestDuration()
        {
            double durationMs = ParseDouble(ReadAppSetting("MeasurementSources.BeamGageSmokeTestDurationMs"), 1500.0d);
            if (durationMs < 100.0d)
            {
                durationMs = 100.0d;
            }

            return TimeSpan.FromMilliseconds(durationMs);
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

        private static MeasurementSourceRuntimeProbeResult ProbeStarLabLog(StarLabLogMeasurementOptions options)
        {
            StarLabLogMeasurementOptions effectiveOptions = options ?? StarLabLogMeasurementOptions.Default;
            bool hasPath = !string.IsNullOrWhiteSpace(effectiveOptions.LogFilePath);
            bool exists = hasPath && File.Exists(effectiveOptions.LogFilePath);
            List<MeasurementSourceRuntimeProbeStep> steps = new List<MeasurementSourceRuntimeProbeStep>();

            steps.Add(
                new MeasurementSourceRuntimeProbeStep
                {
                    Name = "Log path",
                    Status = hasPath ? "PASS" : "FAIL",
                    Details = hasPath ? effectiveOptions.LogFilePath : "Select StarLab Data_log.txt before starting acquisition."
                });

            steps.Add(
                new MeasurementSourceRuntimeProbeStep
                {
                    Name = "File exists",
                    Status = exists ? "PASS" : "FAIL",
                    Details = exists ? "File is available for shared reading." : "The selected StarLab log file was not found."
                });

            if (!exists)
            {
                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = false,
                    Summary = "StarLab log file is not ready.",
                    Details = hasPath ? effectiveOptions.LogFilePath : "No StarLab log file is configured.",
                    Steps = steps
                };
            }

            try
            {
                StarLabLogParser parser = new StarLabLogParser(effectiveOptions.EnergyColumnName);
                string[] lines = ReadSharedLines(effectiveOptions.LogFilePath, 200);
                for (int i = 0; i < lines.Length; i++)
                {
                    StarLabLogSample ignored;
                    parser.TryProcessLine(lines[i], out ignored);
                }

                bool headerFound = parser.Columns.Length > 0;
                bool energyColumnFound = !string.IsNullOrWhiteSpace(parser.EnergyColumnName);
                steps.Add(
                    new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Table header",
                        Status = headerFound ? "PASS" : "WARN",
                        Details = headerFound ? string.Join(" | ", parser.Columns) : "Waiting for the StarLab table header."
                    });
                steps.Add(
                    new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Energy column",
                        Status = energyColumnFound ? "PASS" : "WARN",
                        Details = energyColumnFound ? parser.EnergyColumnName : "Waiting for an energy column."
                    });

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = headerFound && energyColumnFound,
                    Summary = headerFound && energyColumnFound
                        ? "StarLab log file is ready."
                        : "StarLab log file is readable, but measurement rows are not ready yet.",
                    Details =
                        "Path: " + effectiveOptions.LogFilePath + Environment.NewLine +
                        "Preferred energy column: " + effectiveOptions.EnergyColumnName + Environment.NewLine +
                        "Resolved energy column: " + (energyColumnFound ? parser.EnergyColumnName : "n/a") + Environment.NewLine +
                        "Poll interval: " + effectiveOptions.PollInterval,
                    Steps = steps
                };
            }
            catch (Exception ex)
            {
                steps.Add(
                    new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Shared read",
                        Status = "FAIL",
                        Details = ex.Message
                    });

                return new MeasurementSourceRuntimeProbeResult
                {
                    DependencyAvailable = false,
                    Summary = "StarLab log file could not be read.",
                    Details = ex.Message,
                    Steps = steps
                };
            }
        }

        private static string[] ReadSharedLines(string path, int maxLines)
        {
            List<string> lines = new List<string>();
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.Default, true))
            {
                while (!reader.EndOfStream && lines.Count < maxLines)
                {
                    lines.Add(reader.ReadLine());
                }
            }

            return lines.ToArray();
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
            return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static double? ParseNullableDouble(string value)
        {
            double parsed;
            return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)
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

        private static string FormatBeamGageCalibration(BeamGageMeasurementOptions options)
        {
            if (options == null)
            {
                return "unchanged";
            }

            if (options.PowerEnergyCalibrationValue.HasValue)
            {
                StringBuilder builder = new StringBuilder();
                builder.Append(options.PowerEnergyCalibrationValue.Value.ToString("G17"));
                builder.Append(' ');
                builder.Append(string.IsNullOrWhiteSpace(options.PowerEnergyCalibrationUnitQuantifier) ? string.Empty : options.PowerEnergyCalibrationUnitQuantifier + ' ');
                builder.Append(string.IsNullOrWhiteSpace(options.PowerEnergyCalibrationUnitBase) ? "JOULES" : options.PowerEnergyCalibrationUnitBase);
                if (options.ResetPowerEnergyCalibrationOnStart)
                {
                    builder.Append(" after reset");
                }

                return builder.ToString().Trim();
            }

            return options.ResetPowerEnergyCalibrationOnStart ? "reset only" : "unchanged";
        }

        private static string FormatBeamGageUnits(string unitBase, string unitQuantifier)
        {
            if (string.IsNullOrWhiteSpace(unitBase) && string.IsNullOrWhiteSpace(unitQuantifier))
            {
                return "n/a";
            }

            if (string.IsNullOrWhiteSpace(unitQuantifier))
            {
                return unitBase;
            }

            if (string.Equals(unitQuantifier, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                return unitBase;
            }

            return unitQuantifier + " " + unitBase;
        }

        private static string FormatStringList(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "none";
            }

            return string.Join(" | ", values);
        }
    }

    public sealed class MeasurementSourceOption
    {
        private readonly Func<IMeasurementSource> _createSource;
        private readonly Func<MeasurementSourceRuntimeProbeResult> _probeRuntime;

        public MeasurementSourceOption(
            string key,
            string displayName,
            bool isImplemented,
            Func<IMeasurementSource> createSource,
            Func<MeasurementSourceRuntimeProbeResult> probeRuntime)
        {
            Key = key;
            DisplayName = displayName;
            IsImplemented = isImplemented;
            _createSource = createSource;
            _probeRuntime = probeRuntime;
        }

        public string Key { get; private set; }

        public string DisplayName { get; private set; }

        public bool IsImplemented { get; private set; }

        public IMeasurementSource CreateSource()
        {
            return _createSource();
        }

        public MeasurementSourceRuntimeProbeResult ProbeRuntime()
        {
            return _probeRuntime();
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public sealed class MeasurementSourceDiagnostic
    {
        public string SlotName { get; set; }

        public string DisplayName { get; set; }

        public bool IsImplemented { get; set; }

        public MeasurementSourceRuntimeProbeResult Probe { get; set; }
    }
}
