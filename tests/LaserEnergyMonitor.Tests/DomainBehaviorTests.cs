using System;
using System.Collections.Generic;
using LaserEnergyMonitor.Domain;
using Xunit;

namespace LaserEnergyMonitor.Tests
{
    public sealed class DomainBehaviorTests
    {
        [Fact]
        public void TimeWindowMeasurementSynchronizer_MatchesClosestSampleAndExpiresStaleSample()
        {
            TimeWindowMeasurementSynchronizer synchronizer = new TimeWindowMeasurementSynchronizer();
            synchronizer.Configure(TimeSpan.FromMilliseconds(10), "BeamGage", "Ophir");

            List<SynchronizedMeasurementPair> pairs = new List<SynchronizedMeasurementPair>();
            List<DesynchronizationEventArgs> desynchronized = new List<DesynchronizationEventArgs>();
            synchronizer.PairReady += delegate(object sender, SynchronizedMeasurementPairEventArgs args)
            {
                pairs.Add(args.Pair);
            };
            synchronizer.Desynchronized += delegate(object sender, DesynchronizationEventArgs args)
            {
                desynchronized.Add(args);
            };

            DateTime t0 = new DateTime(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);

            synchronizer.Push(CreateSample("Ophir", 1L, t0.AddMilliseconds(9), 20d));
            synchronizer.Push(CreateSample("Ophir", 2L, t0.AddMilliseconds(3), 21d));
            synchronizer.Push(CreateSample("BeamGage", 10L, t0, 10d));
            synchronizer.Push(CreateSample("BeamGage", 11L, t0.AddMilliseconds(30), 11d));

            Assert.Single(pairs);
            Assert.Equal(10L, pairs[0].FirstSample.SequenceNumber);
            Assert.Equal(2L, pairs[0].SecondSample.SequenceNumber);
            Assert.Equal(TimeSpan.FromMilliseconds(3), pairs[0].Delta);

            Assert.Single(desynchronized);
            Assert.Equal(1L, desynchronized[0].Sample.SequenceNumber);
            Assert.Contains("window expired", desynchronized[0].Reason);
            Assert.Contains("Stale source: Ophir", desynchronized[0].Reason);
            Assert.Contains("newest other source: BeamGage", desynchronized[0].Reason);
        }

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
                    SynchronizationDelta = TimeSpan.FromMilliseconds(10),
                    SessionName = "Detector test"
                });

            DateTime t0 = new DateTime(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);

            StationarityUpdate first = detector.Evaluate(CreatePair(1L, t0, 100d, 200d));
            StationarityUpdate second = detector.Evaluate(CreatePair(2L, t0.AddMilliseconds(10), 100d, 200d));
            StationarityUpdate third = detector.Evaluate(CreatePair(3L, t0.AddMilliseconds(20), 100d, 200d));
            StationarityUpdate fourth = detector.Evaluate(CreatePair(4L, t0.AddMilliseconds(30), 120d, 240d));

            Assert.False(first.HasEnoughData);
            Assert.True(second.HasEnoughData);
            Assert.False(second.EnteredStationaryState);

            Assert.True(third.EnteredStationaryState);
            Assert.True(third.IsStationary);
            Assert.Equal(0d, third.StabilityMetric, 6);

            Assert.True(fourth.ExitedStationaryState);
            Assert.False(fourth.IsStationary);
            Assert.True(fourth.StabilityMetric > 5d);
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

        private static SynchronizedMeasurementPair CreatePair(long pairId, DateTime timestampUtc, double firstEnergy, double secondEnergy)
        {
            return new SynchronizedMeasurementPair
            {
                PairId = pairId,
                FirstSample = CreateSample("BeamGage", pairId, timestampUtc, firstEnergy),
                SecondSample = CreateSample("Ophir", pairId, timestampUtc.AddMilliseconds(1), secondEnergy),
                Delta = TimeSpan.FromMilliseconds(1)
            };
        }
    }
}
