using System;
using System.Linq;
using System.Threading;
using Serilog;

namespace EventStore.Workflow
{
    internal sealed class Application : IDisposable
    {
        private static readonly ILogger _log = Log.ForContext<Application>();

        private readonly DbStore _store;
        private readonly ApplicationConfig _config;
        private readonly int _id = InstanceCounter.GetNextId("Application");
        private readonly Guid _applicationGuid = Guid.NewGuid();
        private readonly LifecycleState _lifecycle = new();

        public Application(DbStore store, ApplicationConfig config)
        {
            _store = store;
            _config = config;
        }

        public void Start(CancellationToken cancellationToken)
        {
            if (!_lifecycle.Start())
            {
                return;
            }

            _log.Debug(
                "Application {id} starting with {producerCount} producer(s) and {consumerCount} consumer(s)...",
                _id, _config.Producers.Count, _config.Consumers.Count);

            var consumers = _config.Consumers.Select(cfg => new Consumer(_store, _applicationGuid, cfg)).ToList();
            var producers = _config.Producers.Select(cfg =>
            {
                var producer = new Producer(_store, _applicationGuid, cfg);
                producer.EventProduced += ev =>
                {
                    foreach (var consumer in consumers)
                    {
                        consumer.NotifyEventProduced(ev);
                    }
                };
                return producer;
            }).ToList();

            foreach (var consumer in consumers)
            {
                _lifecycle.AddChild(() =>
                {
                    consumer.StartConsuming(cancellationToken);
                    return consumer;
                }, cancellationToken);
            }

            foreach (var producer in producers)
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
