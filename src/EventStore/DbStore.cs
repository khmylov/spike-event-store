using System;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using EventStore.Database;
using EventStore.Model;
using JetBrains.Annotations;
using Serilog;

namespace EventStore
{
    internal sealed record CreateInputEventRequest(Guid EventId, DateTimeOffset CreatedAt, Guid ApplicationId, EventPayload Payload);

    [PublicAPI]
    internal sealed class InputEventReadDto
    {
        public int Id { get; set; }
        public Guid InputEventId { get; set; }
        public string EventType { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset InsertedAt { get; set; }
        public string Payload { get; set; } = default!;
        public Guid ApplicationId { get; set; }
    }

    internal sealed class DbStore
    {
        private static readonly ILogger _log = Log.ForContext<DbStore>();

        private readonly ConnectionProvider _connectionProvider;

        public DbStore(ConnectionProvider connectionProvider)
        {
            _connectionProvider = connectionProvider;
        }

        public async Task PrepareDatabaseAsync(CancellationToken cancellationToken)
        {
            _log.Debug("Checking database structure");

            await _connectionProvider.WithConnectionAsync(async connection =>
            {
                await connection.ExecuteAsync(@"
IF OBJECT_ID('EventStore') IS NULL
BEGIN
    CREATE TABLE [dbo].[EventStore] (
        [Id] INT IDENTITY(1, 1) NOT NULL,
        [InputEventId] UNIQUEIDENTIFIER NOT NULL,
        [EventType] NVARCHAR(255) NOT NULL,
        [CreatedAt] DATETIMEOFFSET NOT NULL,
        [InsertedAt] DATETIMEOFFSET CONSTRAINT [DF_EventStore_InsertedAt] DEFAULT (SYSDATETIMEOFFSET()) NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [ApplicationId] UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT [PK_EventStore] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END");
                await connection.ExecuteAsync(@"
IF IndexProperty(OBJECT_ID('EventStore'), 'IX_EventStore_InputEventId', 'IndexId') IS NULL
BEGIN
    CREATE NONCLUSTERED INDEX [IX_EventStore_InputEventId] ON [EventStore]([InputEventId]) INCLUDE ([EventType]);
END
");

                return Unit.Default;
            }, cancellationToken);
        }

        public async Task ClearEventsAsync(CancellationToken cancellationToken)
        {
            _log.Debug("Removing all stored events");

            await _connectionProvider.WithConnectionAsync(async connection =>
            {
                await connection.ExecuteAsync(@"DELETE FROM [EventStore]");
                return Unit.Default;
            }, cancellationToken);
        }

        public async Task<InputEvent> Enqueue(CreateInputEventRequest @event, CancellationToken cancellationToken)
        {
            _log.Debug("Enqueueing event {eventId}", @event.EventId);
            var retrieved = await _connectionProvider.WithConnectionAsync(async connection =>
            {
                var output = await connection.QueryFirstAsync<InputEventReadDto>(@"
INSERT INTO [EventStore] ([InputEventId], [EventType], [CreatedAt], [Payload], [ApplicationId])
OUTPUT inserted.*
VALUES (@eventId, @eventType, @createdAt, @payload, @applicationId)
",
                    new
                    {
                        eventId = @event.EventId,
                        eventType = "InputEvent",
                        createdAt = @event.CreatedAt,
                        payload = @$"{{""number"": {@event.Payload.Number}}}",
                        applicationId = @event.ApplicationId
                    });

                return Map(output);
            }, cancellationToken);
            _log.Information("Inserted {event}", retrieved);

            return retrieved;
        }

        public async Task<bool> ConsumeOneAsync(Func<InputEvent, Task> handle, CancellationToken cancellationToken)
        {
            _log.Debug("Trying to consume next task...");
            return await _connectionProvider.WithConnectionAsync(async connection =>
            {
                await using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                var read = await connection
                    .QueryFirstOrDefaultAsync<InputEventReadDto?>(
                        @"
WITH cte AS (
    SELECT TOP(1) *
    FROM [EventStore] WITH (ROWLOCK, READCOMMITTEDLOCK, READPAST)
    WHERE [EventType] = N'InputEvent'
    ORDER BY [Id] ASC
)
DELETE FROM cte
OUTPUT deleted.*
",
                        transaction: transaction);

                if (read is null)
                {
                    _log.Information("Nothing to read");
                    return false;
                }

                var @event = Map(read);
                _log.Information("Read one entry {data}", @event);
                await handle(@event);

                _log.Information("Finished processing {event}, committing...", @event);

                await transaction.CommitAsync(cancellationToken);

                return true;
            }, cancellationToken);
        }

        private InputEvent Map(InputEventReadDto dto)
        {
            var number = JsonSerializer.Deserialize<JsonElement>(dto.Payload).GetProperty("number").GetInt32();
            return new InputEvent(
                dto.Id, dto.InputEventId, dto.ApplicationId, dto.CreatedAt, dto.InsertedAt,
                new EventPayload(number));
        }
    }
}
