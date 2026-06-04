using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Logging
{
    public sealed class AsyncApplicationLogger : IApplicationLogger, IDisposable
    {
        private readonly IApplicationLogger _inner;
        private readonly BlockingCollection<Action> _queue;
        private readonly Task _worker;
        private volatile bool _disposed;

        public AsyncApplicationLogger(IApplicationLogger inner)
        {
            _inner = inner ?? throw new ArgumentNullException("inner");
            _queue = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
            _worker = Task.Factory.StartNew(ProcessQueue, TaskCreationOptions.LongRunning);
        }

        public void Info(string message)
        {
            Enqueue(delegate { _inner.Info(message); });
        }

        public void Warning(string message)
        {
            Enqueue(delegate { _inner.Warning(message); });
        }

        public void Error(string message)
        {
            Enqueue(delegate { _inner.Error(message); });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();

            try
            {
                _worker.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _queue.Dispose();
        }

        private void Enqueue(Action action)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _queue.Add(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ProcessQueue()
        {
            foreach (Action action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch
                {
                }
            }
        }
    }
}
