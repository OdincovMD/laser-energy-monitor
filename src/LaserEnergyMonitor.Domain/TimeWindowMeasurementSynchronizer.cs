using System;
using System.Collections.Generic;

namespace LaserEnergyMonitor.Domain
{
    public sealed class TimeWindowMeasurementSynchronizer : IMeasurementSynchronizer
    {
        private readonly List<MeasurementSample> _firstSamples = new List<MeasurementSample>();
        private readonly List<MeasurementSample> _secondSamples = new List<MeasurementSample>();
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

            List<SynchronizedMeasurementPair> pairs = new List<SynchronizedMeasurementPair>();
            List<DesynchronizationEventArgs> desynchronizedSamples = new List<DesynchronizationEventArgs>();

            lock (_gate)
            {
                Enqueue(sample);
                TryMatch(pairs, desynchronizedSamples);
            }

            for (int i = 0; i < pairs.Count; i++)
            {
                PairReady?.Invoke(this, new SynchronizedMeasurementPairEventArgs(pairs[i]));
            }

            for (int i = 0; i < desynchronizedSamples.Count; i++)
            {
                Desynchronized?.Invoke(this, desynchronizedSamples[i]);
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
                InsertSorted(_firstSamples, sample);
                return;
            }

            if (string.Equals(sample.SourceId, _secondSourceId, StringComparison.OrdinalIgnoreCase))
            {
                InsertSorted(_secondSamples, sample);
                return;
            }

            throw new InvalidOperationException("Unknown source id: " + sample.SourceId);
        }

        private void TryMatch(
            List<SynchronizedMeasurementPair> pairs,
            List<DesynchronizationEventArgs> desynchronizedSamples)
        {
            while (_firstSamples.Count > 0 && _secondSamples.Count > 0)
            {
                List<MeasurementSample> oldestList;
                List<MeasurementSample> otherList;
                bool oldestIsFirst = CompareSamples(_firstSamples[0], _secondSamples[0]) <= 0;
                if (oldestIsFirst)
                {
                    oldestList = _firstSamples;
                    otherList = _secondSamples;
                }
                else
                {
                    oldestList = _secondSamples;
                    otherList = _firstSamples;
                }

                MeasurementSample oldest = oldestList[0];
                int matchIndex = FindClosestMatchIndex(oldest, otherList);
                if (matchIndex >= 0)
                {
                    MeasurementSample match = otherList[matchIndex];
                    oldestList.RemoveAt(0);
                    otherList.RemoveAt(matchIndex);
                    _pairCounter += 1;

                    MeasurementSample first = oldestIsFirst ? oldest : match;
                    MeasurementSample second = oldestIsFirst ? match : oldest;
                    pairs.Add(
                        new SynchronizedMeasurementPair
                        {
                            PairId = _pairCounter,
                            FirstSample = first,
                            SecondSample = second,
                            Delta = (first.TimestampUtc - second.TimestampUtc).Duration()
                        });
                    continue;
                }

                if (IsStale(oldest, otherList))
                {
                    oldestList.RemoveAt(0);
                    desynchronizedSamples.Add(
                        new DesynchronizationEventArgs(
                            oldest,
                            "No matching sample was found before the synchronization window expired."));
                    continue;
                }

                break;
            }
        }

        private int FindClosestMatchIndex(MeasurementSample sample, List<MeasurementSample> candidates)
        {
            int bestIndex = -1;
            TimeSpan bestDelta = TimeSpan.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                TimeSpan delta = (sample.TimestampUtc - candidates[i].TimestampUtc).Duration();
                if (delta > _maxDelta)
                {
                    continue;
                }

                if (bestIndex < 0 || delta < bestDelta)
                {
                    bestIndex = i;
                    bestDelta = delta;
                }
            }

            return bestIndex;
        }

        private bool IsStale(MeasurementSample sample, List<MeasurementSample> candidates)
        {
            if (sample == null || candidates == null || candidates.Count == 0)
            {
                return false;
            }

            MeasurementSample newestCandidate = candidates[candidates.Count - 1];
            return newestCandidate.TimestampUtc - sample.TimestampUtc > _maxDelta;
        }

        private static void InsertSorted(List<MeasurementSample> samples, MeasurementSample sample)
        {
            int insertIndex = samples.Count;
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                if (CompareSamples(samples[i], sample) <= 0)
                {
                    insertIndex = i + 1;
                    break;
                }

                insertIndex = i;
            }

            samples.Insert(insertIndex, sample);
        }

        private static int CompareSamples(MeasurementSample left, MeasurementSample right)
        {
            if (left == null && right == null)
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int timestampCompare = left.TimestampUtc.CompareTo(right.TimestampUtc);
            if (timestampCompare != 0)
            {
                return timestampCompare;
            }

            int monotonicCompare = left.MonotonicTicks.CompareTo(right.MonotonicTicks);
            if (monotonicCompare != 0)
            {
                return monotonicCompare;
            }

            return left.SequenceNumber.CompareTo(right.SequenceNumber);
        }
    }
}
