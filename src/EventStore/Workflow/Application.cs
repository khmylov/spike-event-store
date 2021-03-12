using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;

namespace EventStore.Workflow
{
    internal sealed class Application : IDisposable
    {
        private static readonly ILogger _log = Log.ForContext<Application>();

        private readonly ApplicationConfig _config;
        private readonly int _id = InstanceCounter.GetNextId("Application");
        private readonly Guid _applicationGuid = Guid.NewGuid();
        private readonly LifecycleState _lifecycle = new();

        public Application(DbStore store, ApplicationConfig config)
        {
            _config = config;

            Consumers = config.Consumers.Select(cfg => new Consumer(store, _applicationGuid, cfg)).ToList();
            Producers = config.Producers.Select(cfg =>
            {
                var producer = new Producer(store, _applicationGuid, cfg);
                producer.EventProduced += ev =>
                {
                    foreach (var consumer in Consumers)
                    {
                        consumer.NotifyEventProduced(ev);
                    }
                };
                return producer;
            }).ToList();
        }

        public IReadOnlyList<Consumer> Consumers { get; }

        public IReadOnlyList<Producer> Producers { get; }

        public void Start(CancellationToken cancellationToken)
        {
            if (!_lifecycle.Start())
            {
                return;
            }

            _log.Debug(
                "Application {id} starting with {producerCount} producer(s) and {consumerCount} consumer(s)...",
                _id, _config.Producers.Count, _config.Consumers.Count);

            foreach (var consumer in Consumers)
            {
                _lifecycle.AddChild(() =>
                {
                    consumer.StartConsuming(cancellationToken);
                    return consumer;
                }, cancellationToken);
            }

            foreach (var producer in Producers)
            {
                _lifecycle.AddChild(() =>
                {
                    producer.StartPublishingContinuously(cancellationToken);
                    return producer;
                }, cancellationToken);
            }
        }

        public void Dispose()
        {
            _lifecycle.Stop();
        }
    }
}
