using System;
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
    }
}
