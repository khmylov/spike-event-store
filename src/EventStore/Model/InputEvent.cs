using System;

namespace EventStore.Model
{
    internal sealed record EventPayload(int Number);

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
}
