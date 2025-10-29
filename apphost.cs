#:sdk Aspire.AppHost.Sdk@9.5.2
#:package Testcontainers.PostgreSql@4.8.1
#:package Testcontainers.Redis@4.8.1
#:package Testcontainers.PubSub@4.8.1
#:project runtime/app.csproj

using Testcontainers.PostgreSql;
using Testcontainers.PubSub;
using Testcontainers.Redis;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = new PostgreSqlBuilder()
    .WithDatabase("micro")
    .WithUsername("user")
    .WithPassword("password")
    .Build();

var pubSub = new PubSubBuilder().Build();

var redis = new RedisBuilder().Build();

await Task.WhenAll(postgres.StartAsync(), pubSub.StartAsync(), redis.StartAsync());

builder
    .AddProject<Projects.app>("app")
    .WithHttpEndpoint(port: 5080)
    .WithReplicas(4)
    .WithEnvironment("postgres", postgres.GetConnectionString())
    .WithEnvironment("pubsub", pubSub.GetEmulatorEndpoint())
    .WithEnvironment("redis", redis.GetConnectionString());

builder.Build().Run();
