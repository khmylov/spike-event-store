using System;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Histogram;
using EventStore.Model;
using Serilog;

namespace EventStore.Workflow
{
    internal sealed class Consumer : IDisposable
    {
        private static readonly ILogger _log = Log.ForContext<Consumer>();

        private readonly int _id = InstanceCounter.GetNextId("Consumer");
        private readonly LifecycleState _lifecycle = new();

        private readonly DbStore _store;
        private readonly Guid _applicationId;
        private readonly ConsumerConfig _config;

        private int? _lastConsumedDatabaseId;

        public Consumer(DbStore store, Guid applicationId, ConsumerConfig config)
        {
            _store = store;
            _applicationId = applicationId;
            _config = config;
        }

        public void StartConsuming(CancellationToken cancellationToken)
        {
            if (!_lifecycle.Start())
            {
                return;
            }

            _log.Debug("Consumer {consumerId} starting new event monitoring...", _id);

            async void Run()
            {
                while (!cancellationToken.IsCancellationRequested && !_lifecycle.IsDisposed)
                {
                    var consumed = false;
                    try
                    {
                        consumed = await ConsumeOneAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error while consuming");
                    }

                    await Task.Delay(consumed ? _config.PickNextInterval : _config.PollingInterval, cancellationToken);
                }
            }

            Run();
        }

        private Task<bool> ConsumeOneAsync(CancellationToken cancellationToken)
        {
            return _store.ConsumeOneAsync(Handle, cancellationToken);
        }

        private Task Handle(InputEvent @event)
        {
            var lastConsumed = _lastConsumedDatabaseId;
            var databaseId = @event.DatabaseId;
            if (databaseId <= lastConsumed)
            {
                _log.Error("Last consumed ID {lastConsumed} > current ID {current}", lastConsumed, databaseId);
                Metrics.Instance.Counter.Increment(new CounterOptions {Name = "invalid_consume_order_count"});
            }

            _lastConsumedDatabaseId = databaseId;

            var sameApp = @event.ApplicationId == _applicationId;

            Metrics.Instance.Histogram.Update(
                new HistogramOptions {Name = "create_consume_latency", Tags = new MetricTags("same_app", sameApp.ToString())},
                (long) (DateTimeOffset.Now - @event.CreatedAt).TotalMilliseconds);
            Metrics.Instance.Histogram.Update(
                new HistogramOptions {Name = "insert_consume_latency", Tags = new MetricTags("same_app", sameApp.ToString())},
                (long) (DateTimeOffset.Now - @event.InsertedAt).TotalMilliseconds);

            Metrics.Instance.Counter.Increment(new CounterOptions {Name = "handled_input_event_count"});
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _lifecycle.Stop();
        }

        public void NotifyEventProduced(InputEvent @event)
        {
            _log.Debug("Consumer {consumerId} received notification about produced event {eventId}", _id, @event.EventId);

            async void ConsumeSafe()
            {
                try
                {
                    await ConsumeOneAsync(default);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error while consuming");
                }
            }

            ConsumeSafe();
        }
    }
}
