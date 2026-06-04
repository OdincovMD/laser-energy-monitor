using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using LaserEnergyMonitor.Domain;

namespace LaserEnergyMonitor.Infrastructure.Excel
{
    public sealed class AsyncMeasurementExporter : IMeasurementExporter
    {
        private readonly IMeasurementExporter _inner;
        private readonly object _gate = new object();
        private BlockingCollection<Action> _queue;
        private Task _worker;
        private Exception _workerException;
        private bool _completed;
        private bool _disposed;

        public AsyncMeasurementExporter(IMeasurementExporter inner)
        {
            _inner = inner ?? throw new ArgumentNullException("inner");
        }

        public void StartSession(SessionMetadata metadata, SessionSettings settings)
        {
            ThrowIfDisposed();

            lock (_gate)
            {
                if (_queue != null)
                {
                    throw new InvalidOperationException("Exporter session is already active.");
                }

                _workerException = null;
                _completed = false;
                _inner.StartSession(metadata, settings);
                _queue = new BlockingCollection<Action>(new ConcurrentQueue<Action>());
                BlockingCollection<Action> workerQueue = _queue;
                _worker = Task.Factory.StartNew(
                    delegate { ProcessQueue(workerQueue); },
                    TaskCreationOptions.LongRunning);
            }
        }

        public void WriteMeasurement(SynchronizedMeasurementPair pair, StationarityUpdate update)
        {
            Enqueue(delegate { _inner.WriteMeasurement(pair, update); });
        }

        public void WriteEvent(SessionEvent sessionEvent)
        {
            Enqueue(delegate { _inner.WriteEvent(sessionEvent); });
        }

        public void WriteStationarySegment(StationarySegmentResult segment)
        {
            Enqueue(delegate { _inner.WriteStationarySegment(segment); });
        }

        public void Complete(SessionSummary summary)
        {
            CompleteQueuedWrites();
            _inner.Complete(summary);
        }

        public void Abort(SessionSummary summary, string reason)
        {
            CompleteQueuedWrites();
            _inner.Abort(summary, reason);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                CompleteQueuedWrites();
            }
            catch
            {
            }

            _inner.Dispose();
        }

        private void Enqueue(Action action)
        {
            ThrowIfDisposed();
            ThrowWorkerExceptionIfAny();

            BlockingCollection<Action> queue = _queue;
            if (queue == null)
            {
                return;
            }

            try
            {
                queue.Add(action);
            }
            catch (InvalidOperationException)
            {
                ThrowWorkerExceptionIfAny();
            }
        }

        private void CompleteQueuedWrites()
        {
            BlockingCollection<Action> queue;
            Task worker;

            lock (_gate)
            {
                if (_completed)
                {
                    ThrowWorkerExceptionIfAny();
                    return;
                }

                _completed = true;
                queue = _queue;
                worker = _worker;
                _queue = null;
                _worker = null;
            }

            if (queue != null)
            {
                queue.CompleteAdding();
            }

            if (worker != null)
            {
                try
                {
                    worker.Wait();
                }
                catch (AggregateException)
                {
                }
            }

            if (queue != null)
            {
                queue.Dispose();
            }

            ThrowWorkerExceptionIfAny();
        }

        private void ProcessQueue(BlockingCollection<Action> queue)
        {
            try
            {
                foreach (Action action in queue.GetConsumingEnumerable())
                {
                    action();
                }
            }
            catch (Exception ex)
            {
                _workerException = ex;
            }
        }

        private void ThrowWorkerExceptionIfAny()
        {
            Exception exception = _workerException;
            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
