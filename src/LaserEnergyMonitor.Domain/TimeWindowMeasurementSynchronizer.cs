using System;
using System.Collections.Generic;

namespace LaserEnergyMonitor.Domain
{
    public sealed class TimeWindowMeasurementSynchronizer : IMeasurementSynchronizer
    {
        private readonly Queue<MeasurementSample> _firstSamples = new Queue<MeasurementSample>();
        private readonly Queue<MeasurementSample> _secondSamples = new Queue<MeasurementSample>();
        private readonly object _gate = new object();

        private TimeSpan _maxDelta = TimeSpan.FromMilliseconds(5);
        private string _firstSourceId;
        private string _secondSourceId;
        private long _pairCounter;

        public event EventHandler<SynchronizedMeasurementPairEventArgs> PairReady;
        public event EventHandler<DesynchronizationEventArgs> Desynchronized;

        public void Configure(TimeSpan maxDelta, string firstSourceId, string secondSourceId)
        {
            if (string.IsNullOrWhiteSpace(firstSourceId))
            {
                throw new ArgumentException("First source id must be provided.", "firstSourceId");
            }

            if (string.IsNullOrWhiteSpace(secondSourceId))
            {
                throw new ArgumentException("Second source id must be provided.", "secondSourceId");
            }

            _maxDelta = maxDelta;
            _firstSourceId = firstSourceId;
            _secondSourceId = secondSourceId;
            Reset();
        }

        public void Push(MeasurementSample sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException("sample");
            }

            lock (_gate)
            {
                Enqueue(sample);
                TryMatch();
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _firstSamples.Clear();
                _secondSamples.Clear();
                _pairCounter = 0;
            }
        }

        private void Enqueue(MeasurementSample sample)
        {
            if (string.Equals(sample.SourceId, _firstSourceId, StringComparison.OrdinalIgnoreCase))
            {
                _firstSamples.Enqueue(sample);
                return;
            }

            if (string.Equals(sample.SourceId, _secondSourceId, StringComparison.OrdinalIgnoreCase))
            {
                _secondSamples.Enqueue(sample);
                return;
            }

            throw new InvalidOperationException("Unknown source id: " + sample.SourceId);
        }

        private void TryMatch()
        {
            while (_firstSamples.Count > 0 && _secondSamples.Count > 0)
            {
                MeasurementSample first = _firstSamples.Peek();
                MeasurementSample second = _secondSamples.Peek();
                TimeSpan delta = first.TimestampUtc - second.TimestampUtc;
                TimeSpan absDelta = delta.Duration();

                if (absDelta <= _maxDelta)
                {
                    _firstSamples.Dequeue();
                    _secondSamples.Dequeue();
                    _pairCounter += 1;

                    PairReady?.Invoke(
                        this,
                        new SynchronizedMeasurementPairEventArgs(
                            new SynchronizedMeasurementPair
                            {
                                PairId = _pairCounter,
                                FirstSample = first,
                                SecondSample = second,
                                Delta = absDelta
                            }));

                    continue;
                }

                if (first.TimestampUtc < second.TimestampUtc)
                {
                    _firstSamples.Dequeue();
                    Desynchronized?.Invoke(this, new DesynchronizationEventArgs(first, "No matching sample was found in the synchronization window."));
                }
                else
                {
                    _secondSamples.Dequeue();
                    Desynchronized?.Invoke(this, new DesynchronizationEventArgs(second, "No matching sample was found in the synchronization window."));
                }
            }
        }
    }
}
