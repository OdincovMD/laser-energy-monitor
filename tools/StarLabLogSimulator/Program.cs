using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace LaserEnergyMonitor.StarLabLogSimulator
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            SimulatorOptions options;
            if (!SimulatorOptions.TryParse(args, out options))
            {
                SimulatorOptions.PrintUsage();
                return 2;
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                new Simulator(options).Run(cts.Token);
                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("StarLab log simulator failed: " + ex.Message);
                return 1;
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    internal sealed class Simulator
    {
        private const double MathMultiplier = 30.0d / 28.4d;
        private readonly SimulatorOptions _options;
        private readonly Random _random = new Random();
        private readonly Queue<double> _averageWindow = new Queue<double>();

        public Simulator(SimulatorOptions options)
        {
            _options = options;
        }

        public void Run(CancellationToken token)
        {
            string directory = Path.GetDirectoryName(_options.Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DateTime firstPulseLocal = DateTime.Now;
            using (FileStream stream = new FileStream(_options.Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (StreamWriter writer = new StreamWriter(stream, Encoding.Default))
            {
                WriteHeader(writer, firstPulseLocal);
                writer.Flush();

                Console.WriteLine("StarLab log simulator started.");
                Console.WriteLine("File: " + _options.Path);
                Console.WriteLine("Batch: " + _options.BatchSize.ToString(CultureInfo.InvariantCulture) + " rows every " + _options.FlushInterval.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s");
                Console.WriteLine("Press Ctrl+C to stop.");
                Console.WriteLine();

                int samplesWritten = 0;
                DateTime startedUtc = DateTime.UtcNow;
                while (!token.IsCancellationRequested && !IsDurationExpired(startedUtc))
                {
                    int rowsThisBatch = ResolveRowsThisBatch();
                    for (int i = 0; i < rowsThisBatch; i++)
                    {
                        double timestampSeconds = samplesWritten * _options.SampleInterval.TotalSeconds;
                        double channelA = GenerateEnergy(samplesWritten);
                        double filtered = AddToAverage(channelA);
                        double mathM = channelA * MathMultiplier;

                        writer.WriteLine(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "{0,17:0.000000}\t  {1:0.000e+000} \t  {2:0.000e+000} \t  {3:0.000e+000} \t",
                                timestampSeconds,
                                channelA,
                                filtered,
                                mathM));

                        samplesWritten++;
                    }

                    writer.Flush();
                    Console.WriteLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  wrote " + samplesWritten.ToString(CultureInfo.InvariantCulture) + " rows");

                    if (token.WaitHandle.WaitOne(_options.FlushInterval))
                    {
                        break;
                    }
                }
            }

            Console.WriteLine("StarLab log simulator stopped.");
        }

        private bool IsDurationExpired(DateTime startedUtc)
        {
            return _options.Duration.HasValue && DateTime.UtcNow - startedUtc >= _options.Duration.Value;
        }

        private int ResolveRowsThisBatch()
        {
            double ratio = _options.FlushInterval.TotalMilliseconds / _options.SampleInterval.TotalMilliseconds;
            int rows = (int)Math.Round(ratio);
            return rows > 0 ? rows : _options.BatchSize;
        }

        private double GenerateEnergy(int sampleIndex)
        {
            double slowWave = Math.Sin(sampleIndex / 11.0d) * _options.ModulationJoules;
            double noise = (_random.NextDouble() - 0.5d) * 2.0d * _options.NoiseJoules;
            return Math.Max(0.0d, _options.BaseEnergyJoules + slowWave + noise);
        }

        private double AddToAverage(double value)
        {
            _averageWindow.Enqueue(value);
            while (_averageWindow.Count > _options.BatchSize)
            {
                _averageWindow.Dequeue();
            }

            double total = 0.0d;
            foreach (double item in _averageWindow)
            {
                total += item;
            }

            return total / _averageWindow.Count;
        }

        private static void WriteHeader(StreamWriter writer, DateTime firstPulseLocal)
        {
            writer.WriteLine(";PC Software:StarLab Version 2.40 Build 8");
            writer.WriteLine("! ******* Warning: Do not modify this file. Changes may prevent   ********");
            writer.WriteLine("! ******* the StarLab Log reader from opening the file correctly. ********");
            writer.WriteLine(";Logged:" + firstPulseLocal.ToString("dd/MM/yyyy 'at' HH:mm:ss", CultureInfo.InvariantCulture));
            writer.WriteLine(";File Version:4");
            writer.WriteLine(";Graph Mode:Merge");
            writer.WriteLine(";Graph Type:Line");
            writer.WriteLine(";Notes:");
            writer.WriteLine();
            writer.WriteLine(";Channel A:Pulsar Sensor 1 Pyroelectric PE50BF-DFH-C (s/n:SIMULATED)  FU1.27 (s/n:SIMULATED)");
            writer.WriteLine(";Math M:A*30/28.4");
            writer.WriteLine();
            writer.WriteLine(";Channel A:Details");
            writer.WriteLine(";Name:PE50BF-DFH-C");
            writer.WriteLine(";Graph Color:RGB(0,102,255)");
            writer.WriteLine(";Units:J");
            writer.WriteLine(";Settings:Measuring:Energy");
            writer.WriteLine(";Settings:Wavelength:1064");
            writer.WriteLine(";Settings:Range:20.0mJ");
            writer.WriteLine(";Functions:Average:3 sec");
            writer.WriteLine();
            writer.WriteLine(";Math M:Details");
            writer.WriteLine(";Name:A*30/28.4");
            writer.WriteLine(";Graph Color:RGB(154,0,0)");
            writer.WriteLine();
            writer.WriteLine(";--------------------");
            writer.WriteLine();
            writer.WriteLine(";First Pulse Arrived : " + firstPulseLocal.ToString("dd/MM/yyyy 'at' HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
            writer.WriteLine("    Timestamp    \t  Channel A  \t  F(A)       \t  Math M     \t");
        }
    }

    internal sealed class SimulatorOptions
    {
        private SimulatorOptions()
        {
            Path = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "output",
                "starlab-log-simulator",
                "Data_log.txt");
            Duration = null;
            SampleInterval = TimeSpan.FromSeconds(1);
            FlushInterval = TimeSpan.FromSeconds(3);
            BatchSize = 3;
            BaseEnergyJoules = 0.004d;
            NoiseJoules = 0.00012d;
            ModulationJoules = 0.00025d;
        }

        public string Path { get; private set; }

        public TimeSpan? Duration { get; private set; }

        public TimeSpan SampleInterval { get; private set; }

        public TimeSpan FlushInterval { get; private set; }

        public int BatchSize { get; private set; }

        public double BaseEnergyJoules { get; private set; }

        public double NoiseJoules { get; private set; }

        public double ModulationJoules { get; private set; }

        public static bool TryParse(string[] args, out SimulatorOptions options)
        {
            options = new SimulatorOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(arg, "--path", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadString(args, ref i, out string value))
                    {
                        return false;
                    }

                    options.Path = value;
                    continue;
                }

                if (string.Equals(arg, "--seconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadNonNegativeDouble(args, ref i, out double seconds))
                    {
                        return false;
                    }

                    options.Duration = seconds <= 0.0d ? (TimeSpan?)null : TimeSpan.FromSeconds(seconds);
                    continue;
                }

                if (string.Equals(arg, "--sample-ms", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadPositiveDouble(args, ref i, out double milliseconds))
                    {
                        return false;
                    }

                    options.SampleInterval = TimeSpan.FromMilliseconds(milliseconds);
                    continue;
                }

                if (string.Equals(arg, "--flush-ms", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadPositiveDouble(args, ref i, out double milliseconds))
                    {
                        return false;
                    }

                    options.FlushInterval = TimeSpan.FromMilliseconds(milliseconds);
                    continue;
                }

                if (string.Equals(arg, "--base-mj", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadPositiveDouble(args, ref i, out double millijoules))
                    {
                        return false;
                    }

                    options.BaseEnergyJoules = millijoules / 1000.0d;
                    continue;
                }

                if (string.Equals(arg, "--noise-uj", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadNonNegativeDouble(args, ref i, out double microjoules))
                    {
                        return false;
                    }

                    options.NoiseJoules = microjoules / 1000000.0d;
                    continue;
                }

                if (string.Equals(arg, "--modulation-uj", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadNonNegativeDouble(args, ref i, out double microjoules))
                    {
                        return false;
                    }

                    options.ModulationJoules = microjoules / 1000000.0d;
                    continue;
                }

                return false;
            }

            if (options.SampleInterval <= TimeSpan.Zero || options.FlushInterval <= TimeSpan.Zero)
            {
                return false;
            }

            options.BatchSize = Math.Max(1, (int)Math.Round(options.FlushInterval.TotalMilliseconds / options.SampleInterval.TotalMilliseconds));
            return true;
        }

        public static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  LaserEnergyMonitor.StarLabLogSimulator.exe [--path PATH] [--seconds 60] [--sample-ms 1000] [--flush-ms 3000] [--base-mj 4.0] [--noise-uj 120] [--modulation-uj 250]");
            Console.WriteLine();
            Console.WriteLine("Defaults:");
            Console.WriteLine("  path      <exe>\\output\\starlab-log-simulator\\Data_log.txt");
            Console.WriteLine("  seconds   0 / omitted means run until Ctrl+C");
            Console.WriteLine("  sample    1 row per second");
            Console.WriteLine("  flush     3 rows every 3 seconds");
            Console.WriteLine("  energy    about 4 mJ with clearly visible drift/noise");
        }

        private static bool TryReadString(string[] args, ref int index, out string value)
        {
            value = null;
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return false;
            }

            value = args[++index];
            return true;
        }

        private static bool TryReadPositiveDouble(string[] args, ref int index, out double value)
        {
            return TryReadNonNegativeDouble(args, ref index, out value) && value > 0.0d;
        }

        private static bool TryReadNonNegativeDouble(string[] args, ref int index, out double value)
        {
            value = 0.0d;
            if (index + 1 >= args.Length)
            {
                return false;
            }

            index++;
            return double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0.0d;
        }
    }
}
