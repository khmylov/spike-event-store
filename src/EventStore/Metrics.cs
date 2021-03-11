using App.Metrics;

namespace EventStore
{
    internal static class Metrics
    {
        public static IMeasureMetrics Instance = default!;
    }
}
