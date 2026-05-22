using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal sealed class OphirRuntimeSession : IDisposable
    {
        private const string ProgId = "OphirLMMeasurement.CoLMMeasurement";
        private readonly object _comObject;
        private readonly int _deviceHandle;
        private readonly int _channel;
        private bool _streaming;
        private bool _disposed;

        private OphirRuntimeSession(object comObject, int deviceHandle, int channel, string serialNumber)
        {
            _comObject = comObject;
            _deviceHandle = deviceHandle;
            _channel = channel;
            SerialNumber = serialNumber;
        }

        public string SerialNumber { get; private set; }

        public int Channel
        {
            get { return _channel; }
        }

        public static OphirRuntimeSession Open(OphirMeasurementOptions options)
        {
            OphirMeasurementOptions effectiveOptions = options ?? OphirMeasurementOptions.Default;
            object comObject = CreateRuntimeInstance();
            try
            {
                string serialNumber = ResolveSerialNumber(comObject, effectiveOptions.DeviceSerialNumber);
                int deviceHandle = OpenDevice(comObject, serialNumber);
                int channel = ResolveChannel(comObject, deviceHandle, effectiveOptions.PreferredChannel);
                return new OphirRuntimeSession(comObject, deviceHandle, channel, serialNumber);
            }
            catch
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.FinalReleaseComObject(comObject);
                }

                throw;
            }
        }

        public void StartStream()
        {
            ThrowIfDisposed();
            if (_streaming)
            {
                return;
            }

            Invoke("StartStream", _deviceHandle, _channel);
            _streaming = true;
        }

        public void StopStream()
        {
            ThrowIfDisposed();
            if (!_streaming)
            {
                return;
            }

            TryInvoke("StopStream", _deviceHandle, _channel);
            _streaming = false;
        }

        public OphirDataBatch GetDataBatch()
        {
            ThrowIfDisposed();

            object[] args =
            {
                _deviceHandle,
                _channel,
                null,
                null,
                null
            };

            Invoke("GetData", args);

            double[] energies = args[2] as double[];
            double[] timestamps = args[3] as double[];
            int[] statuses = args[4] as int[];

            if (energies == null || timestamps == null || statuses == null)
            {
                throw new InvalidOperationException("Ophir SDK returned an unexpected GetData payload.");
            }

            return new OphirDataBatch(energies, timestamps, statuses, DateTime.UtcNow);
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
                if (_deviceHandle != 0)
                {
                    TryInvoke("Close", _deviceHandle);
                }
            }
            finally
            {
                TryInvoke("CloseAll");
                if (Marshal.IsComObject(_comObject))
                {
                    Marshal.FinalReleaseComObject(_comObject);
                }
            }

            _disposed = true;
        }

        private static object CreateRuntimeInstance()
        {
            Type runtimeType = Type.GetTypeFromProgID(ProgId, false);
            if (runtimeType == null)
            {
                throw new OphirPrerequisiteException(
                    "Ophir SDK dependencies are not available on this machine." + Environment.NewLine +
                    "Ophir COM runtime is not registered." + Environment.NewLine +
                    "The ProgID 'OphirLMMeasurement.CoLMMeasurement' was not found." + Environment.NewLine +
                    "Install the Ophir COM runtime or vendor automation package." + Environment.NewLine +
                    "This application is built for x86, so the matching 32-bit vendor components must be installed.");
            }

            try
            {
                return Activator.CreateInstance(runtimeType);
            }
            catch (COMException ex)
            {
                throw new OphirPrerequisiteException(
                    "The Ophir COM type was found, but activation failed." + Environment.NewLine +
                    "Check that the vendor runtime is installed correctly and that its bitness matches the application's x86 target.",
                    ex);
            }
        }

        private static string ResolveSerialNumber(object comObject, string requestedSerialNumber)
        {
            object serialNumbersObject = InvokeWithOutObject(comObject, "ScanUSB");
            Array serialNumbers = serialNumbersObject as Array;

            if (serialNumbers == null || serialNumbers.Length == 0)
            {
                throw new InvalidOperationException("Ophir SDK is available, but no USB devices were found.");
            }

            string firstDetected = null;
            for (int i = 0; i < serialNumbers.Length; i++)
            {
                string candidate = Convert.ToString(serialNumbers.GetValue(i), CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (firstDetected == null)
                {
                    firstDetected = candidate;
                }

                if (!string.IsNullOrWhiteSpace(requestedSerialNumber) &&
                    string.Equals(candidate, requestedSerialNumber, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            if (!string.IsNullOrWhiteSpace(requestedSerialNumber))
            {
                throw new InvalidOperationException(
                    "Ophir SDK detected USB devices, but the configured serial number was not found: " + requestedSerialNumber);
            }

            if (string.IsNullOrWhiteSpace(firstDetected))
            {
                throw new InvalidOperationException("Ophir SDK returned only empty device serial numbers.");
            }

            return firstDetected;
        }

        private static int OpenDevice(object comObject, string serialNumber)
        {
            object handleObject = InvokeWithOutObject(comObject, "OpenUSBDevice", serialNumber);
            int handle = Convert.ToInt32(handleObject, CultureInfo.InvariantCulture);
            if (handle == 0)
            {
                throw new InvalidOperationException("Ophir SDK did not return a valid device handle.");
            }

            return handle;
        }

        private static int ResolveChannel(object comObject, int deviceHandle, int? preferredChannel)
        {
            if (preferredChannel.HasValue)
            {
                int channel = preferredChannel.Value;
                if (IsSensorExists(comObject, deviceHandle, channel))
                {
                    return channel;
                }

                throw new InvalidOperationException(
                    "Ophir device was opened, but the configured channel does not have an active sensor head: " + channel.ToString(CultureInfo.InvariantCulture));
            }

            for (int channel = 0; channel < 4; channel++)
            {
                if (IsSensorExists(comObject, deviceHandle, channel))
                {
                    return channel;
                }
            }

            throw new InvalidOperationException("Ophir device was opened, but no active sensor heads were found.");
        }

        private static bool IsSensorExists(object comObject, int deviceHandle, int channel)
        {
            object existsObject = InvokeWithOutObject(comObject, "IsSensorExists", deviceHandle, channel);
            return Convert.ToBoolean(existsObject, CultureInfo.InvariantCulture);
        }

        private object Invoke(string methodName, params object[] args)
        {
            return Invoke(_comObject, methodName, args);
        }

        private static object Invoke(object comObject, string methodName, params object[] args)
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
            catch (COMException ex)
            {
                throw CreateInvocationException(methodName, ex);
            }
            catch (Exception ex)
            {
                throw CreateInvocationException(methodName, ex);
            }
        }

        private static object InvokeWithOutObject(object comObject, string methodName, params object[] inputArgs)
        {
            object[] args = new object[inputArgs.Length + 1];
            for (int i = 0; i < inputArgs.Length; i++)
            {
                args[i] = inputArgs[i];
            }

            args[args.Length - 1] = null;
            Invoke(comObject, methodName, args);
            return args[args.Length - 1];
        }

        private void TryInvoke(string methodName, params object[] args)
        {
            try
            {
                Invoke(methodName, args);
            }
            catch
            {
            }
        }

        private static Exception CreateInvocationException(string methodName, Exception ex)
        {
            string details = BuildExceptionDetails(ex);

            if (string.Equals(methodName, "ScanUSB", StringComparison.OrdinalIgnoreCase))
            {
                return new InvalidOperationException(
                    "Ophir SDK call failed for 'ScanUSB'." + Environment.NewLine +
                    "The COM object is registered, but USB device discovery failed." + Environment.NewLine +
                    "Typical causes: the meter is not visible to StarLab, the USB driver is missing, the device is already held by another process, or the COM runtime bitness does not match the app." + Environment.NewLine +
                    details,
                    ex);
            }

            return new InvalidOperationException(
                "Ophir SDK call failed for '" + methodName + "'." + Environment.NewLine + details,
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
