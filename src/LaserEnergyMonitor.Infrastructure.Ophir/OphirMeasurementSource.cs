using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    public sealed class OphirMeasurementSource : IMeasurementSource
    {
        private const string ProgId = "OphirLMMeasurement.CoLMMeasurement";
        private readonly object _gate = new object();
        private CancellationTokenSource _cts;
        private Task _pollingTask;
        private object _comObject;
        private int _deviceHandle;
        private int _channel;
        private long _sequence;

        public string SourceId
        {
            get { return "Ophir"; }
        }

        public bool IsConnected { get; private set; }

        public event EventHandler<MeasurementReceivedEventArgs> MeasurementReceived;
        public event EventHandler<DeviceFaultEventArgs> Faulted;

        public void Initialize()
        {
            lock (_gate)
            {
                if (IsConnected)
                {
                    return;
                }

                _comObject = CreateRuntimeInstance();
                string serialNumber = GetFirstAvailableSerialNumber();
                _deviceHandle = OpenDevice(serialNumber);
                _channel = GetFirstAvailableChannel(_deviceHandle);
                IsConnected = true;
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (!IsConnected || _comObject == null)
                {
                    throw new InvalidOperationException("Ophir source is not initialized.");
                }

                if (_pollingTask != null && !_pollingTask.IsCompleted)
                {
                    return;
                }

                Invoke("StartStream", _deviceHandle, _channel);
                _cts = new CancellationTokenSource();
                _pollingTask = Task.Factory.StartNew(
                    () => PollMeasurements(_cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                }

                if (_comObject != null && _deviceHandle != 0)
                {
                    TryInvoke("StopStream", _deviceHandle, _channel);
                }
            }
        }

        public void Dispose()
        {
            Stop();

            lock (_gate)
            {
                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }

                if (_comObject != null)
                {
                    if (_deviceHandle != 0)
                    {
                        TryInvoke("Close", _deviceHandle);
                    }

                    TryInvoke("CloseAll");
                }

                _pollingTask = null;

                if (Marshal.IsComObject(_comObject))
                {
                    Marshal.FinalReleaseComObject(_comObject);
                }

                _comObject = null;
                _deviceHandle = 0;
                _channel = 0;
                IsConnected = false;
            }
        }

        private void PollMeasurements(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    object[] dataResponse = GetDataBatch(_deviceHandle, _channel);
                    double[] data = dataResponse[0] as double[];
                    double[] timestamps = dataResponse[1] as double[];
                    int[] statuses = dataResponse[2] as int[];

                    if (data != null && timestamps != null && statuses != null)
                    {
                        PublishSamples(data, timestamps, statuses);
                    }

                    Thread.Sleep(50);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Faulted?.Invoke(
                        this,
                        new DeviceFaultEventArgs(
                            new DeviceFault
                            {
                                SourceId = SourceId,
                                Severity = FaultSeverity.Critical,
                                Message = "Ophir streaming failed: " + ex.Message,
                                TimestampUtc = DateTime.UtcNow,
                                Exception = ex
                            }));
                    break;
                }
            }
        }

        private void PublishSamples(double[] data, double[] timestamps, int[] statuses)
        {
            int sampleCount = Math.Min(data.Length, Math.Min(timestamps.Length, statuses.Length));
            for (int i = 0; i < sampleCount; i++)
            {
                int measurementType = statuses[i] / 0x10000;
                int status = statuses[i] % 0x10000;

                if (measurementType != 0 || status != 0)
                {
                    continue;
                }

                MeasurementReceived?.Invoke(
                    this,
                    new MeasurementReceivedEventArgs(
                        new MeasurementSample
                        {
                            SourceId = SourceId,
                            SequenceNumber = Interlocked.Increment(ref _sequence),
                            TimestampUtc = ConvertTimestampToUtc(timestamps[i]),
                            MonotonicTicks = DateTime.UtcNow.Ticks,
                            Energy = data[i]
                        }));
            }
        }

        private static DateTime ConvertTimestampToUtc(double timestamp)
        {
            DateTime utcNow = DateTime.UtcNow;
            if (timestamp <= 0)
            {
                return utcNow;
            }

            try
            {
                return DateTime.SpecifyKind(DateTime.Today.AddSeconds(timestamp), DateTimeKind.Local).ToUniversalTime();
            }
            catch
            {
                return utcNow;
            }
        }

        private object CreateRuntimeInstance()
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

        private string GetFirstAvailableSerialNumber()
        {
            object serialNumbersObject = InvokeWithOutObject("ScanUSB");
            Array serialNumbers = serialNumbersObject as Array;

            if (serialNumbers == null || serialNumbers.Length == 0)
            {
                throw new InvalidOperationException("Ophir SDK is available, but no USB devices were found.");
            }

            string serialNumber = Convert.ToString(serialNumbers.GetValue(0), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                throw new InvalidOperationException("Ophir SDK returned an empty device serial number.");
            }

            return serialNumber;
        }

        private int OpenDevice(string serialNumber)
        {
            object handleObject = InvokeWithOutObject("OpenUSBDevice", serialNumber);
            int handle = Convert.ToInt32(handleObject, CultureInfo.InvariantCulture);
            if (handle == 0)
            {
                throw new InvalidOperationException("Ophir SDK did not return a valid device handle.");
            }

            return handle;
        }

        private int GetFirstAvailableChannel(int deviceHandle)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                object existsObject = InvokeWithOutObject("IsSensorExists", deviceHandle, channel);
                bool exists = Convert.ToBoolean(existsObject, CultureInfo.InvariantCulture);
                if (exists)
                {
                    return channel;
                }
            }

            throw new InvalidOperationException("Ophir device was opened, but no active sensor heads were found.");
        }

        private object Invoke(string methodName, params object[] args)
        {
            if (_comObject == null)
            {
                throw new InvalidOperationException("Ophir COM object is not available.");
            }

            try
            {
                return _comObject.GetType().InvokeMember(
                    methodName,
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    _comObject,
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

        private object[] GetDataBatch(int deviceHandle, int channel)
        {
            object[] args =
            {
                deviceHandle,
                channel,
                null,
                null,
                null
            };

            Invoke("GetData", args);

            object[] values =
            {
                args[2],
                args[3],
                args[4]
            };

            if (values[0] == null || values[1] == null || values[2] == null)
            {
                throw new InvalidOperationException("Ophir SDK returned an unexpected GetData payload.");
            }

            return values;
        }

        private object InvokeWithOutObject(string methodName, params object[] inputArgs)
        {
            object[] args = new object[inputArgs.Length + 1];
            for (int i = 0; i < inputArgs.Length; i++)
            {
                args[i] = inputArgs[i];
            }

            args[args.Length - 1] = null;
            Invoke(methodName, args);
            return args[args.Length - 1];
        }
    }
}
