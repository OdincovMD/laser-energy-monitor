using System;
using System.Collections.Generic;

namespace LaserEnergyMonitor.Domain
{
    public sealed class RollingWindow
    {
        private readonly Queue<double> _values = new Queue<double>();
        private double _sum;

        public int Capacity { get; private set; }

        public int Count
        {
            get { return _values.Count; }
        }

        public bool IsFull
        {
            get { return Count >= Capacity; }
        }

        public void Configure(int capacity)
        {
            if (capacity <= 1)
            {
                throw new ArgumentOutOfRangeException("capacity", "Rolling window size must be greater than 1.");
            }

            Capacity = capacity;
            Reset();
        }

        public void Add(double value)
        {
            if (Capacity <= 0)
            {
                throw new InvalidOperationException("Rolling window must be configured before use.");
            }

            _values.Enqueue(value);
            _sum += value;

            while (_values.Count > Capacity)
            {
                _sum -= _values.Dequeue();
            }
        }

        public double Average()
        {
            if (_values.Count == 0)
            {
                return 0d;
            }

            return _sum / _values.Count;
        }

        public IReadOnlyCollection<double> Snapshot()
        {
            return _values.ToArray();
        }

        public void Reset()
        {
            _values.Clear();
            _sum = 0d;
        }
    }
}
