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

        private readonly object _comObject;
        private readonly short _deviceHandle;
        private readonly short _channel;
        private readonly int _ownerThreadId;
        private bool _streaming;
        private bool _disposed;

        private OphirFastXRuntimeSession(object comObject, short deviceHandle, short channel, string serialNumber)
        {
            _comObject = comObject;
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
            object comObject = CreateRuntimeInstance();
            bool usbOpened = false;

            try
            {
                OpenUsb(comObject);
                usbOpened = true;
                DeviceDescriptor device = ResolveDevice(comObject, effectiveOptions.DeviceSerialNumber);
                short channel = ResolveChannel(comObject, device.Handle, effectiveOptions.PreferredChannel);
                return new OphirFastXRuntimeSession(comObject, device.Handle, channel, device.SerialNumber);
            }
            catch
            {
                if (usbOpened)
                {
                    TryInvoke(comObject, "CloseUSB");
                }

                ReleaseComObject(comObject);
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
            ReleaseComObject(_comObject);
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

        internal static object CreateRuntimeInstance()
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

            try
            {
                return Activator.CreateInstance(runtimeType);
            }
            catch (COMException ex)
            {
                throw new OphirPrerequisiteException(
                    "The OphirFastX ActiveX type was found, but activation failed." + Environment.NewLine +
                    "Check that the x86 vendor ActiveX control and its USB driver are installed correctly.",
                    ex);
            }
        }

        internal static void OpenUsb(object comObject)
        {
            object[] args = { 0 };
            EnsureSuccess(comObject, "OpenUSB", args);
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

        internal static void ReleaseComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
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

        private static string TryGetErrorMessage(object comObject, int errorCode)
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
}
