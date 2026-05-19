using System;

namespace LaserEnergyMonitor.Domain
{
    public sealed class RollingStationarityDetector : IStationarityDetector
    {
        private readonly RollingWindow _firstWindow = new RollingWindow();
        private readonly RollingWindow _secondWindow = new RollingWindow();
        private bool _isStationary;
        private bool _hasPreviousAverage;
        private double _previousFirstAverage;
        private double _previousSecondAverage;
        private SessionSettings _settings;

        public void Configure(SessionSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            _settings = settings;
            _firstWindow.Configure(settings.RollingWindowSize);
            _secondWindow.Configure(settings.RollingWindowSize);
            ResetInternalState();
        }

        public StationarityUpdate Evaluate(SynchronizedMeasurementPair pair)
        {
            if (_settings == null)
            {
                throw new InvalidOperationException("Detector must be configured before evaluation.");
            }

            _firstWindow.Add(pair.FirstSample.Energy);
            _secondWindow.Add(pair.SecondSample.Energy);

            double firstAverage = _firstWindow.Average();
            double secondAverage = _secondWindow.Average();

            StationarityUpdate update = new StationarityUpdate
            {
                RollingAverageFirst = firstAverage,
                RollingAverageSecond = secondAverage,
                HasEnoughData = _firstWindow.IsFull && _secondWindow.IsFull,
                IsStationary = _isStationary
            };

            if (!update.HasEnoughData)
            {
                return update;
            }

            if (!_hasPreviousAverage)
            {
                _previousFirstAverage = firstAverage;
                _previousSecondAverage = secondAverage;
                _hasPreviousAverage = true;
                return update;
            }

            double metric = Math.Max(
                RelativePercentChange(_previousFirstAverage, firstAverage),
                RelativePercentChange(_previousSecondAverage, secondAverage));

            update.StabilityMetric = metric;

            if (!_isStationary && metric <= _settings.EnterThresholdPercent)
            {
                _isStationary = true;
                update.IsStationary = true;
                update.EnteredStationaryState = true;
            }
            else if (_isStationary && metric > _settings.ExitThresholdPercent)
            {
                _isStationary = false;
                update.IsStationary = false;
                update.ExitedStationaryState = true;
            }
            else
            {
                update.IsStationary = _isStationary;
            }

            _previousFirstAverage = firstAverage;
            _previousSecondAverage = secondAverage;
            return update;
        }

        public void Reset()
        {
            _firstWindow.Reset();
            _secondWindow.Reset();
            ResetInternalState();
        }

        private void ResetInternalState()
        {
            _isStationary = false;
            _hasPreviousAverage = false;
            _previousFirstAverage = 0d;
            _previousSecondAverage = 0d;
        }

        private static double RelativePercentChange(double previous, double current)
        {
            double denominator = Math.Abs(previous);

            if (denominator < 0.0000001d)
            {
                denominator = 0.0000001d;
            }

            return (Math.Abs(current - previous) / denominator) * 100d;
        }
    }
}
