using System.Threading.Tasks;
using NUnit.Framework;
using Serilog;

namespace EventStore.Tests
{
    public abstract class DatabaseTest
    {
        [SetUp]
        public virtual async Task OnSetUp()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateLogger();

            Metrics.Instance.Manage.Reset();

            await ServiceConfig.Instance.DbStore.PrepareDatabaseAsync(default);
            await ServiceConfig.Instance.DbStore.ClearEventsAsync(default);
        }
    }
}
