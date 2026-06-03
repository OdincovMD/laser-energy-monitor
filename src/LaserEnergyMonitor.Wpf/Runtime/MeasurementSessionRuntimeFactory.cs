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
        private const string OphirSdkSourceKey = "ophir-sdk";
        private const string OphirFastXSourceKey = "ophir-fastx";
        private const string OphirFastXSimulationSourceKey = "ophir-fastx-sim";
        private readonly string _logPath;
        private readonly IOperatorNotifier _notifier;
        private readonly IClock _clock;
        private readonly IReadOnlyList<MeasurementSourceOption> _firstSourceOptions;
        private readonly IReadOnlyList<MeasurementSourceOption> _secondSourceOptions;
        private readonly BeamGageMeasurementOptions _beamGageOptions;
        private readonly TimeSpan _beamGageSmokeTestDuration;
        private readonly OphirMeasurementOptions _ophirOptions;
        private readonly TimeSpan _ophirSmokeTestDuration;

        public MeasurementSessionRuntimeFactory(string logPath, IOperatorNotifier notifier, IClock clock)
        {
            _logPath = logPath;
            _notifier = notifier;
            _clock = clock;
            _beamGageOptions = LoadBeamGageOptions();
            _beamGageSmokeTestDuration = LoadBeamGageSmokeTestDuration();
            _ophirOptions = LoadOphirOptions(logPath);
            _ophirSmokeTestDuration = LoadOphirSmokeTestDuration();

            _firstSourceOptions = new[]
            {
                new MeasurementSourceOption(
                    "beam-sim",
                    "Simulated BeamGage",
                    true,
                    () => new SimulatedMeasurementSource("BeamGage", SimulatedMeasurementProfile.CreateBeamGageCustomerLike(), 50),
                    () => new MeasurementSourceRuntimeProbeResult
                    {
                        DependencyAvailable = true,
                        Summary = "Simulation mode is ready.",
                        Details = "No external BeamGage SDK is required."
                    }),
                new MeasurementSourceOption(
                    "beam-sdk",
                    "BeamGage SDK",
                    true,
                    () => new BeamGageMeasurementSource(CloneBeamGageOptions(_beamGageOptions)),
                    BeamGageRuntimeProbe.Probe)
            };

            _secondSourceOptions = new[]
            {
                new MeasurementSourceOption(
                    "ophir-sim",
                    "Simulated Ophir",
                    true,
                    () => new SimulatedMeasurementSource("Ophir", SimulatedMeasurementProfile.CreateOphirCustomerLike(), 50),
                    () => new MeasurementSourceRuntimeProbeResult
                    {
                        DependencyAvailable = true,
                        Summary = "Simulation mode is ready.",
                        Details = "No external Ophir runtime is required."
                    }),
                new MeasurementSourceOption(
                    "ophir-sdk",
                    "Ophir LMMeasurement SDK",
                    true,
                    () => new OphirMeasurementSource(CloneOphirOptions(_ophirOptions, OphirRuntimeBackend.LmMeasurement)),
                    OphirRuntimeProbe.Probe),
                new MeasurementSourceOption(
                    "ophir-fastx",
                    "Ophir Pulsar ActiveX (legacy)",
                    true,
                    () => new OphirMeasurementSource(CloneOphirOptions(_ophirOptions, OphirRuntimeBackend.PulsarFastX)),
                    OphirFastXRuntimeProbe.Probe),
                new MeasurementSourceOption(
                    "ophir-fastx-sim",
                    "Simulated Pulsar ActiveX",
                    true,
                    () => new OphirMeasurementSource(CloneOphirOptions(_ophirOptions, OphirRuntimeBackend.SimulatedPulsarFastX)),
                    OphirFastXSimulationRuntimeProbe.Probe)
            };

            if (!string.IsNullOrWhiteSpace(_ophirOptions.ReplayFilePath))
            {
                _secondSourceOptions = _secondSourceOptions.Concat(
                    new[]
                    {
                        new MeasurementSourceOption(
                            "ophir-replay",
                            "Ophir Replay Capture",
                            true,
                            () => new OphirReplayMeasurementSource(CloneOphirOptions(_ophirOptions)),
                            () => ProbeOphirReplay(_ophirOptions.ReplayFilePath))
                    }).ToArray();
            }
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

        public MeasurementSessionService Create(string firstSourceKey, string secondSourceKey)
        {
            MeasurementSourceOption firstOption = GetFirstSourceOption(firstSourceKey);
            MeasurementSourceOption secondOption = GetSecondSourceOption(secondSourceKey);

            ValidateImplementation(firstOption);
            ValidateImplementation(secondOption);

            return new MeasurementSessionService(
                firstOption.CreateSource(),
                secondOption.CreateSource(),
                new TimeWindowMeasurementSynchronizer(),
                new RollingStationarityDetector(),
                new PrototypeExcelExporter(),
                new FileApplicationLogger(_logPath),
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

        public string RunOphirSmokeTest(string selectedSecondSourceKey)
        {
            MeasurementSourceOption selectedOption = GetSecondSourceOption(selectedSecondSourceKey);
            MeasurementSourceOption ophirSdkOption = GetOphirSmokeTestOption(selectedOption);
            MeasurementSourceRuntimeProbeResult probe = ophirSdkOption.ProbeRuntime();
            StringBuilder builder = new StringBuilder();
            builder.Append("Ophir smoke-test generated at ");
            builder.Append(_clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.Append("Selected UI source: ");
            builder.AppendLine(selectedOption.DisplayName);
            builder.Append("Executed source: ");
            builder.AppendLine(ophirSdkOption.DisplayName);
            builder.AppendLine("Mode: Selected Ophir backend smoke-test");
            builder.Append("Duration: ");
            builder.AppendLine(_ophirSmokeTestDuration.ToString());
            builder.Append("Configured serial: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_ophirOptions.DeviceSerialNumber) ? "auto-first-detected" : _ophirOptions.DeviceSerialNumber);
            builder.Append("Configured preferred channel: ");
            builder.AppendLine(_ophirOptions.PreferredChannel.HasValue ? _ophirOptions.PreferredChannel.Value.ToString() : "auto-first-active");
            builder.Append("Configured poll interval: ");
            builder.AppendLine(_ophirOptions.PollInterval.ToString());
            builder.Append("Configured timestamp strategy: ");
            builder.AppendLine(_ophirOptions.TimestampStrategy.ToString());
            builder.Append("Capture directory: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_ophirOptions.CaptureDirectoryPath) ? "disabled" : _ophirOptions.CaptureDirectoryPath);
            builder.Append("Replay file: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(_ophirOptions.ReplayFilePath) ? "not configured" : _ophirOptions.ReplayFilePath);
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
            string resolvedSerialNumber = string.Empty;
            string resolvedChannel = string.Empty;
            string capturePath = string.Empty;
            int rawSampleCount = 0;
            int acceptedSampleCount = 0;
            int nonZeroStatusCount = 0;
            string statusPreview = string.Empty;
            string outcome;
            Exception executionException = null;

            bool sdkUnavailable = !probe.DependencyAvailable;
            bool noUsbDevicesDetected = string.Equals(
                probe.Summary,
                "Ophir COM runtime is functional. No USB devices are currently visible.",
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    probe.Summary,
                    "Ophir Pulsar ActiveX runtime is functional. No Pulsar USB devices are currently visible.",
                    StringComparison.OrdinalIgnoreCase);

            if (sdkUnavailable)
            {
                outcome = "Runtime unavailable. Acquisition was not attempted.";
            }
            else if (noUsbDevicesDetected)
            {
                outcome = "Runtime available, but no USB devices are currently visible. Acquisition was skipped.";
            }
            else
            {
                try
                {
                    OphirMeasurementSource source = (OphirMeasurementSource)ophirSdkOption.CreateSource();
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
                        resolvedSerialNumber = string.IsNullOrWhiteSpace(source.CurrentSerialNumber) ? "n/a" : source.CurrentSerialNumber;
                        resolvedChannel = source.CurrentChannel.HasValue ? source.CurrentChannel.Value.ToString() : "n/a";
                        source.Start();
                        Thread.Sleep(_ophirSmokeTestDuration);
                        source.Stop();
                        capturePath = string.IsNullOrWhiteSpace(source.LastCapturePath) ? "n/a" : source.LastCapturePath;
                        rawSampleCount = source.RawSampleCount;
                        acceptedSampleCount = source.AcceptedSampleCount;
                        nonZeroStatusCount = source.NonZeroStatusCount;
                        statusPreview = source.StatusPreview;
                    }

                    if (fault != null)
                    {
                        outcome = "Streaming fault reported by Ophir source.";
                    }
                    else if (sampleCount > 0)
                    {
                        outcome = "Live samples were received from the real Ophir SDK source.";
                    }
                    else
                    {
                        outcome = "SDK calls completed, but no samples were received during the smoke-test window.";
                    }
                }
                catch (Exception ex)
                {
                    executionException = ex;
                    outcome = "Acquisition attempt failed before live samples were confirmed.";
                }
            }

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
            builder.Append("Resolved serial: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(resolvedSerialNumber) ? "n/a" : resolvedSerialNumber);
            builder.Append("Resolved channel: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(resolvedChannel) ? "n/a" : resolvedChannel);
            builder.Append("Capture file: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(capturePath) ? "n/a" : capturePath);
            builder.Append("Raw samples observed: ");
            builder.AppendLine(rawSampleCount.ToString());
            builder.Append("Accepted energy samples: ");
            builder.AppendLine(acceptedSampleCount.ToString());
            builder.Append("Non-zero status samples: ");
            builder.AppendLine(nonZeroStatusCount.ToString());
            builder.Append("First raw statuses: ");
            builder.AppendLine(string.IsNullOrWhiteSpace(statusPreview) ? "n/a" : statusPreview);

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
            new FileApplicationLogger(_logPath).Info("Ophir smoke-test completed." + Environment.NewLine + report);
            WriteSmokeTestReport(report, "ophir-smoke-test");
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
            File.WriteAllText(reportPath, report);
        }

        private static OphirMeasurementOptions LoadOphirOptions(string logPath)
        {
            OphirMeasurementOptions options = OphirMeasurementOptions.Default;
            options.DeviceSerialNumber = ReadAppSetting("MeasurementSources.OphirSerialNumber");
            options.PreferredChannel = ParseNullableInt(ReadAppSetting("MeasurementSources.OphirPreferredChannel"));
            options.TimestampStrategy = ParseTimestampStrategy(ReadAppSetting("MeasurementSources.OphirTimestampStrategy"));
            options.ReplayFilePath = ReadAppSetting("MeasurementSources.OphirReplayPath");
            options.ReplaySpeedMultiplier = ParseDouble(ReadAppSetting("MeasurementSources.OphirReplaySpeedMultiplier"), 1.0d);

            double pollIntervalMs = ParseDouble(ReadAppSetting("MeasurementSources.OphirPollIntervalMs"), 50.0d);
            if (pollIntervalMs < 1.0d)
            {
                pollIntervalMs = 1.0d;
            }

            options.PollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
            string captureDirectory = ReadAppSetting("MeasurementSources.OphirCaptureDirectory");
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                options.CaptureDirectoryPath = captureDirectory;
            }
            else
            {
                string logDirectory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(logDirectory))
                {
                    options.CaptureDirectoryPath = Path.Combine(logDirectory, "ophir-captures");
                }
            }

            return options;
        }

        private static TimeSpan LoadOphirSmokeTestDuration()
        {
            double durationMs = ParseDouble(ReadAppSetting("MeasurementSources.OphirSmokeTestDurationMs"), 1500.0d);
            if (durationMs < 100.0d)
            {
                durationMs = 100.0d;
            }

            return TimeSpan.FromMilliseconds(durationMs);
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

        private MeasurementSourceOption GetOphirSmokeTestOption(MeasurementSourceOption selectedOption)
        {
            if (selectedOption != null &&
                string.Equals(selectedOption.Key, OphirFastXSourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return GetSecondSourceOption(OphirFastXSourceKey);
            }

            if (selectedOption != null &&
                string.Equals(selectedOption.Key, OphirFastXSimulationSourceKey, StringComparison.OrdinalIgnoreCase))
            {
                return GetSecondSourceOption(OphirFastXSimulationSourceKey);
            }

            return GetSecondSourceOption(OphirSdkSourceKey);
        }

        private static OphirMeasurementOptions CloneOphirOptions(
            OphirMeasurementOptions options,
            OphirRuntimeBackend? runtimeBackend = null)
        {
            return new OphirMeasurementOptions
            {
                RuntimeBackend = runtimeBackend ?? options.RuntimeBackend,
                DeviceSerialNumber = options.DeviceSerialNumber,
                PreferredChannel = options.PreferredChannel,
                PollInterval = options.PollInterval,
                TimestampStrategy = options.TimestampStrategy,
                CaptureDirectoryPath = options.CaptureDirectoryPath,
                ReplayFilePath = options.ReplayFilePath,
                ReplaySpeedMultiplier = options.ReplaySpeedMultiplier
            };
        }

        private static MeasurementSourceRuntimeProbeResult ProbeOphirReplay(string replayFilePath)
        {
            bool exists = !string.IsNullOrWhiteSpace(replayFilePath) && File.Exists(replayFilePath);
            return new MeasurementSourceRuntimeProbeResult
            {
                DependencyAvailable = exists,
                Summary = exists
                    ? "Replay capture file is available."
                    : "Replay capture file is missing.",
                Details = exists
                    ? replayFilePath
                    : "Configure MeasurementSources.OphirReplayPath to a valid capture CSV file.",
                Steps = new[]
                {
                    new MeasurementSourceRuntimeProbeStep
                    {
                        Name = "Replay file",
                        Status = exists ? "PASS" : "FAIL",
                        Details = replayFilePath ?? string.Empty
                    }
                }
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
            options.ShowGui = ParseBool(ReadAppSetting("MeasurementSources.BeamGageShowGui"), false);
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

        private static int? ParseNullableInt(string value)
        {
            int parsed;
            if (int.TryParse(value, out parsed))
            {
                return parsed;
            }

            return null;
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

        private static OphirTimestampStrategy ParseTimestampStrategy(string value)
        {
            OphirTimestampStrategy parsed;
            return Enum.TryParse(value, true, out parsed)
                ? parsed
                : OphirTimestampStrategy.HostArrivalUtc;
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
