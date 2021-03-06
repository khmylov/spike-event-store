using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Counter;
using App.Metrics.Histogram;
using EventStore.Model;
using Serilog;

namespace EventStore.Workflow
{
    internal sealed record EventProducedSignal;

    internal sealed class ConsumerStateMachine
    {
        private static readonly ILogger _log = Log.ForContext<ConsumerStateMachine>();
        private ConsumerState _currentState = InitialState.Instance;

        private readonly ConsumerConfig _config;
        private readonly Func<Task<bool>> _fetchNext;

        public ConsumerStateMachine(
            ConsumerConfig config,
            Func<Task<bool>> fetchNext)
        {
            _config = config;
            _fetchNext = fetchNext;
        }

        public void Start()
        {
            TransitionTo(InitialState.Instance, new FetchingState());
        }

        public void Stop()
        {
            _currentState = InitialState.Instance;
        }

        public void TransitionTo(ConsumerState expectedCurrent, ConsumerState state)
        {
            var originalState = Interlocked.CompareExchange(ref _currentState, state, expectedCurrent);

            if (originalState == expectedCurrent)
            {
                _log.Debug("Entering state {stateType}", state);
                state.OnEnter(this);
            }
            else
            {
                _log.Warning(
                    "Transition to state {nextState} not completed because expected state is {expectedState} but actual original is {originalState}",
                    state, expectedCurrent, originalState);
            }
        }

        public void ScheduleTransitionTo(TimeSpan delay, ConsumerState expectedCurrent, ConsumerState state)
        {
            var currentState = _currentState;

            if (currentState == expectedCurrent)
            {
                RunAfterDelay();
            }
            else
            {
                _log.Warning(
                    "Skipping scheduled state transition to {nextState} because expected current to be {expectedState}, but it's actually {currentState}",
                    state, expectedCurrent, currentState);
            }

            async void RunAfterDelay()
            {
                try
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    TransitionTo(expectedCurrent, state);
                }
                catch (Exception ex)
                {
                    _log.Error("Error while executing scheduled transition");
                }
            }
        }

        public void Handle(EventProducedSignal signal)
        {
            _currentState.Handle(this, signal);
        }

        private async void RunSafe(Func<Task> func)
        {
            try
            {
                await func().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("Unexpected consumer state machine error", ex);
                _currentState = CorruptedState.Instance;
            }
        }

        internal abstract class ConsumerState
        {
            public virtual void Handle(ConsumerStateMachine stateMachine, EventProducedSignal signal)
            {
            }

            public virtual void OnEnter(ConsumerStateMachine stateMachine)
            {
            }
        }

        private sealed class InitialState : ConsumerState
        {
            public static readonly InitialState Instance = new();
        }

        private sealed class FetchingState : ConsumerState
        {
            private bool _eventProducedSignalled;

            public override void Handle(ConsumerStateMachine stateMachine, EventProducedSignal signal)
            {
                _eventProducedSignalled = true;
            }

            public override void OnEnter(ConsumerStateMachine stateMachine)
            {
                stateMachine.RunSafe(async () =>
                {
                    var consumed = await stateMachine._fetchNext().ConfigureAwait(false);
                    if (consumed)
                    {
                        stateMachine.TransitionTo(this, new FetchedState());
                    }
                    else if (_eventProducedSignalled)
                    {
                        stateMachine.ScheduleTransitionTo(stateMachine._config.PickNextInterval, this, new FetchingState());
                    }
                    else
                    {
                        stateMachine.TransitionTo(this, new FetchedEmptyState());
                    }
                });
            }
        }

        private sealed class FetchedState : ConsumerState
        {
            public override void OnEnter(ConsumerStateMachine stateMachine)
            {
                base.OnEnter(stateMachine);
                stateMachine.ScheduleTransitionTo(stateMachine._config.PickNextInterval, this, new FetchingState());
            }
        }

        private sealed class FetchedEmptyState : ConsumerState
        {
            public override void Handle(ConsumerStateMachine stateMachine, EventProducedSignal signal)
            {
                base.Handle(stateMachine, signal);
                stateMachine.TransitionTo(this, new FetchingState());
            }

            public override void OnEnter(ConsumerStateMachine stateMachine)
            {
                base.OnEnter(stateMachine);
                stateMachine.ScheduleTransitionTo(stateMachine._config.PollingInterval, this, new FetchingState());
            }
        }

        private sealed class CorruptedState : ConsumerState
        {
            public static readonly CorruptedState Instance = new();
        }
    }

    internal sealed class Consumer : IDisposable
    {
        private static readonly ILogger _log = Log.ForContext<Consumer>();

        private readonly int _id = InstanceCounter.GetNextId("Consumer");
        private readonly LifecycleState _lifecycle = new();

        private readonly DbStore _store;
        private readonly Guid _applicationId;
        private readonly ConsumerConfig _config;
        private readonly ConsumerStateMachine _stateMachine;

        private int? _lastConsumedDatabaseId;

        public Consumer(DbStore store, Guid applicationId, ConsumerConfig config)
        {
            _log.Information("Created new consumer {consumerId}", _id);
            _store = store;
            _applicationId = applicationId;
            _config = config;
            _stateMachine = new ConsumerStateMachine(config, async () =>
            {
                try
                {
                    return await ConsumeManyAsync(default).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Error("Error consuming event", ex);
                    return false;
                }
            });
        }

        public event Action<InputEvent>? EventConsumed;

        public void StartConsuming(CancellationToken cancellationToken)
        {
            if (!_lifecycle.Start())
            {
                return;
            }

            _log.Debug("Consumer {consumerId} starting new event monitoring...", _id);
            _stateMachine.Start();
        }

        private Task<bool> ConsumeManyAsync(CancellationToken cancellationToken)
        {
            return _store.ConsumeManyAsync(_config.BatchFetchSize, Handle, cancellationToken);
        }

        private async Task Handle(IReadOnlyList<InputEvent> events)
        {
            Metrics.Measure.Histogram.Update(new HistogramOptions {Name = "fetched_batch_size"}, events.Count);

            foreach (var @event in events)
            {
                var lastConsumed = _lastConsumedDatabaseId;

                var databaseId = @event.DatabaseId;
                if (databaseId <= lastConsumed)
                {
                    _log.Error("Last consumed ID {lastConsumed} > current ID {current}", lastConsumed, databaseId);
                    Metrics.Measure.Counter.Increment(new CounterOptions {Name = "invalid_consume_order_count"});
                }

                _lastConsumedDatabaseId = databaseId;

                var sameApp = @event.ApplicationId == _applicationId;

                Metrics.Measure.Histogram.Update(
                    new HistogramOptions {Name = "create_consume_latency", Tags = new MetricTags("same_app", sameApp.ToString())},
                    (long) (DateTimeOffset.Now - @event.CreatedAt).TotalMilliseconds);
                Metrics.Measure.Histogram.Update(
                    new HistogramOptions {Name = "insert_consume_latency", Tags = new MetricTags("same_app", sameApp.ToString())},
                    (long) (DateTimeOffset.Now - @event.InsertedAt).TotalMilliseconds);

                Metrics.Measure.Counter.Increment(new CounterOptions {Name = "handled_input_event_count"});
                EventConsumed?.Invoke(@event);
            }

            await Task.Delay(_config.HandlerDuration).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (!_lifecycle.Stop())
            {
                return;
            }

            _stateMachine.Stop();
        }

        public void NotifyEventProduced(InputEvent @event)
        {
            _log.Debug("Consumer {consumerId} received notification about produced event {eventId}", _id, @event.EventId);
            _stateMachine.Handle(new EventProducedSignal());
        }
    }
}
