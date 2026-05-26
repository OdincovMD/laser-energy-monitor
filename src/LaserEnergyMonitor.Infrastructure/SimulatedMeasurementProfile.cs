using System;

namespace LaserEnergyMonitor.Infrastructure
{
    public sealed class SimulatedMeasurementProfile
    {
        private readonly AnchorPoint[] _anchors;
        private readonly double _noiseAmplitude;
        private readonly double _tailDriftAmplitude;
        private readonly double _tailDriftPeriod;

        public SimulatedMeasurementProfile(
            AnchorPoint[] anchors,
            double noiseAmplitude,
            double tailDriftAmplitude,
            double tailDriftPeriod)
        {
            if (anchors == null || anchors.Length == 0)
            {
                throw new ArgumentException("At least one anchor point is required.", "anchors");
            }

            _anchors = anchors;
            _noiseAmplitude = noiseAmplitude;
            _tailDriftAmplitude = tailDriftAmplitude;
            _tailDriftPeriod = tailDriftPeriod <= 0 ? 1.0d : tailDriftPeriod;
        }

        public double ComputeEnergy(long sequence, Random random)
        {
            if (sequence <= _anchors[0].Sequence)
            {
                return AddNoise(_anchors[0].Energy, random);
            }

            for (int i = 1; i < _anchors.Length; i++)
            {
                if (sequence <= _anchors[i].Sequence)
                {
                    AnchorPoint left = _anchors[i - 1];
                    AnchorPoint right = _anchors[i];
                    double progress = (double)(sequence - left.Sequence) / (double)(right.Sequence - left.Sequence);
                    double energy = left.Energy + ((right.Energy - left.Energy) * progress);
                    return AddNoise(energy, random);
                }
            }

            AnchorPoint last = _anchors[_anchors.Length - 1];
            double drift = _tailDriftAmplitude * Math.Sin(sequence / _tailDriftPeriod);
            return AddNoise(last.Energy + drift, random);
        }

        public static SimulatedMeasurementProfile CreateBeamGageCustomerLike()
        {
            return new SimulatedMeasurementProfile(
                new[]
                {
                    new AnchorPoint(1, 2774180d),
                    new AnchorPoint(6, 2774268d),
                    new AnchorPoint(12, 2774196d),
                    new AnchorPoint(21, 2774150d),
                    new AnchorPoint(29, 2774154d),
                    new AnchorPoint(34, 2774175d),
                    new AnchorPoint(39, 2774197d),
                    new AnchorPoint(45, 2774170d),
                    new AnchorPoint(71, 2774120d)
                },
                520d,
                180d,
                9d);
        }

        public static SimulatedMeasurementProfile CreateOphirCustomerLike()
        {
            return new SimulatedMeasurementProfile(
                new[]
                {
                    new AnchorPoint(1, 10.2119d),
                    new AnchorPoint(5, 10.4609d),
                    new AnchorPoint(12, 10.5248d),
                    new AnchorPoint(21, 10.6176d),
                    new AnchorPoint(29, 10.6991d),
                    new AnchorPoint(34, 10.7001d),
                    new AnchorPoint(35, 11.4133d),
                    new AnchorPoint(39, 11.4192d),
                    new AnchorPoint(45, 10.7580d),
                    new AnchorPoint(71, 10.7510d)
                },
                0.012d,
                0.018d,
                11d);
        }

        public static SimulatedMeasurementProfile CreateLegacy(double baseEnergy)
        {
            return new SimulatedMeasurementProfile(
                new[]
                {
                    new AnchorPoint(1, baseEnergy + 0.01d),
                    new AnchorPoint(50, baseEnergy + 0.50d),
                    new AnchorPoint(180, baseEnergy + 0.50d),
                    new AnchorPoint(230, baseEnergy + 1.20d),
                    new AnchorPoint(260, baseEnergy + 0.55d)
                },
                0.04d,
                0.02d,
                12d);
        }

        private double AddNoise(double energy, Random random)
        {
            double noise = (random.NextDouble() - 0.5d) * _noiseAmplitude;
            return energy + noise;
        }

        public struct AnchorPoint
        {
            public AnchorPoint(long sequence, double energy)
            {
                Sequence = sequence;
                Energy = energy;
            }

            public long Sequence { get; private set; }

            public double Energy { get; private set; }
        }
    }
}
