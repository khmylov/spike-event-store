using System;
using System.Collections.Generic;

namespace EventStore.Workflow
{
    internal sealed record ProducerConfig(
        TimeSpan StartDelay,
        TimeSpan Interval);

    internal sealed record ConsumerConfig(
        TimeSpan PollingInterval,
        TimeSpan PickNextInterval);

    internal sealed record ApplicationConfig(
        IReadOnlyList<ProducerConfig> Producers,
        IReadOnlyList<ConsumerConfig> Consumers);
}
