using System;

namespace LaserEnergyMonitor.Domain
{
    public sealed class RollingStationarityDetector : IStationarityDetector
    {
        private readonly RollingWindow _firstWindow = new RollingWindow();
        private readonly RollingWindow _secondWindow = new RollingWindow();
        private bool _isStationary;
        private bool _isFirstSourceStationary;
        private bool _isSecondSourceStationary;
        private bool _hasPreviousFirstAverage;
        private bool _hasPreviousSecondAverage;
        private double _previousFirstAverage;
        private double _previousSecondAverage;
        private SessionSettings _settings;
        private string _firstSourceId;
        private string _secondSourceId;

        public void Configure(SessionSettings settings, string firstSourceId, string secondSourceId)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (string.IsNullOrWhiteSpace(firstSourceId))
            {
                throw new ArgumentException("First source id must be provided.", "firstSourceId");
            }

            if (string.IsNullOrWhiteSpace(secondSourceId))
            {
                throw new ArgumentException("Second source id must be provided.", "secondSourceId");
            }

            _settings = settings;
            _firstSourceId = firstSourceId;
            _secondSourceId = secondSourceId;
            _firstWindow.Configure(settings.RollingWindowSize);
            _secondWindow.Configure(settings.RollingWindowSize);
            ResetInternalState();
        }

        public StationarityUpdate Evaluate(MeasurementSample sample)
        {
            if (_settings == null)
            {
                throw new InvalidOperationException("Detector must be configured before evaluation.");
            }

            if (sample == null)
            {
                throw new ArgumentNullException("sample");
            }

            bool isFirstSource = string.Equals(sample.SourceId, _firstSourceId, StringComparison.OrdinalIgnoreCase);
            bool isSecondSource = string.Equals(sample.SourceId, _secondSourceId, StringComparison.OrdinalIgnoreCase);
            if (!isFirstSource && !isSecondSource)
            {
                throw new InvalidOperationException("Unknown source id: " + sample.SourceId);
            }

            if (isFirstSource)
            {
                _firstWindow.Add(sample.Energy);
            }
            else
            {
                _secondWindow.Add(sample.Energy);
            }

            double firstAverage = _firstWindow.Average();
            double secondAverage = _secondWindow.Average();

            StationarityUpdate update = new StationarityUpdate
            {
                RollingAverageFirst = firstAverage,
                RollingAverageSecond = secondAverage,
                HasEnoughData = _firstWindow.IsFull && _secondWindow.IsFull,
                IsFirstSourceStationary = _isFirstSourceStationary,
                IsSecondSourceStationary = _isSecondSourceStationary,
                IsStationary = _isStationary
            };

            bool wasStationary = _isStationary;
            bool firstEntered = false;
            bool firstExited = false;
            bool secondEntered = false;
            bool secondExited = false;

            if (isFirstSource && _firstWindow.IsFull)
            {
                if (!_hasPreviousFirstAverage)
                {
                    _previousFirstAverage = firstAverage;
                    _hasPreviousFirstAverage = true;
                }
                else
                {
                    double firstMetric = RelativePercentChange(_previousFirstAverage, firstAverage);
                    update.FirstStabilityMetric = firstMetric;
                    UpdateSourceStationarity(
                        firstMetric,
                        ref _isFirstSourceStationary,
                        out firstEntered,
                        out firstExited);
                    _previousFirstAverage = firstAverage;
                }
            }

            if (isSecondSource && _secondWindow.IsFull)
            {
                if (!_hasPreviousSecondAverage)
                {
                    _previousSecondAverage = secondAverage;
                    _hasPreviousSecondAverage = true;
                }
                else
                {
                    double secondMetric = RelativePercentChange(_previousSecondAverage, secondAverage);
                    update.SecondStabilityMetric = secondMetric;
                    UpdateSourceStationarity(
                        secondMetric,
                        ref _isSecondSourceStationary,
                        out secondEntered,
                        out secondExited);
                    _previousSecondAverage = secondAverage;
                }
            }

            update.FirstSourceEnteredStationaryState = firstEntered;
            update.SecondSourceEnteredStationaryState = secondEntered;
            update.FirstSourceExitedStationaryState = firstExited;
            update.SecondSourceExitedStationaryState = secondExited;
            update.IsFirstSourceStationary = _isFirstSourceStationary;
            update.IsSecondSourceStationary = _isSecondSourceStationary;
            update.StabilityMetric = Math.Max(update.FirstStabilityMetric, update.SecondStabilityMetric);

            _isStationary = _isFirstSourceStationary && _isSecondSourceStationary;

            if (!wasStationary && _isStationary)
            {
                update.IsStationary = true;
                update.EnteredStationaryState = true;
            }
            else if (wasStationary && !_isStationary)
            {
                update.IsStationary = false;
                update.ExitedStationaryState = true;
            }
            else
            {
                update.IsStationary = _isStationary;
            }

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
            _isFirstSourceStationary = false;
            _isSecondSourceStationary = false;
            _hasPreviousFirstAverage = false;
            _hasPreviousSecondAverage = false;
            _previousFirstAverage = 0d;
            _previousSecondAverage = 0d;
        }

        private void UpdateSourceStationarity(
            double metric,
            ref bool isSourceStationary,
            out bool entered,
            out bool exited)
        {
            entered = false;
            exited = false;

            if (!isSourceStationary && metric <= _settings.EnterThresholdPercent)
            {
                isSourceStationary = true;
                entered = true;
                return;
            }

            if (isSourceStationary && metric > _settings.ExitThresholdPercent)
            {
                isSourceStationary = false;
                exited = true;
            }
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
