using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using EventStore.Database;
using EventStore.Workflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EventStore
{
    public static class Program
    {
        static async Task Main()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.Async(a => a.File("log.txt"))
                .CreateLogger();

            Log.Information("Application started at {DateTime}, press Ctrl+C to quit", DateTimeOffset.Now);
            var cts = new CancellationTokenSource();
            var done = ConfigureCancellation(cts);
            await Run(done, cts.Token);
            Log.CloseAndFlush();
        }

        private static WaitHandle ConfigureCancellation(CancellationTokenSource cancellationSource)
        {
            var done = new ManualResetEvent(false);

            Console.CancelKeyPress += (_, args) =>
            {
                args.Cancel = true;
                Log.Information("Shutting down...");
                cancellationSource.Cancel();
                done.Set();
            };

            return done;
        }

        private static async Task Run(WaitHandle done, CancellationToken cancellationToken)
        {
            // Run
            var tasks = new List<Task>();

            using (var host = CreateWebHost())
            {
                Metrics.Instance = host.Services.GetRequiredService<IMetrics>().Measure;

                // Dependencies
                var connectionStringBuilder = new SqlConnectionStringBuilder(
                    "Server=(local);Initial Catalog=EventStore;Integrated Security=SSPI;Connection Timeout=60;");
                var connectionProvider = new ConnectionProvider(connectionStringBuilder);
                var dbStore = new DbStore(connectionProvider);

                // Init
                await dbStore.PrepareDatabaseAsync(cancellationToken);
                await dbStore.ClearEventsAsync(cancellationToken);

                foreach (var appConfig in CreateApplications())
                {
                    var app = new Application(dbStore, appConfig);
                    Task.Run(() => app.Start(cancellationToken));
                }

                tasks.Add(host.StartAsync(cancellationToken));

                done.WaitOne();
            }

            await Task.WhenAll(tasks);
        }

        private static IHost CreateWebHost()
        {
            return Host
                .CreateDefaultBuilder()
                .UseMetricsEndpoints()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseKestrel(options =>
                        {
                            options.AllowSynchronousIO = true;
                        })
                        .UseUrls("http://*:6242")
                        .UseStartup<Startup>();
                })
                .Build();
        }

        private static IEnumerable<ApplicationConfig> CreateApplications()
        {
            // Main app which produces and consumes
            yield return new ApplicationConfig(
                Producers: Enumerable
                    .Range(1, 10)
                    .Select(_ => new ProducerConfig(
                        StartDelay: Rand.TimeSpanMs(0, 100),
                        Interval: Rand.TimeSpanMs(300, 1000)))
                    .ToList(),
                Consumers: Enumerable
                    .Range(1, 1)
                    .Select(_ => new ConsumerConfig(
                        PollingInterval: TimeSpan.FromSeconds(3000),
                        PickNextInterval: TimeSpan.Zero))
                    .ToList());

            // Secondary app which produces only
            yield return new ApplicationConfig(
                Producers: Enumerable
                    .Range(1, 6)
                    .Select(_ => new ProducerConfig(
                        StartDelay: Rand.TimeSpanMs(0, 100),
                        Interval: Rand.TimeSpanMs(300, 1000)))
                    .ToList(),
                Consumers: Array.Empty<ConsumerConfig>());
        }
    }
}
