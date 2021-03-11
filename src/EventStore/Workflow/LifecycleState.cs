using System;
using System.Collections.Generic;
using System.Threading;

namespace EventStore.Workflow
{
    public class LifecycleState
    {
        private volatile bool _started;
        private volatile bool _disposed;

        private readonly object _gate = new();
        private readonly List<IDisposable> _children = new();

        public bool IsDisposed => _disposed;

        public bool Start()
        {
            if (!_started)
                lock (_gate)
                    if (!_started)
                    {
                        _started = true;
                        return true;
                    }

            return false;
        }

        public void AddChild(Func<IDisposable> action, CancellationToken cancellationToken)
        {
            if (!_disposed && !cancellationToken.IsCancellationRequested)
                lock (_gate)
                    if (!_disposed && !cancellationToken.IsCancellationRequested)
                    {
                        var disposable = action();
                        IDisposable? compositeDisposable = null;
                        var cancellationRegistration = cancellationToken.Register(() =>
                        {
                            disposable.Dispose();
                            _children.Remove(compositeDisposable);
                        });

                        compositeDisposable = Disposable.Create(() =>
                        {
                            cancellationRegistration.Dispose();
                            disposable.Dispose();
                        });

                        _children.Add(compositeDisposable);
                    }
        }

        public bool Stop()
        {
            if (!_disposed)
                lock (_gate)
                    if (!_disposed)
                    {
                        foreach (var disposable in _children)
                        {
                            disposable.Dispose();
                        }
                        _children.Clear();

                        _disposed = true;
                        return true;
                    }

            return false;
        }
    }
}
