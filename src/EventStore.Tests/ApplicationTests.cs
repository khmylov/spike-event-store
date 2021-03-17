using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Workflow;
using NUnit.Framework;
using Serilog;
using Shouldly;

namespace EventStore.Tests
{
    public class ApplicationTests : DatabaseTest
    {
        [Test]
        public async Task SingleFastProducerSingleConsumerTest()
        {
            const int expectedEventCount = 200;

            var allProduced = new CounterWaiter(expectedEventCount);
            var allConsumed = new CounterWaiter(expectedEventCount);

            using (var application = new Application(
                ServiceConfig.Instance.DbStore,
                new ApplicationConfig(
                    Producers: new[] {new ProducerConfig(TimeSpan.Zero, TimeSpan.FromMilliseconds(1))},
                    Consumers: new[] {new ConsumerConfig(1, TimeSpan.FromMilliseconds(1), TimeSpan.Zero, TimeSpan.Zero)})))
            {
                foreach (var p in application.Producers)
                {
                    p.EventProduced += _ => allProduced.Track();
                }

                foreach (var consumer in application.Consumers)
                {
                    consumer.EventConsumed += _ => allConsumed.Track();
                }

                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                application.Start(cancellationTokenSource.Token);

                await Task.WhenAll(allProduced.Task, allConsumed.Task);
            }

            allProduced.ActualCount.ShouldBeGreaterThanOrEqualTo(expectedEventCount);
            allConsumed.ActualCount.ShouldBeGreaterThanOrEqualTo(expectedEventCount);

            var latency = TestUtils.GetCreateConsumeLatency(true);
            Log.Information("Latency {@latency}", latency);
            latency.Median.ShouldBeLessThanOrEqualTo(100);
        }
    }
}
