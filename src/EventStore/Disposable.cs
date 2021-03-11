using System;
using System.Threading;

namespace EventStore
{
    internal static class Disposable
    {
        public static IDisposable Create(Action action)
        {
            return new AnonymousDisposable(action);
        }

        private sealed class AnonymousDisposable : IDisposable
        {
            private int _disposed;
            private readonly Action _action;

            public AnonymousDisposable(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _action();
                }
            }
        }
    }
}
