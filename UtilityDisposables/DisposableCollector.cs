using System;
using System.Collections.Generic;

namespace UtilityDisposables
{
    /// <summary>
    /// A class for easily collecting IDisposables together to be disposed all at once at a later time.
    /// This class is thread-safe.
    /// </summary>
    public class DisposableCollector : ManagedDisposable, IDisposableCollector
    {
        private List<IDisposable> _disposables = new List<IDisposable>();

        private readonly object _lock = new object();

        public DisposableCollector(IEnumerable<IDisposable> disposables)
        {
            Disposes(disposables);
        }

        public DisposableCollector(params IDisposable[] disposables)
        {
            Disposes(disposables);
        }

        public void Disposes(IEnumerable<IDisposable> disposables)
        {
            lock (_lock)
            {
                if (IsDisposed)
                    throw new InvalidOperationException("Cannot modify an already-disposed IDisposable");
                foreach (var disposable in disposables)
                {
                    _disposables.Add(disposable);
                }
            }
        }

        public void Disposes(params IDisposable[] disposables)
        {
            lock (_lock)
            {
                if (IsDisposed)
                    throw new InvalidOperationException("Cannot modify an already-disposed IDisposable");
                foreach (var disposable in disposables)
                {
                    _disposables.Add(disposable);
                }
            }
        }

        public T Disposes<T>(T disposable)
            where T : IDisposable
        {
            lock (_lock)
            {
                if (IsDisposed)
                    throw new InvalidOperationException("Cannot modify an already-disposed IDisposable");
                _disposables.Add(disposable);
                return disposable;
            }
        }

        public void Disposes<T>(params T[] disposables)
            where T : IDisposable
        {
            lock (_lock)
            {
                if (IsDisposed)
                    throw new InvalidOperationException("Cannot modify an already-disposed IDisposable");
                foreach (var disposable in disposables)
                {
                    _disposables.Add(disposable);
                }
            }
        }
        public void TryDisposes(IEnumerable<IDisposable> disposables)
        {
            lock (_lock)
            {
                if (IsDisposed) return;
                foreach (var disposable in disposables)
                {
                    _disposables.Add(disposable);
                }
            }
        }

        public void TryDisposes(params IDisposable[] disposables)
        {
            lock (_lock)
            {
                if (IsDisposed) return;
                foreach (var disposable in disposables)
                {
                    _disposables.Add(disposable);
                }
            }
        }

        public T TryDisposes<T>(T disposable)
            where T : IDisposable
        {
            lock (_lock)
            {
                if (IsDisposed) return disposable;
                _disposables.Add(disposable);
                return disposable;
            }
        }

        public void TryDisposes<T>(params T[] disposables)
            where T : IDisposable
        {
            lock (_lock)
            {
                if (IsDisposed) return;
                foreach (var disposable in disposables)
                {
                    _disposables.Add(disposable);
                }
            }
        }

        protected override void DisposeManagedResources()
        {
            lock (_lock)
            {
                if (IsDisposed) return;
                while (_disposables.Count > 0)
                {
                    var first = _disposables[0];
                    _disposables.Remove(first);
                    first.Dispose();
                }
                _disposables = null;
            }
        }

        public bool IsDisposed => _disposables == null;
    }
}
