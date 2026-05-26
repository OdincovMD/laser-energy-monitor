using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LaserEnergyMonitor.Infrastructure.Ophir
{
    internal sealed class StaWorker : IDisposable
    {
        private readonly BlockingCollection<IStaWorkItem> _queue;
        private readonly Thread _thread;
        private bool _disposed;

        public StaWorker(string name)
        {
            _queue = new BlockingCollection<IStaWorkItem>();
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = name;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public void Invoke(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            Invoke<object>(
                delegate
                {
                    action();
                    return null;
                });
        }

        public T Invoke<T>(Func<T> func)
        {
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }

            ThrowIfDisposed();
            if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId)
            {
                return func();
            }

            StaWorkItem<T> workItem = new StaWorkItem<T>(func);
            _queue.Add(workItem);
            workItem.Wait();
            return workItem.GetResult();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(2));
            _queue.Dispose();
        }

        private void Run()
        {
            foreach (IStaWorkItem workItem in _queue.GetConsumingEnumerable())
            {
                workItem.Execute();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private interface IStaWorkItem
        {
            void Execute();
        }

        private sealed class StaWorkItem<T> : IStaWorkItem
        {
            private readonly Func<T> _func;
            private readonly ManualResetEventSlim _completed;
            private T _result;
            private Exception _exception;

            public StaWorkItem(Func<T> func)
            {
                _func = func;
                _completed = new ManualResetEventSlim(false);
            }

            public void Execute()
            {
                try
                {
                    _result = _func();
                }
                catch (Exception ex)
                {
                    _exception = ex;
                }
                finally
                {
                    _completed.Set();
                }
            }

            public void Wait()
            {
                _completed.Wait();
            }

            public T GetResult()
            {
                _completed.Dispose();
                if (_exception != null)
                {
                    throw _exception;
                }

                return _result;
            }
        }
    }
}
