using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LaserEnergyMonitor.Domain;
using LaserEnergyMonitor.Infrastructure.Ophir;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class StarLabLogMeasurementSourceTests
    {
        [Fact]
        public void Start_WhenStartAtBeginning_PublishesExistingStarLabRows()
        {
            string path = CreateTempLogFile();
            File.WriteAllText(
                path,
                CreateHeader() +
                "         0.000000\t  4.024e-003 \t  4.024e-003 \t  4.251e-003 \t" + Environment.NewLine);

            using (StarLabLogMeasurementSource source = new StarLabLogMeasurementSource(
                new StarLabLogMeasurementOptions
                {
                    LogFilePath = path,
                    EnergyColumnName = "Math M",
                    PollInterval = TimeSpan.FromMilliseconds(10),
                    StartAtEnd = false
                }))
            using (ManualResetEventSlim received = new ManualResetEventSlim(false))
            {
                List<MeasurementSample> samples = new List<MeasurementSample>();
                source.MeasurementReceived += delegate(object sender, MeasurementReceivedEventArgs args)
                {
                    samples.Add(args.Sample);
                    received.Set();
                };

                source.Initialize();
                source.Start();

                Assert.True(received.Wait(TimeSpan.FromSeconds(2)));
                source.Stop();

                Assert.Single(samples);
                Assert.Equal("Ophir", samples[0].SourceId);
                Assert.Equal(4.251e-003, samples[0].Energy, 9);
                Assert.Equal(1L, samples[0].SequenceNumber);
            }
        }

        [Fact]
        public void Start_WhenStartAtEnd_PublishesRowsAppendedAfterStart()
        {
            string path = CreateTempLogFile();
            File.WriteAllText(path, CreateHeader());

            using (StarLabLogMeasurementSource source = new StarLabLogMeasurementSource(
                new StarLabLogMeasurementOptions
                {
                    LogFilePath = path,
                    EnergyColumnName = "Math M",
                    PollInterval = TimeSpan.FromMilliseconds(10),
                    StartAtEnd = true
                }))
            using (ManualResetEventSlim received = new ManualResetEventSlim(false))
            {
                List<MeasurementSample> samples = new List<MeasurementSample>();
                source.MeasurementReceived += delegate(object sender, MeasurementReceivedEventArgs args)
                {
                    samples.Add(args.Sample);
                    received.Set();
                };

                source.Initialize();
                source.Start();

                File.AppendAllText(
                    path,
                    "         1.009374\t  4.010e-003 \t  4.017e-003 \t  4.243e-003 \t" + Environment.NewLine);

                Assert.True(received.Wait(TimeSpan.FromSeconds(2)));
                source.Stop();

                Assert.Single(samples);
                Assert.Equal(4.243e-003, samples[0].Energy, 9);
            }
        }

        private static string CreateTempLogFile()
        {
            string directory = Path.Combine(Path.GetTempPath(), "LaserEnergyMonitorTests");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".txt");
        }

        private static string CreateHeader()
        {
            return
                ";PC Software:StarLab Version 2.40 Build 8" + Environment.NewLine +
                ";First Pulse Arrived : 16/06/2026 at 10:43:36.983876" + Environment.NewLine +
                "    Timestamp    \t  Channel A  \t  F(A)       \t  Math M     \t" + Environment.NewLine;
        }
    }
}
