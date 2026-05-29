using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace LaserEnergyMonitor.Infrastructure.BeamGage
{
    internal sealed class BeamGageRuntimeSession : IDisposable
    {
        private static readonly object AssemblyLoaderGate = new object();
        private static string _loadedInstallPath;
        private static ResolveEventHandler _assemblyResolveHandler;
        private const string ConsoleServiceAssemblyName = "Spiricon.Interfaces.ConsoleService.dll";
        private const string AutomationAssemblyName = "Spiricon.Automation.dll";
        private const string BeamGageAutomationAssemblyName = "Spiricon.BeamGage.Automation.dll";
        private const string AutomatedBeamGageTypeName = "Spiricon.Automation.AutomatedBeamGage";
        private const string FrameEventsTypeName = "Spiricon.Automation.AutomationFrameEvents";

        private readonly object _beamGage;
        private readonly object _frameEvents;
        private Delegate _onNewFrameDelegate;
        private readonly BeamGageMeasurementOptions _options;
        private bool _streaming;
        private bool _disposed;

        private BeamGageRuntimeSession(
            object beamGage,
            object frameEvents,
            Delegate onNewFrameDelegate,
            BeamGageMeasurementOptions options,
            string currentDataSource,
            string currentPowerMeter,
            string currentWaveLength)
        {
            _beamGage = beamGage;
            _frameEvents = frameEvents;
            _onNewFrameDelegate = onNewFrameDelegate;
            _options = options;
            CurrentDataSource = currentDataSource;
            CurrentPowerMeter = currentPowerMeter;
            CurrentWaveLength = currentWaveLength;
        }

        internal event EventHandler<BeamGageFrameAvailableEventArgs> FrameAvailable;
        internal event EventHandler<BeamGageFaultEventArgs> Faulted;

        internal string CurrentDataSource { get; private set; }

        internal string CurrentPowerMeter { get; private set; }

        internal string CurrentWaveLength { get; private set; }

        internal string CurrentEnergyUnitBase { get; private set; }

        internal string CurrentEnergyUnitQuantifier { get; private set; }

        internal double? CurrentScaleMultiplier { get; private set; }

        internal bool IsOnline
        {
            get
            {
                object dataSource = GetPropertyValue(_beamGage, "DataSource");
                return ToBoolean(GetPropertyValue(dataSource, "Online"));
            }
        }

        internal string DataSourceStatus
        {
            get
            {
                object dataSource = GetPropertyValue(_beamGage, "DataSource");
                object status = GetPropertyValue(dataSource, "Status");
                return status != null ? status.ToString() : string.Empty;
            }
        }

        internal static BeamGageRuntimeSession Open(BeamGageMeasurementOptions options)
        {
            EnsureStaThread();

            BeamGageMeasurementOptions effectiveOptions = options ?? BeamGageMeasurementOptions.Default;
            string installPath = ResolveInstallPath();
            LoadAutomationAssemblies(installPath);

            Type automatedBeamGageType = GetRequiredType(BeamGageAutomationAssemblyName, AutomatedBeamGageTypeName);
            object beamGage = CreateBeamGageClient(automatedBeamGageType, effectiveOptions);

            try
            {
                ConfigureDataSource(beamGage, effectiveOptions);
                ConfigurePowerMeter(beamGage, effectiveOptions);
                ConfigurePowerEnergy(beamGage, effectiveOptions);

                object resultsPriorityFrame = GetPropertyValue(beamGage, "ResultsPriorityFrame");
                Type frameEventsType = GetRequiredType(AutomationAssemblyName, FrameEventsTypeName);
                object frameEvents = Activator.CreateInstance(frameEventsType, resultsPriorityFrame);

                BeamGageRuntimeSession session = new BeamGageRuntimeSession(
                    beamGage,
                    frameEvents,
                    null,
                    CloneOptions(effectiveOptions),
                    Convert.ToString(GetPropertyValue(GetPropertyValue(beamGage, "DataSource"), "DataSource"), CultureInfo.InvariantCulture),
                    Convert.ToString(GetPropertyValue(GetPropertyValue(beamGage, "PowerMeter"), "PowerMeter"), CultureInfo.InvariantCulture),
                    Convert.ToString(GetPropertyValue(GetPropertyValue(beamGage, "PowerMeter"), "WaveLength"), CultureInfo.InvariantCulture));
                session.RefreshFrameMetadata();

                MethodInfo callbackMethod = typeof(BeamGageRuntimeSession).GetMethod(
                    "OnRemoteNewFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                EventInfo onNewFrameEvent = frameEventsType.GetEvent("OnNewFrame");
                Delegate boundCallback = Delegate.CreateDelegate(onNewFrameEvent.EventHandlerType, session, callbackMethod);
                onNewFrameEvent.AddEventHandler(frameEvents, boundCallback);
                session._onNewFrameDelegate = boundCallback;
                return session;
            }
            catch
            {
                ShutdownBeamGage(beamGage);
                throw;
            }
        }

        internal void StartStream()
        {
            ThrowIfDisposed();
            if (_streaming)
            {
                return;
            }

            object dataSource = GetPropertyValue(_beamGage, "DataSource");
            InvokeMethod(dataSource, "Start");
            _streaming = true;
        }

        internal void StopStream()
        {
            ThrowIfDisposed();
            if (!_streaming)
            {
                return;
            }

            object dataSource = GetPropertyValue(_beamGage, "DataSource");
            TryInvokeMethod(dataSource, "Stop");
            _streaming = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                StopStream();
            }
            catch
            {
            }

            try
            {
                EventInfo eventInfo = _frameEvents.GetType().GetEvent("OnNewFrame");
                if (eventInfo != null && _onNewFrameDelegate != null)
                {
                    eventInfo.RemoveEventHandler(_frameEvents, _onNewFrameDelegate);
                }
            }
            catch
            {
            }

            ShutdownBeamGage(_beamGage);
            _disposed = true;
        }

        private void OnRemoteNewFrame()
        {
            if (_disposed || !_streaming)
            {
                return;
            }

            try
            {
                BeamGageFrameSnapshot snapshot = ReadSnapshot();
                if (snapshot == null)
                {
                    return;
                }

                FrameAvailable?.Invoke(this, new BeamGageFrameAvailableEventArgs(snapshot));
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(
                    this,
                    new BeamGageFaultEventArgs(
                        "BeamGage frame processing failed: " + ex.Message,
                        ex));
            }
        }

        private BeamGageFrameSnapshot ReadSnapshot()
        {
            DateTime recordedUtc = DateTime.UtcNow;
            object resultsPriorityFrame = GetPropertyValue(_beamGage, "ResultsPriorityFrame");
            object frameInfoResults = GetPropertyValue(_beamGage, "FrameInfoResults");
            object powerEnergyResults = GetPropertyValue(_beamGage, "PowerEnergyResults");

            long frameId = Convert.ToInt64(
                ToDouble(GetPropertyValue(frameInfoResults, "ID")),
                CultureInfo.InvariantCulture);
            double totalEnergy = ToDouble(GetPropertyValue(powerEnergyResults, "Total"));
            double timestampOaDate = ToDouble(GetPropertyValue(frameInfoResults, "Timestamp"));
            CurrentEnergyUnitBase = Convert.ToString(GetPropertyValue(resultsPriorityFrame, "EnergyUnitsBase"), CultureInfo.InvariantCulture);
            CurrentEnergyUnitQuantifier = Convert.ToString(GetPropertyValue(resultsPriorityFrame, "EnergyUnitsQuantifier"), CultureInfo.InvariantCulture);
            CurrentScaleMultiplier = TryToNullableDouble(GetPropertyValue(frameInfoResults, "ScaleMultiplier"));

            return new BeamGageFrameSnapshot(
                frameId,
                ResolveTimestampUtc(recordedUtc, timestampOaDate),
                recordedUtc,
                totalEnergy);
        }

        private DateTime ResolveTimestampUtc(DateTime recordedUtc, double frameTimestampOaDate)
        {
            if (_options.TimestampStrategy != BeamGageTimestampStrategy.FrameInfoOaDateLocal)
            {
                return recordedUtc;
            }

            if (frameTimestampOaDate <= 0d)
            {
                return recordedUtc;
            }

            try
            {
                DateTime localTime = DateTime.FromOADate(frameTimestampOaDate);
                return DateTime.SpecifyKind(localTime, DateTimeKind.Local).ToUniversalTime();
            }
            catch
            {
                return recordedUtc;
            }
        }

        private static void ConfigureDataSource(object beamGage, BeamGageMeasurementOptions options)
        {
            object dataSource = GetPropertyValue(beamGage, "DataSource");
            string[] dataSourceList = ToStringArray(GetPropertyValue(dataSource, "DataSourceList"));
            if (dataSourceList.Length == 0)
            {
                throw new InvalidOperationException(
                    "BeamGage automation server started, but no data sources were detected." + Environment.NewLine +
                    "Open BeamGage Professional and confirm that the expected camera/source is visible.");
            }

            string selectedDataSource = ResolvePreferredItem(dataSourceList, options.DataSource);
            if (!string.IsNullOrWhiteSpace(selectedDataSource))
            {
                SetPropertyValue(dataSource, "DataSource", selectedDataSource);
            }

            string currentDataSource = Convert.ToString(GetPropertyValue(dataSource, "DataSource"), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(currentDataSource))
            {
                throw new InvalidOperationException(
                    "BeamGage did not report an active data source after initialization.");
            }
        }

        private static void ConfigurePowerMeter(object beamGage, BeamGageMeasurementOptions options)
        {
            object powerMeter = GetPropertyValue(beamGage, "PowerMeter");
            string[] powerMeterList = ToStringArray(GetPropertyValue(powerMeter, "PowerMeterList"));
            string selectedPowerMeter = ResolvePreferredItem(powerMeterList, options.PowerMeter);
            if (!string.IsNullOrWhiteSpace(selectedPowerMeter))
            {
                SetPropertyValue(powerMeter, "PowerMeter", selectedPowerMeter);
            }

            string currentPowerMeter = Convert.ToString(GetPropertyValue(powerMeter, "PowerMeter"), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(currentPowerMeter))
            {
                return;
            }

            string[] waveLengths = ToStringArray(GetPropertyValue(powerMeter, "AvailableWaveLengths"));
            string selectedWaveLength = ResolvePreferredItem(waveLengths, options.WaveLength);
            if (!string.IsNullOrWhiteSpace(selectedWaveLength))
            {
                SetPropertyValue(powerMeter, "WaveLength", selectedWaveLength);
            }
        }

        private static void ConfigurePowerEnergy(object beamGage, BeamGageMeasurementOptions options)
        {
            object powerEnergy = GetPropertyValue(beamGage, "PowerEnergy");
            if (powerEnergy == null)
            {
                return;
            }

            if (options.ResetPowerEnergyCalibrationOnStart)
            {
                InvokeMethod(powerEnergy, "RemoveCalibration");
            }

            if (!options.PowerEnergyCalibrationValue.HasValue)
            {
                return;
            }

            Type unitBaseType = GetRequiredType(AutomationAssemblyName, "Spiricon.Automation.AutomationPwrEngyUnitBase");
            object unitBase = ParseEnumValue(
                unitBaseType,
                options.PowerEnergyCalibrationUnitBase,
                "BeamGage power/energy calibration base unit",
                "JOULES");

            if (string.IsNullOrWhiteSpace(options.PowerEnergyCalibrationUnitQuantifier))
            {
                InvokeMethod(powerEnergy, "CalibrateFrame", options.PowerEnergyCalibrationValue.Value, unitBase);
                return;
            }

            Type quantifierType = GetRequiredType(AutomationAssemblyName, "Spiricon.Automation.AutomationPwrEngyUnitQuantifier");
            object quantifier = ParseEnumValue(
                quantifierType,
                options.PowerEnergyCalibrationUnitQuantifier,
                "BeamGage power/energy calibration quantifier",
                null);

            InvokeMethod(powerEnergy, "CalibrateFrame", options.PowerEnergyCalibrationValue.Value, unitBase, quantifier);
        }

        private static string ResolvePreferredItem(string[] items, string preferredValue)
        {
            if (items == null || items.Length == 0)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(preferredValue))
            {
                return items[0];
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i], preferredValue, StringComparison.OrdinalIgnoreCase))
                {
                    return items[i];
                }
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IndexOf(preferredValue, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return items[i];
                }
            }

            throw new InvalidOperationException(
                "BeamGage could not find the configured item: " + preferredValue);
        }

        private static object CreateBeamGageClient(Type automatedBeamGageType, BeamGageMeasurementOptions options)
        {
            string previousCurrentDirectory = Environment.CurrentDirectory;
            try
            {
                string instanceId = string.IsNullOrWhiteSpace(options.AutomationInstanceId)
                    ? BeamGageMeasurementOptions.Default.AutomationInstanceId
                    : options.AutomationInstanceId;
                if (!string.IsNullOrWhiteSpace(_loadedInstallPath))
                {
                    Environment.CurrentDirectory = _loadedInstallPath;
                }

                return Activator.CreateInstance(automatedBeamGageType, instanceId, options.ShowGui);
            }
            catch (TargetInvocationException ex)
            {
                throw CreateStartupException(ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                throw CreateStartupException(ex);
            }
            finally
            {
                Environment.CurrentDirectory = previousCurrentDirectory;
            }
        }

        private static Exception CreateStartupException(Exception ex)
        {
            string message =
                "BeamGage automation server failed to start." + Environment.NewLine +
                "Make sure BeamGage Professional is installed, the process is launched from an STA thread, and the current user can start the vendor automation server." + Environment.NewLine +
                "Current apartment state: " + Thread.CurrentThread.GetApartmentState() + Environment.NewLine +
                "BeamGage install path: " + (_loadedInstallPath ?? "unknown") + Environment.NewLine +
                "Current directory during caller context: " + Environment.CurrentDirectory + Environment.NewLine +
                "Original error: " + ex.Message;

            if (ex.Message.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                message += Environment.NewLine +
                    "BeamGage commonly throws 'Access denied' when it is created from a non-STA thread or when the remoting server cannot be started in the current context.";
            }

            if (ex.Message.IndexOf("object reference", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ex.Message.IndexOf("Ссылка на объект", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                message += Environment.NewLine +
                    "The vendor constructor raised a null-reference internally. This usually means one of the BeamGage support assemblies or configuration files was not available in the expected context.";
            }

            return new BeamGagePrerequisiteException(message, ex);
        }

        private static void EnsureStaThread()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                throw new BeamGagePrerequisiteException(
                    "BeamGage automation must be initialized from an STA thread." + Environment.NewLine +
                    "The application UI thread is STA, but this call was made from " +
                    Thread.CurrentThread.GetApartmentState().ToString() + ".");
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
            lock (AssemblyLoaderGate)
            {
                if (string.Equals(_loadedInstallPath, installPath, StringComparison.OrdinalIgnoreCase) &&
                    _assemblyResolveHandler != null)
                {
                    return;
                }

                _loadedInstallPath = installPath;
                EnsureAssemblyResolveHandler();

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
        }

        private static void EnsureAssemblyResolveHandler()
        {
            if (_assemblyResolveHandler != null)
            {
                return;
            }

            _assemblyResolveHandler = delegate(object sender, ResolveEventArgs args)
            {
                if (string.IsNullOrWhiteSpace(_loadedInstallPath))
                {
                    return null;
                }

                AssemblyName requestedAssembly = new AssemblyName(args.Name);
                string candidatePath = Path.Combine(_loadedInstallPath, requestedAssembly.Name + ".dll");
                if (!File.Exists(candidatePath))
                {
                    return null;
                }

                try
                {
                    return Assembly.LoadFrom(candidatePath);
                }
                catch
                {
                    return null;
                }
            };

            AppDomain.CurrentDomain.AssemblyResolve += _assemblyResolveHandler;
        }

        private static Type GetRequiredType(string assemblyFileName, string typeName)
        {
            string simpleAssemblyName = Path.GetFileNameWithoutExtension(assemblyFileName);
            Assembly assembly = null;
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Assembly candidate = loadedAssemblies[i];
                if (string.Equals(candidate.GetName().Name, simpleAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    assembly = candidate;
                    break;
                }
            }

            if (assembly == null)
            {
                throw new BeamGagePrerequisiteException(
                    "BeamGage automation assembly was loaded, but is not visible in the current application domain: " + simpleAssemblyName);
            }

            Type type = assembly.GetType(typeName, false);
            if (type == null)
            {
                throw new BeamGagePrerequisiteException(
                    "BeamGage automation type was not found: " + typeName);
            }

            return type;
        }

        private static BeamGageMeasurementOptions CloneOptions(BeamGageMeasurementOptions options)
        {
            return new BeamGageMeasurementOptions
            {
                AutomationInstanceId = options.AutomationInstanceId,
                ShowGui = options.ShowGui,
                DataSource = options.DataSource,
                PowerMeter = options.PowerMeter,
                WaveLength = options.WaveLength,
                TimestampStrategy = options.TimestampStrategy,
                ResetPowerEnergyCalibrationOnStart = options.ResetPowerEnergyCalibrationOnStart,
                PowerEnergyCalibrationValue = options.PowerEnergyCalibrationValue,
                PowerEnergyCalibrationUnitBase = options.PowerEnergyCalibrationUnitBase,
                PowerEnergyCalibrationUnitQuantifier = options.PowerEnergyCalibrationUnitQuantifier
            };
        }

        private void RefreshFrameMetadata()
        {
            try
            {
                object resultsPriorityFrame = GetPropertyValue(_beamGage, "ResultsPriorityFrame");
                object frameInfoResults = GetPropertyValue(_beamGage, "FrameInfoResults");
                CurrentEnergyUnitBase = Convert.ToString(GetPropertyValue(resultsPriorityFrame, "EnergyUnitsBase"), CultureInfo.InvariantCulture);
                CurrentEnergyUnitQuantifier = Convert.ToString(GetPropertyValue(resultsPriorityFrame, "EnergyUnitsQuantifier"), CultureInfo.InvariantCulture);
                CurrentScaleMultiplier = TryToNullableDouble(GetPropertyValue(frameInfoResults, "ScaleMultiplier"));
            }
            catch
            {
                CurrentEnergyUnitBase = string.Empty;
                CurrentEnergyUnitQuantifier = string.Empty;
                CurrentScaleMultiplier = null;
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

        private static bool ToBoolean(object value)
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static double ToDouble(object value)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static double? TryToNullableDouble(object value)
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                return ToDouble(value);
            }
            catch
            {
                return null;
            }
        }

        private static object ParseEnumValue(Type enumType, string rawValue, string optionName, string fallbackValue)
        {
            if (enumType == null)
            {
                throw new ArgumentNullException("enumType");
            }

            string effectiveValue = string.IsNullOrWhiteSpace(rawValue) ? fallbackValue : rawValue;
            if (string.IsNullOrWhiteSpace(effectiveValue))
            {
                throw new InvalidOperationException(optionName + " is required.");
            }

            try
            {
                return Enum.Parse(enumType, effectiveValue, true);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(
                    optionName + " is invalid: " + effectiveValue + ". " +
                    "Expected one of the BeamGage vendor enum values.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }

    internal sealed class BeamGageFrameAvailableEventArgs : EventArgs
    {
        public BeamGageFrameAvailableEventArgs(BeamGageFrameSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public BeamGageFrameSnapshot Snapshot { get; private set; }
    }

    internal sealed class BeamGageFaultEventArgs : EventArgs
    {
        public BeamGageFaultEventArgs(string message, Exception exception)
        {
            Message = message;
            Exception = exception;
        }

        public string Message { get; private set; }

        public Exception Exception { get; private set; }
    }

    internal sealed class BeamGageFrameSnapshot
    {
        public BeamGageFrameSnapshot(long frameId, DateTime timestampUtc, DateTime recordedUtc, double energy)
        {
            FrameId = frameId;
            TimestampUtc = timestampUtc;
            RecordedUtc = recordedUtc;
            Energy = energy;
        }

        public long FrameId { get; private set; }

        public DateTime TimestampUtc { get; private set; }

        public DateTime RecordedUtc { get; private set; }

        public double Energy { get; private set; }
    }
}
