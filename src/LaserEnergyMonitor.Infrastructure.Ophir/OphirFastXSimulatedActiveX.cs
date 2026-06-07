using System;
using System.Globalization;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal sealed class OphirFastXSimulatedActiveX
    {
        public const short DeviceHandle = 10;
        public const short ActiveChannel = 0;
        public const int SerialNumber = 2601001;
        public const string DeviceName = "Simulated Pulsar ActiveX";

        private bool _usbOpen;
        private bool _channelEnabled;
        private bool _streaming;
        private int _sampleIndex;

        public int OpenUSB(ref int reserved)
        {
            _usbOpen = true;
            reserved = 0;
            return 0;
        }

        public int CloseUSB()
        {
            _streaming = false;
            _channelEnabled = false;
            _usbOpen = false;
            return 0;
        }

        public int GetNumberOfDevices(ref short count)
        {
            if (!_usbOpen)
            {
                return 1001;
            }

            count = 1;
            return 0;
        }

        public int GetDeviceHandle(short index, ref short handle)
        {
            if (!_usbOpen)
            {
                return 1001;
            }

            if (index != 0)
            {
                return 1002;
            }

            handle = DeviceHandle;
            return 0;
        }

        public int GetDeviceInfo(short handle, ref int serialNumber, ref string name)
        {
            if (!IsKnownHandle(handle))
            {
                return 1003;
            }

            serialNumber = SerialNumber;
            name = DeviceName;
            return 0;
        }

        public int IsChannelExists(short handle, short channel, ref short exists)
        {
            if (!IsKnownHandle(handle))
            {
                return 1003;
            }

            exists = channel == ActiveChannel ? (short)1 : (short)0;
            return 0;
        }

        public int EnableDisableChannelForCS(short handle, short channel, short enabled)
        {
            if (!IsKnownHandle(handle) || channel != ActiveChannel)
            {
                return 1004;
            }

            _channelEnabled = enabled != 0;
            return 0;
        }

        public int StartCS2(short handle)
        {
            if (!IsKnownHandle(handle))
            {
                return 1003;
            }

            if (!_channelEnabled)
            {
                return 1005;
            }

            _streaming = true;
            return 0;
        }

        public int StopCS(short handle)
        {
            if (!IsKnownHandle(handle))
            {
                return 1003;
            }

            _streaming = false;
            return 0;
        }

        public int GetData(ref Array data)
        {
            if (!_streaming)
            {
                data = Array.Empty<double>();
                return 0;
            }

            double[] payload = new double[15];
            for (int i = 0; i < 3; i++)
            {
                int sample = _sampleIndex++;
                int offset = i * 5;
                payload[offset] = DeviceHandle;
                payload[offset + 1] = ActiveChannel;
                payload[offset + 2] = ResolveSecondsFromMidnight(sample);
                payload[offset + 3] = ComputeEnergy(sample);
                payload[offset + 4] = sample % 17 == 0 ? 2 : 0;
            }

            data = payload;
            return 0;
        }

        public int GetErrorFromCode(int errorCode, ref string message)
        {
            message = "Simulated OphirFastX error " + errorCode.ToString(CultureInfo.InvariantCulture);
            return 0;
        }

        private static bool IsKnownHandle(short handle)
        {
            return handle == DeviceHandle;
        }

        private static double ResolveSecondsFromMidnight(int sample)
        {
            DateTime localNow = DateTime.Now;
            return localNow.TimeOfDay.TotalSeconds + (sample * 0.05d);
        }

        private static double ComputeEnergy(int sample)
        {
            double wave = Math.Sin(sample / 9.0d) * 0.00008d;
            double slowDrift = Math.Sin(sample / 75.0d) * 0.00003d;
            return 0.0042d + wave + slowDrift;
        }
    }
}
