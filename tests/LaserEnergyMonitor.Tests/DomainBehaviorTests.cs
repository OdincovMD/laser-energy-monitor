using System;
using LaserEnergyMonitor.Domain;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class DomainBehaviorTests
    {
        [Fact]
        public void RollingStationarityDetector_EntersAndExitsStationaryState_WhenMetricCrossesThresholds()
        {
            RollingStationarityDetector detector = new RollingStationarityDetector();
            detector.Configure(
                new SessionSettings
                {
                    RollingWindowSize = 2,
                    EnterThresholdPercent = 1d,
                    ExitThresholdPercent = 5d,
                    SessionName = "Detector test"
                },
                "BeamGage",
                "Ophir");

            DateTime t0 = new DateTime(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);

            StationarityUpdate first = detector.Evaluate(CreateSample("BeamGage", 1L, t0, 100d));
            detector.Evaluate(CreateSample("Ophir", 1L, t0.AddMilliseconds(1), 200d));
            detector.Evaluate(CreateSample("BeamGage", 2L, t0.AddMilliseconds(10), 100d));
            StationarityUpdate second = detector.Evaluate(CreateSample("Ophir", 2L, t0.AddMilliseconds(11), 200d));
            detector.Evaluate(CreateSample("BeamGage", 3L, t0.AddMilliseconds(20), 100d));
            StationarityUpdate third = detector.Evaluate(CreateSample("Ophir", 3L, t0.AddMilliseconds(21), 200d));
            StationarityUpdate fourth = detector.Evaluate(CreateSample("BeamGage", 4L, t0.AddMilliseconds(30), 120d));
            detector.Evaluate(CreateSample("Ophir", 4L, t0.AddMilliseconds(31), 240d));

            Assert.False(first.HasEnoughData);
            Assert.True(second.HasEnoughData);
            Assert.False(second.EnteredStationaryState);

            Assert.True(third.EnteredStationaryState);
            Assert.True(third.IsStationary);
            Assert.True(third.IsFirstSourceStationary);
            Assert.True(third.IsSecondSourceStationary);
            Assert.Equal(0d, third.StabilityMetric, 6);
            Assert.Equal(0d, third.FirstStabilityMetric, 6);
            Assert.Equal(0d, third.SecondStabilityMetric, 6);

            Assert.True(fourth.ExitedStationaryState);
            Assert.False(fourth.IsStationary);
            Assert.True(fourth.StabilityMetric > 5d);
        }

        [Fact]
        public void RollingStationarityDetector_TracksEachSourceStabilizingIndependently()
        {
            RollingStationarityDetector detector = new RollingStationarityDetector();
            detector.Configure(
                new SessionSettings
                {
                    RollingWindowSize = 2,
                    EnterThresholdPercent = 1d,
                    ExitThresholdPercent = 5d,
                    SessionName = "Independent warm-up test"
                },
                "BeamGage",
                "Ophir");

            DateTime t0 = new DateTime(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);

            detector.Evaluate(CreateSample("BeamGage", 1L, t0, 100d));
            detector.Evaluate(CreateSample("BeamGage", 2L, t0.AddMilliseconds(10), 100d));
            StationarityUpdate third = detector.Evaluate(CreateSample("BeamGage", 3L, t0.AddMilliseconds(20), 100d));

            detector.Evaluate(CreateSample("Ophir", 1L, t0.AddMilliseconds(1), 100d));
            StationarityUpdate fourth = detector.Evaluate(CreateSample("Ophir", 2L, t0.AddMilliseconds(11), 200d));
            detector.Evaluate(CreateSample("Ophir", 3L, t0.AddMilliseconds(21), 300d));
            detector.Evaluate(CreateSample("Ophir", 4L, t0.AddMilliseconds(31), 300d));
            StationarityUpdate fifth = detector.Evaluate(CreateSample("Ophir", 5L, t0.AddMilliseconds(41), 300d));

            Assert.True(third.FirstSourceEnteredStationaryState);
            Assert.True(third.IsFirstSourceStationary);
            Assert.False(third.IsSecondSourceStationary);
            Assert.False(third.IsStationary);
            Assert.False(third.EnteredStationaryState);
            Assert.Equal(0d, third.FirstStabilityMetric, 6);
            Assert.Equal(0d, third.SecondStabilityMetric, 6);

            Assert.False(fourth.IsStationary);
            Assert.True(fourth.IsFirstSourceStationary);
            Assert.False(fourth.IsSecondSourceStationary);

            Assert.True(fifth.SecondSourceEnteredStationaryState);
            Assert.True(fifth.IsFirstSourceStationary);
            Assert.True(fifth.IsSecondSourceStationary);
            Assert.True(fifth.EnteredStationaryState);
            Assert.True(fifth.IsStationary);
            Assert.Equal(fifth.SecondStabilityMetric, fifth.StabilityMetric, 6);
        }

        private static MeasurementSample CreateSample(string sourceId, long sequenceNumber, DateTime timestampUtc, double energy)
        {
            return new MeasurementSample
            {
                SourceId = sourceId,
                SequenceNumber = sequenceNumber,
                TimestampUtc = timestampUtc,
                MonotonicTicks = timestampUtc.Ticks,
                Energy = energy
            };
        }
    }
}
