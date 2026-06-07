using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal sealed class OphirFastXRuntimeSession : IOphirRuntimeSession
    {
        private static readonly string[] ProgIds =
        {
            "OPHIRFASTX.OphirFastXCtrl.1",
            "OPHIRFASTX.OphirFastXCtrl",
            "OPHIRFASTXBeta.OphirFastXCtrl.1",
            "OPHIRFASTXBeta.OphirFastXCtrl"
        };

        private readonly OphirFastXRuntimeHandle _runtimeHandle;
        private readonly object _comObject;
        private readonly short _deviceHandle;
        private readonly short _channel;
        private readonly int _ownerThreadId;
        private bool _streaming;
        private bool _disposed;

        private OphirFastXRuntimeSession(OphirFastXRuntimeHandle runtimeHandle, short deviceHandle, short channel, string serialNumber)
        {
            _runtimeHandle = runtimeHandle;
            _comObject = runtimeHandle.ComObject;
            _deviceHandle = deviceHandle;
            _channel = channel;
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            SerialNumber = serialNumber;
        }

        public string SerialNumber { get; private set; }

        public int Channel
        {
            get { return _channel; }
        }

        public static OphirFastXRuntimeSession Open(OphirMeasurementOptions options)
        {
            EnsureStaThread();
            OphirMeasurementOptions effectiveOptions = options ?? OphirMeasurementOptions.Default;
            OphirFastXRuntimeHandle runtimeHandle = CreateRuntimeInstance();
            return Open(effectiveOptions, runtimeHandle);
        }

        internal static OphirFastXRuntimeSession OpenSimulated(OphirMeasurementOptions options)
        {
            EnsureStaThread();
            OphirMeasurementOptions effectiveOptions = options ?? OphirMeasurementOptions.Default;
            OphirFastXRuntimeHandle runtimeHandle = OphirFastXRuntimeHandle.ForRawComObject(
                new OphirFastXSimulatedActiveX(),
                "Simulated OphirFastX ActiveX object");
            return Open(effectiveOptions, runtimeHandle);
        }

        private static OphirFastXRuntimeSession Open(OphirMeasurementOptions effectiveOptions, OphirFastXRuntimeHandle runtimeHandle)
        {
            object comObject = runtimeHandle.ComObject;
            bool usbOpened = false;

            try
            {
                OpenUsb(comObject);
                usbOpened = true;
                DeviceDescriptor device = ResolveDevice(comObject, effectiveOptions.DeviceSerialNumber);
                short channel = ResolveChannel(comObject, device.Handle, effectiveOptions.PreferredChannel);
                return new OphirFastXRuntimeSession(runtimeHandle, device.Handle, channel, device.SerialNumber);
            }
            catch
            {
                if (usbOpened)
                {
                    TryInvoke(comObject, "CloseUSB");
                }

                runtimeHandle.Dispose();
                throw;
            }
        }

        public void StartStream()
        {
            ThrowIfDisposed();
            EnsureThreadAffinity();
            if (_streaming)
            {
                return;
            }

            EnsureSuccess(_comObject, "EnableDisableChannelForCS", _deviceHandle, _channel, (short)1);
            EnsureSuccess(_comObject, "StartCS2", _deviceHandle);
            _streaming = true;
        }

        public void StopStream()
        {
            ThrowIfDisposed();
            EnsureThreadAffinity();
            if (!_streaming)
            {
                return;
            }

            TryInvoke(_comObject, "StopCS", _deviceHandle);
            _streaming = false;
        }

        public OphirDataBatch GetDataBatch()
        {
            ThrowIfDisposed();
            EnsureThreadAffinity();

            object[] args = { null };
            EnsureSuccess(_comObject, "GetData", args);
            return ParseDataBatch(args[0] as Array, _deviceHandle, _channel, DateTime.UtcNow);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnsureThreadAffinity();
            try
            {
                StopStream();
            }
            catch
            {
            }

            TryInvoke(_comObject, "CloseUSB");
            _runtimeHandle.Dispose();
            _disposed = true;
        }

        internal static Type FindRuntimeType(out string progId)
        {
            for (int i = 0; i < ProgIds.Length; i++)
            {
                Type runtimeType = Type.GetTypeFromProgID(ProgIds[i], false);
                if (runtimeType != null)
                {
                    progId = ProgIds[i];
                    return runtimeType;
                }
            }

            progId = null;
            return null;
        }

        internal static OphirFastXRuntimeHandle CreateRuntimeInstance()
        {
            string progId;
            Type runtimeType = FindRuntimeType(out progId);
            if (runtimeType == null)
            {
                throw new OphirPrerequisiteException(
                    "Ophir Pulsar ActiveX runtime is not registered." + Environment.NewLine +
                    "Install and register the x86 OphirFastX ActiveX control supplied by Ophir." + Environment.NewLine +
                    "Expected ProgID: OPHIRFASTX.OphirFastXCtrl.1 or OPHIRFASTXBeta.OphirFastXCtrl.1.");
            }

#if NETFRAMEWORK
            Exception hostedActivationException = null;
            try
            {
                return OphirFastXActiveXHost.Create(runtimeType, progId);
            }
            catch (Exception ex)
            {
                hostedActivationException = ex;
            }
#endif

            try
            {
                return OphirFastXRuntimeHandle.ForRawComObject(
                    Activator.CreateInstance(runtimeType),
#if NETFRAMEWORK
                    "Raw COM fallback after hosted ActiveX activation failed: " + hostedActivationException.Message
#else
                    "Raw COM activation"
#endif
                    );
            }
            catch (COMException ex)
            {
                throw new OphirPrerequisiteException(
                    "The OphirFastX ActiveX type was found, but activation failed." + Environment.NewLine +
                    "Check that the x86 vendor ActiveX control and its USB driver are installed correctly." +
#if NETFRAMEWORK
                    Environment.NewLine +
                    "Hosted ActiveX activation error: " + hostedActivationException.Message +
#endif
                    string.Empty,
                    ex);
            }
        }

        internal static int OpenUsb(object comObject)
        {
            object[] args = { 0 };
            EnsureSuccess(comObject, "OpenUSB", args);
            return Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
        }

        internal static List<DeviceDescriptor> GetDevices(object comObject)
        {
            object[] countArgs = { (short)0 };
            EnsureSuccess(comObject, "GetNumberOfDevices", countArgs);
            short count = Convert.ToInt16(countArgs[0], CultureInfo.InvariantCulture);
            List<DeviceDescriptor> devices = new List<DeviceDescriptor>();

            for (short index = 0; index < count; index++)
            {
                object[] handleArgs = { index, (short)0 };
                EnsureSuccess(comObject, "GetDeviceHandle", handleArgs);
                short handle = Convert.ToInt16(handleArgs[1], CultureInfo.InvariantCulture);
                devices.Add(GetDeviceDescriptor(comObject, handle));
            }

            return devices;
        }

        internal static List<int> GetActiveChannels(object comObject, short deviceHandle)
        {
            List<int> channels = new List<int>();
            for (short channel = 0; channel < 4; channel++)
            {
                object[] args = { deviceHandle, channel, (short)0 };
                EnsureSuccess(comObject, "IsChannelExists", args);
                if (Convert.ToInt16(args[2], CultureInfo.InvariantCulture) != 0)
                {
                    channels.Add(channel);
                }
            }

            return channels;
        }

        internal static OphirDataBatch ParseDataBatch(Array payload, short deviceHandle, short channel, DateTime recordedUtc)
        {
            if (payload == null || payload.Length == 0)
            {
                return new OphirDataBatch(null, null, null, recordedUtc);
            }

            if (payload.Length % 5 != 0)
            {
                throw new InvalidOperationException("OphirFastX returned an unexpected GetData payload.");
            }

            List<double> energies = new List<double>();
            List<double> timestamps = new List<double>();
            List<int> statuses = new List<int>();
            for (int i = 0; i < payload.Length; i += 5)
            {
                short payloadHandle = Convert.ToInt16(payload.GetValue(i), CultureInfo.InvariantCulture);
                short payloadChannel = Convert.ToInt16(payload.GetValue(i + 1), CultureInfo.InvariantCulture);
                if (payloadHandle != deviceHandle || payloadChannel != channel)
                {
                    continue;
                }

                timestamps.Add(Convert.ToDouble(payload.GetValue(i + 2), CultureInfo.InvariantCulture));
                energies.Add(Convert.ToDouble(payload.GetValue(i + 3), CultureInfo.InvariantCulture));
                statuses.Add(Convert.ToInt32(payload.GetValue(i + 4), CultureInfo.InvariantCulture));
            }

            return new OphirDataBatch(energies.ToArray(), timestamps.ToArray(), statuses.ToArray(), recordedUtc);
        }

        internal static void EnsureSuccess(object comObject, string methodName, params object[] args)
        {
            object result = Invoke(comObject, methodName, args);
            int errorCode = Convert.ToInt32(result, CultureInfo.InvariantCulture);
            if (errorCode == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                "OphirFastX call failed for '" + methodName + "'." + Environment.NewLine +
                "Error code: " + errorCode.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                "Vendor message: " + TryGetErrorMessage(comObject, errorCode));
        }

        internal static object Invoke(object comObject, string methodName, params object[] args)
        {
            try
            {
                ParameterModifier[] modifiers = CreateParameterModifiers(methodName, args == null ? 0 : args.Length);
                if (modifiers != null)
                {
                    return comObject.GetType().InvokeMember(
                        methodName,
                        BindingFlags.InvokeMethod,
                        null,
                        comObject,
                        args,
                        modifiers,
                        CultureInfo.InvariantCulture,
                        null);
                }

                return comObject.GetType().InvokeMember(
                    methodName,
                    BindingFlags.InvokeMethod,
                    null,
                    comObject,
                    args,
                    CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException ex)
            {
                throw CreateInvocationException(methodName, ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                throw CreateInvocationException(methodName, ex);
            }
        }

        private static ParameterModifier[] CreateParameterModifiers(string methodName, int parameterCount)
        {
            if (parameterCount <= 0)
            {
                return null;
            }

            ParameterModifier modifier = new ParameterModifier(parameterCount);
            bool hasByRefParameter = false;

            if (string.Equals(methodName, "OpenUSB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(methodName, "GetNumberOfDevices", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(methodName, "GetData", StringComparison.OrdinalIgnoreCase))
            {
                modifier[0] = true;
                hasByRefParameter = true;
            }
            else if (string.Equals(methodName, "GetDeviceHandle", StringComparison.OrdinalIgnoreCase) &&
                parameterCount > 1)
            {
                modifier[1] = true;
                hasByRefParameter = true;
            }
            else if (string.Equals(methodName, "GetDeviceInfo", StringComparison.OrdinalIgnoreCase) &&
                parameterCount > 2)
            {
                modifier[1] = true;
                modifier[2] = true;
                hasByRefParameter = true;
            }
            else if (string.Equals(methodName, "IsChannelExists", StringComparison.OrdinalIgnoreCase) &&
                parameterCount > 2)
            {
                modifier[2] = true;
                hasByRefParameter = true;
            }
            else if (string.Equals(methodName, "GetErrorFromCode", StringComparison.OrdinalIgnoreCase) &&
                parameterCount > 1)
            {
                modifier[1] = true;
                hasByRefParameter = true;
            }

            if (!hasByRefParameter)
            {
                return null;
            }

            return new[] { modifier };
        }

        internal static void TryInvoke(object comObject, string methodName, params object[] args)
        {
            try
            {
                Invoke(comObject, methodName, args);
            }
            catch
            {
            }
        }

        private static DeviceDescriptor ResolveDevice(object comObject, string requestedSerialNumber)
        {
            List<DeviceDescriptor> devices = GetDevices(comObject);
            if (devices.Count == 0)
            {
                throw new InvalidOperationException(
                    "OphirFastX runtime is available, but no Pulsar USB devices were found.");
            }

            if (string.IsNullOrWhiteSpace(requestedSerialNumber))
            {
                return devices[0];
            }

            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].SerialNumber, requestedSerialNumber, StringComparison.OrdinalIgnoreCase))
                {
                    return devices[i];
                }
            }

            throw new InvalidOperationException(
                "OphirFastX detected Pulsar USB devices, but the configured serial number was not found: " + requestedSerialNumber);
        }

        private static DeviceDescriptor GetDeviceDescriptor(object comObject, short handle)
        {
            object[] args = { handle, 0, null };
            EnsureSuccess(comObject, "GetDeviceInfo", args);
            return new DeviceDescriptor(
                handle,
                Convert.ToString(args[1], CultureInfo.InvariantCulture),
                Convert.ToString(args[2], CultureInfo.InvariantCulture));
        }

        private static short ResolveChannel(object comObject, short deviceHandle, int? preferredChannel)
        {
            List<int> activeChannels = GetActiveChannels(comObject, deviceHandle);
            if (preferredChannel.HasValue)
            {
                if (activeChannels.Contains(preferredChannel.Value))
                {
                    return Convert.ToInt16(preferredChannel.Value, CultureInfo.InvariantCulture);
                }

                throw new InvalidOperationException(
                    "OphirFastX opened the Pulsar device, but the configured channel does not have an active sensor head: " +
                    preferredChannel.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (activeChannels.Count > 0)
            {
                return Convert.ToInt16(activeChannels[0], CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException("OphirFastX opened the Pulsar device, but no active sensor heads were found.");
        }

        internal static string TryGetErrorMessage(object comObject, int errorCode)
        {
            try
            {
                object[] args = { errorCode, null };
                Invoke(comObject, "GetErrorFromCode", args);
                return Convert.ToString(args[1], CultureInfo.InvariantCulture);
            }
            catch
            {
                return "n/a";
            }
        }

        private static Exception CreateInvocationException(string methodName, Exception ex)
        {
            if (string.Equals(methodName, "OpenUSB", StringComparison.OrdinalIgnoreCase))
            {
                return new InvalidOperationException(
                    "OphirFastX SDK call failed for 'OpenUSB'." + Environment.NewLine +
                    "The ActiveX type is registered and activated, but the vendor USB layer could not be initialized." + Environment.NewLine +
                    "Typical causes: the Pulsar driver is missing or mismatched, StarLab/Ophir software already holds the device, another OphirFastX instance is active, or the installed vendor package does not match the app's x86 runtime." + Environment.NewLine +
                    BuildExceptionDetails(ex),
                    ex);
            }

            return new InvalidOperationException(
                "OphirFastX SDK call failed for '" + methodName + "'." + Environment.NewLine +
                BuildExceptionDetails(ex),
                ex);
        }

        private static string BuildExceptionDetails(Exception ex)
        {
            StringBuilder builder = new StringBuilder();
            Exception current = ex;
            int depth = 0;

            while (current != null && depth < 5)
            {
                if (depth > 0)
                {
                    builder.Append(" -> ");
                }

                builder.Append(current.GetType().FullName);
                builder.Append(": ");
                builder.Append(current.Message);

                COMException comException = current as COMException;
                if (comException != null)
                {
                    builder.Append(" (HRESULT=0x");
                    builder.Append(comException.ErrorCode.ToString("X8", CultureInfo.InvariantCulture));
                    builder.Append(")");
                }

                current = current.InnerException;
                depth++;
            }

            return builder.ToString();
        }

        private static void EnsureStaThread()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                throw new InvalidOperationException("OphirFastX automation must be initialized from an STA thread.");
            }
        }

        private void EnsureThreadAffinity()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "OphirFastX automation must be used from the same STA thread that created the COM object.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        internal sealed class DeviceDescriptor
        {
            public DeviceDescriptor(short handle, string serialNumber, string name)
            {
                Handle = handle;
                SerialNumber = serialNumber;
                Name = name;
            }

            public short Handle { get; private set; }

            public string SerialNumber { get; private set; }

            public string Name { get; private set; }
        }
    }

    internal sealed class OphirFastXRuntimeHandle : IDisposable
    {
        private readonly IDisposable _lease;
        private bool _disposed;

        private OphirFastXRuntimeHandle(object comObject, string activationMode, IDisposable lease)
        {
            ComObject = comObject;
            ActivationMode = activationMode;
            _lease = lease;
        }

        public object ComObject { get; private set; }

        public string ActivationMode { get; private set; }

        public static OphirFastXRuntimeHandle ForRawComObject(object comObject, string activationMode)
        {
            return new OphirFastXRuntimeHandle(comObject, activationMode, null);
        }

        public static OphirFastXRuntimeHandle ForHostedActiveX(object comObject, string activationMode, IDisposable lease)
        {
            return new OphirFastXRuntimeHandle(comObject, activationMode, lease);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_lease != null)
            {
                _lease.Dispose();
                return;
            }

            if (ComObject != null && Marshal.IsComObject(ComObject))
            {
                Marshal.FinalReleaseComObject(ComObject);
            }
        }
    }
}
