using System;
using System.Threading;
using LaserEnergyMonitor.Infrastructure.Ophir;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class OphirFastXRuntimeSessionTests
    {
        [Fact]
        public void ParseDataBatch_FiltersSelectedDeviceAndChannel()
        {
            Array payload = new double[]
            {
                10d, 0d, 1.5d, 0.001d, 0d,
                10d, 1d, 1.6d, 0.002d, 2d,
                11d, 0d, 1.7d, 0.003d, 0d,
                10d, 0d, 1.8d, 0.004d, 3d
            };

            OphirDataBatch batch = OphirFastXRuntimeSession.ParseDataBatch(
                payload,
                10,
                0,
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

            Assert.Equal(2, batch.Count);
            Assert.Equal(new[] { 0.001d, 0.004d }, batch.Energies);
            Assert.Equal(new[] { 1.5d, 1.8d }, batch.Timestamps);
            Assert.Equal(new[] { 0, 3 }, batch.Statuses);
        }

        [Fact]
        public void ParseDataBatch_RejectsMalformedPacket()
        {
            Assert.Throws<InvalidOperationException>(
                () => OphirFastXRuntimeSession.ParseDataBatch(
                    new double[] { 10d, 0d, 1.5d },
                    10,
                    0,
                    DateTime.UtcNow));
        }

        [Fact]
        public void OpenSimulated_UsesFastXProtocolAndReturnsData()
        {
            using (StaWorker worker = new StaWorker("Simulated FastX test worker"))
            {
                worker.Invoke(
                    delegate
                    {
                        OphirMeasurementOptions options = OphirMeasurementOptions.Default;
                        options.RuntimeBackend = OphirRuntimeBackend.SimulatedPulsarFastX;

                        using (IOphirRuntimeSession session = OphirFastXRuntimeSession.OpenSimulated(options))
                        {
                            Assert.Equal("2601001", session.SerialNumber);
                            Assert.Equal(0, session.Channel);

                            session.StartStream();
                            OphirDataBatch batch = session.GetDataBatch();
                            session.StopStream();

                            Assert.Equal(3, batch.Count);
                            Assert.Contains(0, batch.Statuses);
                            Assert.All(batch.Energies, energy => Assert.True(energy > 0.0d));
                        }

                        return 0;
                    });
            }
        }

        [Fact]
        public void SimulatedBackend_PublishesAcceptedMeasurements()
        {
            OphirMeasurementOptions options = OphirMeasurementOptions.Default;
            options.RuntimeBackend = OphirRuntimeBackend.SimulatedPulsarFastX;
            options.PollInterval = TimeSpan.FromMilliseconds(10);

            using (OphirMeasurementSource source = new OphirMeasurementSource(options))
            using (ManualResetEventSlim received = new ManualResetEventSlim(false))
            {
                int sampleCount = 0;
                source.MeasurementReceived += delegate
                {
                    Interlocked.Increment(ref sampleCount);
                    received.Set();
                };

                source.Initialize();
                source.Start();

                Assert.True(received.Wait(TimeSpan.FromSeconds(2)));

                source.Stop();
                Assert.True(sampleCount > 0);
                Assert.True(source.RawSampleCount > 0);
                Assert.True(source.AcceptedSampleCount > 0);
                Assert.Equal("2601001", source.CurrentSerialNumber);
                Assert.Equal(0, source.CurrentChannel);
            }
        }
    }
}
