using System;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics.Counter;
using EventStore.Model;
using Serilog;

namespace EventStore.Workflow
{
    internal sealed class Producer : IDisposable
    {
        private static readonly ILogger _log = Log.ForContext<Producer>();

        private readonly int _id = InstanceCounter.GetNextId("Producer");
        private readonly DbStore _store;
        private readonly Guid _applicationId;
        private readonly ProducerConfig _config;

        private readonly LifecycleState _lifecycle = new();

        public Producer(DbStore store, Guid applicationId, ProducerConfig config)
        {
            _log.Debug("Created new producer {id}", _id);
            _store = store;
            _applicationId = applicationId;
            _config = config;
        }

        public event Action<InputEvent>? EventProduced;

        public async Task PublishNewEventAsync(CancellationToken cancellationToken)
        {
            var input = new CreateInputEventRequest(
                Guid.NewGuid(),
                DateTimeOffset.Now,
                _applicationId,
                new EventPayload(InstanceCounter.GetNextId("EventNumber")));
            _log.Information(
                "Publisher {publisherId} created new event {eventId} with payload {value}, going to publish...",
                _id, input.EventId, input.Payload.Number);
            var stored = await _store.Enqueue(input, cancellationToken);
            Metrics.Instance.Counter.Increment(new CounterOptions {Name = "produced_event_count"});
            EventProduced?.Invoke(stored);
        }

        public void StartPublishingContinuously(CancellationToken cancellationToken)
        {
            if (!_lifecycle.Start())
            {
                return;
            }

            _log.Debug("Producer {producerId} starts continuous publishing with interval {interval}", _id, _config.Interval);

            _lifecycle.AddChild(
                () => new Timer(async _ =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        await PublishNewEventAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error while publishing events by timer from producer {producerId}", _id);
                    }
                }, null, _config.StartDelay, _config.Interval),
                cancellationToken);
        }

        public void Dispose()
        {
            _lifecycle.Stop();
        }
    }
}
