using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Tests
{
    internal class CounterWaiter
    {
        private readonly int _expectedCount;
        private readonly TaskCompletionSource _taskCompletionSource;
        private int _actualCount;

        public CounterWaiter(int expectedCount)
        {
            _expectedCount = expectedCount;
            _taskCompletionSource = new TaskCompletionSource();
        }

        public Task Task => _taskCompletionSource.Task;

        public int ActualCount => _actualCount;

        public void Track()
        {
            if (Interlocked.Increment(ref _actualCount) == _expectedCount)
            {
                _taskCompletionSource.SetResult();
            }
        }
    }
}
