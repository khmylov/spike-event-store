using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore
{
    [UsedImplicitly]
    public class Startup
    {
        [UsedImplicitly]
        public void ConfigureServices(IServiceCollection services)
        {
        }

        [UsedImplicitly]
        public void Configure(
            IApplicationBuilder app,
            IWebHostEnvironment env,
            ILoggerFactory loggerFactory)
        {
        }
    }
}
