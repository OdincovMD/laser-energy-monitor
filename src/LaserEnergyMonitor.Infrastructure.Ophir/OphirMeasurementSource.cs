using System;
using System.Globalization;
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
                    object[] dataResponse = InvokeWithOutputs("GetData", _deviceHandle, _channel, null, null, null);
                    double[] data = dataResponse[2] as double[];
                    double[] timestamps = dataResponse[3] as double[];
                    int[] statuses = dataResponse[4] as int[];

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
            Type runtimeType = Type.GetTypeFromProgID(ProgId, true);
            return Activator.CreateInstance(runtimeType);
        }

        private string GetFirstAvailableSerialNumber()
        {
            object[] result = InvokeWithOutputs("ScanUSB", null);
            object serialNumbersObject = result[0];
            object[] serialNumbers = serialNumbersObject as object[];

            if (serialNumbers == null || serialNumbers.Length == 0)
            {
                throw new InvalidOperationException("Ophir SDK is available, but no USB devices were found.");
            }

            string serialNumber = Convert.ToString(serialNumbers[0], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                throw new InvalidOperationException("Ophir SDK returned an empty device serial number.");
            }

            return serialNumber;
        }

        private int OpenDevice(string serialNumber)
        {
            object[] result = InvokeWithOutputs("OpenUSBDevice", serialNumber, 0);
            int handle = Convert.ToInt32(result[1], CultureInfo.InvariantCulture);
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
                object[] result = InvokeWithOutputs("IsSensorExists", deviceHandle, channel, false);
                bool exists = Convert.ToBoolean(result[2], CultureInfo.InvariantCulture);
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
            catch (Exception ex)
            {
                throw new InvalidOperationException("Ophir SDK call failed for '" + methodName + "': " + ex.Message, ex);
            }
        }

        private object[] InvokeWithOutputs(string methodName, params object[] args)
        {
            Invoke(methodName, args);
            return args;
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
    }
}
