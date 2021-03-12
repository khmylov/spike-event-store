using System;

namespace EventStore.Model
{
    internal sealed record EventPayload(int Number);

    /// <summary>
    /// "Root" event, containing data for all observers.
    /// </summary>
    internal sealed record InputEvent(
        int DatabaseId,
        Guid EventId,
        Guid ApplicationId,
        DateTimeOffset CreatedAt,
        DateTimeOffset InsertedAt,
        EventPayload Payload)
    {
        public override string ToString() => $"Event {EventId} (#{DatabaseId})";
    }

    internal class ObserverEvent
    {
        public int DatabaseId { get; init; }
        public Guid EventId { get; init; }
        public Guid InputEventId { get; init; }
        public EventPayload Payload { get; init; } = default!;
        public string EventType { get; init; } = default!;
    }
}
