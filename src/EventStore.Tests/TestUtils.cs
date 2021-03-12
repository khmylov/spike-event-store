using System.Linq;
using App.Metrics;
using App.Metrics.Histogram;

namespace EventStore.Tests
{
    internal static class TestUtils
    {
        public static HistogramValue GetCreateConsumeLatency(bool sameApp)
        {
            var histogram = Metrics.Instance.Snapshot
                .Get().Contexts
                .SelectMany(x => x.Histograms)
                .FirstOrDefault(x =>
                    x.MultidimensionalName == "create_consume_latency" &&
                    x.Tags.ToDictionary().TryGetValue("same_app", out var isSameApp)
                    && isSameApp == sameApp.ToString());

            return histogram?.Value;
        }
    }
}
