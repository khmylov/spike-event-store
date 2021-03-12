using App.Metrics;

namespace EventStore
{
    internal static class Metrics
    {
        public static IMetrics Instance = new MetricsBuilder().Build();
        public static IMeasureMetrics Measure => Instance.Measure;
    }
}
