using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Model;

namespace EventStore.Workflow
{
    internal sealed record ObserverConfig(string Type);

    internal sealed class Observer
    {
        public Observer(DbStore store, ObserverConfig config)
        {

        }

        public Task<bool> HandleAsync(InputEvent @event, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
