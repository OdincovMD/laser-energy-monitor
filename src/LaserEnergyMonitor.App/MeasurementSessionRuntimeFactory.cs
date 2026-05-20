using System;
using System.Collections.Generic;
using System.Text;
using LaserEnergyMonitor.Application;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure;
using LaserEnergyMonitor.Infrastructure.BeamGage;
using LaserEnergyMonitor.Infrastructure.Excel;
using LaserEnergyMonitor.Infrastructure.Logging;
using LaserEnergyMonitor.Infrastructure.Ophir;

namespace LaserEnergyMonitor.App
{
    public sealed class MeasurementSessionRuntimeFactory
    {
        private readonly string _logPath;
        private readonly IOperatorNotifier _notifier;
        private readonly IClock _clock;
        private readonly IReadOnlyList<MeasurementSourceOption> _firstSourceOptions;
        private readonly IReadOnlyList<MeasurementSourceOption> _secondSourceOptions;

        public MeasurementSessionRuntimeFactory(string logPath, IOperatorNotifier notifier, IClock clock)
        {
            _logPath = logPath;
            _notifier = notifier;
            _clock = clock;

            _firstSourceOptions = new[]
            {
                new MeasurementSourceOption(
                    "beam-sim",
                    "Simulated BeamGage",
                    true,
                    () => new SimulatedMeasurementSource("BeamGage", 10.0d, 50),
                    () => new MeasurementSourceRuntimeProbeResult
                    {
                        DependencyAvailable = true,
                        Summary = "Simulation mode is ready.",
                        Details = "No external BeamGage SDK is required."
                    }),
                new MeasurementSourceOption(
                    "beam-sdk",
                    "BeamGage SDK",
                    false,
                    () => new BeamGageMeasurementSource(),
                    BeamGageRuntimeProbe.Probe)
            };

            _secondSourceOptions = new[]
            {
                new MeasurementSourceOption(
                    "ophir-sim",
                    "Simulated Ophir",
                    true,
                    () => new SimulatedMeasurementSource("Ophir", 10.2d, 50),
                    () => new MeasurementSourceRuntimeProbeResult
                    {
                        DependencyAvailable = true,
                        Summary = "Simulation mode is ready.",
                        Details = "No external Ophir runtime is required."
                    }),
                new MeasurementSourceOption(
                    "ophir-sdk",
                    "Ophir SDK",
                    true,
                    () => new OphirMeasurementSource(),
                    OphirRuntimeProbe.Probe)
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
            builder.Append("  Details: ");
            builder.AppendLine(probe.Details);
            builder.Append("  Live acquisition: ");
            builder.AppendLine(option.IsImplemented ? "implemented in this build" : "not wired yet in this build");
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
}
