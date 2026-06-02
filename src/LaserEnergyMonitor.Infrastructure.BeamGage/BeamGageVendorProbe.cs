using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    public static class BeamGageVendorProbe
    {
        private const string AutomationAssemblyName = "Spiricon.Automation.dll";
        private const string BeamGageAutomationAssemblyName = "Spiricon.BeamGage.Automation.dll";
        private const string AutomatedBeamGageTypeName = "Spiricon.Automation.AutomatedBeamGage";
        private const string FrameEventsTypeName = "Spiricon.Automation.AutomationFrameEvents";

        public static BeamGageVendorProbeResult Run(BeamGageMeasurementOptions options, TimeSpan duration)
        {
            BeamGageMeasurementOptions effectiveOptions = options ?? BeamGageMeasurementOptions.Default;
            string installPath = ResolveInstallPath();
            LoadAutomationAssemblies(installPath);

            VendorProbeSession session = new VendorProbeSession(effectiveOptions, installPath);
            try
            {
                session.Open();
                session.Start();
                Thread.Sleep(duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(2));
                session.Stop();
                return session.BuildResult();
            }
            finally
            {
                session.Dispose();
            }
        }

        private sealed class VendorProbeSession : IDisposable
        {
            private readonly object _gate = new object();
            private readonly BeamGageMeasurementOptions _options;
            private readonly string _installPath;
            private object _beamGage;
            private object _frameEvents;
            private object _dataSource;
            private EventInfo _onNewFrameEvent;
            private Delegate _onNewFrameDelegate;
            private bool _started;
            private bool _disposed;
            private int _callbackCount;
            private int _readErrorCount;
            private long _firstFrameId;
            private long _lastFrameId;
            private double? _minEnergy;
            private double? _maxEnergy;
            private DateTime? _firstCallbackUtc;
            private DateTime? _lastCallbackUtc;
            private string _lastReadError;

            public VendorProbeSession(BeamGageMeasurementOptions options, string installPath)
            {
                _options = options;
                _installPath = installPath;
            }

            public string[] DataSources { get; private set; }

            public string[] PhysicalDataSources { get; private set; }

            public string SelectedDataSource { get; private set; }

            public string CurrentDataSource { get; private set; }

            public string StatusBeforeStart { get; private set; }

            public string StatusAfterStop { get; private set; }

            public string PowerMeter { get; private set; }

            public string WaveLength { get; private set; }

            public void Open()
            {
                Type automatedBeamGageType = GetRequiredType(BeamGageAutomationAssemblyName, AutomatedBeamGageTypeName);
                _beamGage = CreateBeamGageClient(automatedBeamGageType, _options, _installPath);

                _dataSource = GetPropertyValue(_beamGage, "DataSource");
                DataSources = ToStringArray(GetPropertyValue(_dataSource, "DataSourceList"));
                PhysicalDataSources = BeamGageDataSourceSelector.GetPhysicalDataSources(DataSources);
                SelectedDataSource = BeamGageDataSourceSelector.ResolvePhysicalDataSource(DataSources, _options.DataSource);
                SetPropertyValue(_dataSource, "DataSource", SelectedDataSource);
                CurrentDataSource = Convert.ToString(GetPropertyValue(_dataSource, "DataSource"), CultureInfo.InvariantCulture);
                StatusBeforeStart = Convert.ToString(GetPropertyValue(_dataSource, "Status"), CultureInfo.InvariantCulture);

                object powerMeter = GetPropertyValue(_beamGage, "PowerMeter");
                PowerMeter = Convert.ToString(GetPropertyValue(powerMeter, "PowerMeter"), CultureInfo.InvariantCulture);
                WaveLength = Convert.ToString(GetPropertyValue(powerMeter, "WaveLength"), CultureInfo.InvariantCulture);

                object resultsPriorityFrame = GetPropertyValue(_beamGage, "ResultsPriorityFrame");
                Type frameEventsType = GetRequiredType(AutomationAssemblyName, FrameEventsTypeName);
                _frameEvents = Activator.CreateInstance(frameEventsType, resultsPriorityFrame);
                MethodInfo callbackMethod = typeof(VendorProbeSession).GetMethod(
                    "OnRemoteNewFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _onNewFrameEvent = frameEventsType.GetEvent("OnNewFrame");
                _onNewFrameDelegate = Delegate.CreateDelegate(_onNewFrameEvent.EventHandlerType, this, callbackMethod);
                _onNewFrameEvent.AddEventHandler(_frameEvents, _onNewFrameDelegate);
            }

            public void Start()
            {
                InvokeMethod(_dataSource, "Start");
                _started = true;
            }

            public void Stop()
            {
                if (!_started)
                {
                    return;
                }

                TryInvokeMethod(_dataSource, "Stop");
                _started = false;
                StatusAfterStop = Convert.ToString(GetPropertyValue(_dataSource, "Status"), CultureInfo.InvariantCulture);
            }

            public BeamGageVendorProbeResult BuildResult()
            {
                lock (_gate)
                {
                    return new BeamGageVendorProbeResult
                    {
                        InstallPath = _installPath,
                        DataSources = DataSources ?? new string[0],
                        PhysicalDataSources = PhysicalDataSources ?? new string[0],
                        SelectedDataSource = SelectedDataSource,
                        CurrentDataSource = CurrentDataSource,
                        StatusBeforeStart = StatusBeforeStart,
                        StatusAfterStop = StatusAfterStop,
                        PowerMeter = PowerMeter,
                        WaveLength = WaveLength,
                        CallbackCount = _callbackCount,
                        ReadErrorCount = _readErrorCount,
                        FirstFrameId = _firstFrameId,
                        LastFrameId = _lastFrameId,
                        MinEnergy = _minEnergy,
                        MaxEnergy = _maxEnergy,
                        FirstCallbackUtc = _firstCallbackUtc,
                        LastCallbackUtc = _lastCallbackUtc,
                        LastReadError = _lastReadError
                    };
                }
            }

            private void OnRemoteNewFrame()
            {
                lock (_gate)
                {
                    _callbackCount += 1;
                    DateTime now = DateTime.UtcNow;
                    if (!_firstCallbackUtc.HasValue)
                    {
                        _firstCallbackUtc = now;
                    }

                    _lastCallbackUtc = now;
                }

                try
                {
                    object frameInfoResults = GetPropertyValue(_beamGage, "FrameInfoResults");
                    object powerEnergyResults = GetPropertyValue(_beamGage, "PowerEnergyResults");
                    long frameId = Convert.ToInt64(
                        Convert.ToDouble(GetPropertyValue(frameInfoResults, "ID"), CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture);
                    double energy = Convert.ToDouble(GetPropertyValue(powerEnergyResults, "Total"), CultureInfo.InvariantCulture);

                    lock (_gate)
                    {
                        if (_firstFrameId == 0L)
                        {
                            _firstFrameId = frameId;
                        }

                        _lastFrameId = frameId;
                        _minEnergy = !_minEnergy.HasValue ? energy : Math.Min(_minEnergy.Value, energy);
                        _maxEnergy = !_maxEnergy.HasValue ? energy : Math.Max(_maxEnergy.Value, energy);
                    }
                }
                catch (Exception ex)
                {
                    lock (_gate)
                    {
                        _readErrorCount += 1;
                        _lastReadError = ex.Message;
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try
                {
                    Stop();
                }
                catch
                {
                }

                try
                {
                    if (_onNewFrameEvent != null && _onNewFrameDelegate != null && _frameEvents != null)
                    {
                        _onNewFrameEvent.RemoveEventHandler(_frameEvents, _onNewFrameDelegate);
                    }
                }
                catch
                {
                }

                ShutdownBeamGage(_beamGage);
            }
        }

        private static string ResolveInstallPath()
        {
            string[] roots =
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            for (int i = 0; i < roots.Length; i++)
            {
                string root = roots[i];
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string candidate = Path.Combine(root, "Spiricon", "BeamGage Professional");
                if (File.Exists(Path.Combine(candidate, AutomationAssemblyName)) &&
                    File.Exists(Path.Combine(candidate, BeamGageAutomationAssemblyName)))
                {
                    return candidate;
                }
            }

            throw new BeamGagePrerequisiteException(
                "BeamGage automation assemblies were not found on this machine." + Environment.NewLine +
                "Install BeamGage Professional with automation support.");
        }

        private static void LoadAutomationAssemblies(string installPath)
        {
            string[] requiredAssemblies =
            {
                "BeamStack.Interfaces.dll",
                "BG.Interfaces.dll",
                "Spiricon.Interfaces.dll",
                "Spiricon.Interfaces.ConsoleService.dll",
                "Spiricon.Remoting.dll",
                "Spiricon.Shared.dll",
                "Spiricon.TreePattern.Interfaces.dll",
                "Spiricon.TreePattern.dll",
                "Spiricon.Automation.dll",
                "Spiricon.BeamGage.Automation.dll"
            };

            for (int i = 0; i < requiredAssemblies.Length; i++)
            {
                string assemblyPath = Path.Combine(installPath, requiredAssemblies[i]);
                if (File.Exists(assemblyPath))
                {
                    Assembly.LoadFrom(assemblyPath);
                }
            }
        }

        private static Type GetRequiredType(string assemblyFileName, string typeName)
        {
            string simpleAssemblyName = Path.GetFileNameWithoutExtension(assemblyFileName);
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Assembly candidate = loadedAssemblies[i];
                if (string.Equals(candidate.GetName().Name, simpleAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    Type type = candidate.GetType(typeName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }

            throw new BeamGagePrerequisiteException("BeamGage automation type was not found: " + typeName);
        }

        private static object CreateBeamGageClient(Type automatedBeamGageType, BeamGageMeasurementOptions options, string installPath)
        {
            string previousCurrentDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = installPath;
                string instanceId = string.IsNullOrWhiteSpace(options.AutomationInstanceId)
                    ? BeamGageMeasurementOptions.Default.AutomationInstanceId + "VendorProbe"
                    : options.AutomationInstanceId + "VendorProbe";
                return Activator.CreateInstance(automatedBeamGageType, instanceId, true);
            }
            finally
            {
                Environment.CurrentDirectory = previousCurrentDirectory;
            }
        }

        private static void ShutdownBeamGage(object beamGage)
        {
            if (beamGage == null)
            {
                return;
            }

            try
            {
                object instance = GetPropertyValue(beamGage, "Instance");
                if (instance != null)
                {
                    TryInvokeMethod(instance, "Shutdown");
                }
            }
            catch
            {
            }

            IDisposable disposable = beamGage as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            return instance.GetType().InvokeMember(
                propertyName,
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                instance,
                null,
                CultureInfo.InvariantCulture);
        }

        private static void SetPropertyValue(object instance, string propertyName, object value)
        {
            instance.GetType().InvokeMember(
                propertyName,
                BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                instance,
                new[] { value },
                CultureInfo.InvariantCulture);
        }

        private static object InvokeMethod(object instance, string methodName, params object[] args)
        {
            return instance.GetType().InvokeMember(
                methodName,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null,
                instance,
                args,
                CultureInfo.InvariantCulture);
        }

        private static void TryInvokeMethod(object instance, string methodName, params object[] args)
        {
            try
            {
                InvokeMethod(instance, methodName, args);
            }
            catch
            {
            }
        }

        private static string[] ToStringArray(object value)
        {
            Array array = value as Array;
            if (array == null || array.Length == 0)
            {
                return new string[0];
            }

            string[] result = new string[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                result[i] = Convert.ToString(array.GetValue(i), CultureInfo.InvariantCulture);
            }

            return result;
        }
    }

    public sealed class BeamGageVendorProbeResult
    {
        public string InstallPath { get; set; }
        public string[] DataSources { get; set; }
        public string[] PhysicalDataSources { get; set; }
        public string SelectedDataSource { get; set; }
        public string CurrentDataSource { get; set; }
        public string StatusBeforeStart { get; set; }
        public string StatusAfterStop { get; set; }
        public string PowerMeter { get; set; }
        public string WaveLength { get; set; }
        public int CallbackCount { get; set; }
        public int ReadErrorCount { get; set; }
        public long FirstFrameId { get; set; }
        public long LastFrameId { get; set; }
        public double? MinEnergy { get; set; }
        public double? MaxEnergy { get; set; }
        public DateTime? FirstCallbackUtc { get; set; }
        public DateTime? LastCallbackUtc { get; set; }
        public string LastReadError { get; set; }
    }
}
