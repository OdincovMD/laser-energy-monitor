using System;
using System.Globalization;
using System.Threading;
using Spiricon.Automation;

namespace LaserEnergyMonitor.BeamGageVendorProbe
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            ProbeOptions options;
            if (!ProbeOptions.TryParse(args, out options))
            {
                ProbeOptions.PrintUsage();
                return 2;
            }

            try
            {
                Runner runner = new Runner(options);
                runner.Run();
                return runner.CallbackCount > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Probe failed.");
                Console.WriteLine(ex);
                return 3;
            }
        }
    }

    internal sealed class Runner
    {
        private readonly object _gate = new object();
        private readonly ProbeOptions _options;
        private AutomatedBeamGage _bg;
        private AutomationFrameEvents _frameEvents;
        private int _callbackCount;
        private int _readErrorCount;
        private long _firstFrameId;
        private long _lastFrameId;
        private double? _minTotal;
        private double? _maxTotal;
        private string _lastReadError;

        public Runner(ProbeOptions options)
        {
            _options = options;
        }

        public int CallbackCount
        {
            get
            {
                lock (_gate)
                {
                    return _callbackCount;
                }
            }
        }

        public void Run()
        {
            Console.WriteLine("BeamGage Vendor Probe");
            Console.WriteLine("Apartment: " + Thread.CurrentThread.GetApartmentState());
            Console.WriteLine("Instance: " + _options.InstanceName);
            Console.WriteLine("Duration: " + _options.Duration);
            Console.WriteLine("Show GUI: true");
            Console.WriteLine("Start acquisition: " + (_options.StartAcquisition ? "true" : "false"));
            Console.WriteLine();

            try
            {
                _bg = new AutomatedBeamGage(_options.InstanceName, true);

                if (_options.RunUltracal)
                {
                    Console.Write("Ultracalling.... ");
                    _bg.Calibration.Ultracal();
                    Console.WriteLine("finished");
                }

                _frameEvents = new AutomationFrameEvents(_bg.ResultsPriorityFrame);
                _frameEvents.OnNewFrame += NewFrameFunction;

                if (_options.StartAcquisition)
                {
                    _bg.DataSource.Start();
                }

                Console.WriteLine("Waiting for OnNewFrame callbacks...");
                Thread.Sleep(_options.Duration);

                if (_options.StartAcquisition)
                {
                    TryStopDataSource();
                }
            }
            finally
            {
                if (_frameEvents != null)
                {
                    _frameEvents.OnNewFrame -= NewFrameFunction;
                }

                PrintSummary();
                ShutdownBeamGage();
            }
        }

        private void NewFrameFunction()
        {
            try
            {
                long frameId = Convert.ToInt64(_bg.FrameInfoResults.ID, CultureInfo.InvariantCulture);
                double total = Convert.ToDouble(_bg.PowerEnergyResults.Total, CultureInfo.InvariantCulture);

                lock (_gate)
                {
                    _callbackCount += 1;
                    if (_firstFrameId == 0L)
                    {
                        _firstFrameId = frameId;
                    }

                    _lastFrameId = frameId;
                    _minTotal = !_minTotal.HasValue ? total : Math.Min(_minTotal.Value, total);
                    _maxTotal = !_maxTotal.HasValue ? total : Math.Max(_maxTotal.Value, total);
                }

                Console.WriteLine(
                    "Frame # " + frameId.ToString(CultureInfo.InvariantCulture) +
                    ", Total: " + total.ToString("G17", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _callbackCount += 1;
                    _readErrorCount += 1;
                    _lastReadError = ex.Message;
                }

                Console.WriteLine("Frame callback read error: " + ex.Message);
            }
        }

        private void PrintSummary()
        {
            int callbacks;
            int readErrors;
            long firstFrameId;
            long lastFrameId;
            double? minTotal;
            double? maxTotal;
            string lastReadError;

            lock (_gate)
            {
                callbacks = _callbackCount;
                readErrors = _readErrorCount;
                firstFrameId = _firstFrameId;
                lastFrameId = _lastFrameId;
                minTotal = _minTotal;
                maxTotal = _maxTotal;
                lastReadError = _lastReadError;
            }

            Console.WriteLine();
            Console.WriteLine("Summary");
            Console.WriteLine("OnNewFrame callbacks: " + callbacks.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Frame read errors: " + readErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("First FrameInfoResults.ID: " + (firstFrameId > 0L ? firstFrameId.ToString(CultureInfo.InvariantCulture) : "n/a"));
            Console.WriteLine("Last FrameInfoResults.ID: " + (lastFrameId > 0L ? lastFrameId.ToString(CultureInfo.InvariantCulture) : "n/a"));
            Console.WriteLine(
                "PowerEnergyResults.Total min/max: " +
                (minTotal.HasValue && maxTotal.HasValue
                    ? minTotal.Value.ToString("G17", CultureInfo.InvariantCulture) + " / " + maxTotal.Value.ToString("G17", CultureInfo.InvariantCulture)
                    : "n/a"));
            Console.WriteLine("Last read error: " + (string.IsNullOrWhiteSpace(lastReadError) ? "none" : lastReadError));
        }

        private void TryStopDataSource()
        {
            try
            {
                _bg.DataSource.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine("DataSource.Stop failed: " + ex.Message);
            }
        }

        private void ShutdownBeamGage()
        {
            if (_bg == null)
            {
                return;
            }

            try
            {
                _bg.Instance.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine("BeamGage shutdown failed: " + ex.Message);
            }
        }
    }

    internal sealed class ProbeOptions
    {
        private ProbeOptions()
        {
            InstanceName = "VendorProbe";
            Duration = TimeSpan.FromSeconds(10);
            StartAcquisition = true;
        }

        public string InstanceName { get; private set; }

        public TimeSpan Duration { get; private set; }

        public bool StartAcquisition { get; private set; }

        public bool RunUltracal { get; private set; }

        public static bool TryParse(string[] args, out ProbeOptions options)
        {
            options = new ProbeOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(arg, "--instance", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        return false;
                    }

                    options.InstanceName = args[++i];
                    continue;
                }

                if (string.Equals(arg, "--seconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        return false;
                    }

                    double seconds;
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) ||
                        seconds <= 0d)
                    {
                        return false;
                    }

                    options.Duration = TimeSpan.FromSeconds(seconds);
                    continue;
                }

                if (string.Equals(arg, "--no-start", StringComparison.OrdinalIgnoreCase))
                {
                    options.StartAcquisition = false;
                    continue;
                }

                if (string.Equals(arg, "--ultracal", StringComparison.OrdinalIgnoreCase))
                {
                    options.RunUltracal = true;
                    continue;
                }

                return false;
            }

            return true;
        }

        public static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  LaserEnergyMonitor.BeamGageVendorProbe.exe [--seconds 10] [--instance VendorProbe] [--no-start] [--ultracal]");
            Console.WriteLine();
            Console.WriteLine("Default flow:");
            Console.WriteLine("  new AutomatedBeamGage(instance, true)");
            Console.WriteLine("  new AutomationFrameEvents(_bg.ResultsPriorityFrame).OnNewFrame += NewFrameFunction");
            Console.WriteLine("  _bg.DataSource.Start()");
            Console.WriteLine("  read _bg.FrameInfoResults.ID and _bg.PowerEnergyResults.Total in the callback");
        }
    }
}
