using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("postgres");
var pubSubEndpoint = Environment.GetEnvironmentVariable("pubsub");

Console.WriteLine($"App starting; postgres@ {connectionString}");

var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddHostedService<TopologyService>()
    .AddHostedService<ConsumerService>()
    .AddHostedService<MonitoringService>()
    .AddDbContextPool<MicroDbContext>(options => options.UseNpgsql(connectionString))
    .AddDbContextFactory<MicroDbContext>(options => options.UseNpgsql(connectionString))
    .AddSingleton(
        new PubSubGateway(endpoint: pubSubEndpoint ?? "localhost:8085", projectId: "test-project")
    );

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = app.Services.GetRequiredService<MicroDbContext>();
        db.Database.EnsureCreated();
    }
    catch (PostgresException ex) when (ex.SqlState == "42P07") // duplicate_table
    {
        Console.WriteLine("Database is already initialized.");
    }
}

app.MapGet(
    "/",
    async (PubSubGateway pubSub) =>
        await pubSub.PublishMessageAsync(
            topicId: "messages",
            messageText: "Hello, World! @ " + DateTimeOffset.UtcNow.ToString("o")
        )
);

app.Run();

class TopologyService(IDbContextFactory<MicroDbContext> factory, PubSubGateway pubSub)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Initializing topology...");

        await using var context = await factory.CreateDbContextAsync(stoppingToken);

        var acquired = await context.AcquireClusterRoleAsync(
            roleName: "topology-manager",
            leaseDuration: TimeSpan.FromSeconds(15)
        );

        bool topicReady = false,
            subscriptionReady = false;

        do
        {
            if (!topicReady)
            {
                topicReady = await pubSub.VerifyTopicExists(
                    "messages",
                    createIfNotExists: acquired
                );

                if (!acquired)
                {
                    Console.WriteLine("Waiting for topic 'messages' to be created...");
                    await Task.Delay(1000, stoppingToken);
                }
            }

            if (!subscriptionReady)
            {
                subscriptionReady = await pubSub.VerifySubscriptionExists(
                    topicId: "messages",
                    subscriptionId: "message-consumers",
                    createIfNotExists: acquired
                );

                if (!acquired)
                {
                    Console.WriteLine(
                        "Waiting for subscription 'message-consumers' to be created..."
                    );
                    await Task.Delay(1000, stoppingToken);
                }
            }
        } while (!(topicReady && subscriptionReady) && !stoppingToken.IsCancellationRequested);

        Console.WriteLine($"Topology initialized; acquired ownership? {acquired}");

        pubSub.ReadySignal.SetResult();
    }
}

class ConsumerService(PubSubGateway pubSub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await pubSub.ReadySignal.Task;

        Console.WriteLine("Topology ready; starting message consumer...");

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (
                var message in await pubSub.PullMessagesAsync("message-consumers", stoppingToken)
            )
            {
                var text = System.Text.Encoding.UTF8.GetString(message.Message.Data.ToArray());

                await pubSub.AcknowledgeMessagesAsync(
                    "message-consumers",
                    [message.AckId],
                    stoppingToken
                );

                Console.WriteLine($"Received message: {text}");
            }
        }
    }
}

class MonitoringService(IDbContextFactory<MicroDbContext> factory) : BackgroundService
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var currentlyOwned = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var context = await factory.CreateDbContextAsync(stoppingToken);

            var acquired = await context.AcquireClusterRoleAsync(
                roleName: "topology-monitor",
                leaseDuration: SamplingInterval,
                renewExisting: currentlyOwned
            );

            currentlyOwned = acquired;

            if (acquired)
            {
                Console.WriteLine($"[Monitoring] Acquired or renewed lease");

                await Task.Delay(SamplingInterval, stoppingToken);
            }
            else
            {
                // Not acquired; 2x the sampling interval
                await Task.Delay(SamplingInterval * 2, stoppingToken);
            }
        }
    }
}
