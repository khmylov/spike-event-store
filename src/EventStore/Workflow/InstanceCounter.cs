using System.Collections.Concurrent;

namespace EventStore.Workflow
{
    public static class InstanceCounter
    {
        private static readonly ConcurrentDictionary<string, int> _counters = new();

        public static int GetNextId(string componentName)
        {
            return _counters.AddOrUpdate(componentName, _ => 1, (_, count) => count + 1);
        }
    }
}
