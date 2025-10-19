#:sdk Aspire.AppHost.Sdk@9.5.1
#:package Testcontainers.PostgreSql@4.7.0
#:package Testcontainers.PubSub@4.7.0
#:project runtime/app.csproj

using Testcontainers.PostgreSql;
using Testcontainers.PubSub;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = new PostgreSqlBuilder()
    .WithDatabase("micro")
    .WithUsername("user")
    .WithPassword("password")
    .Build();

var pubSub = new PubSubBuilder().Build();

await Task.WhenAll(postgres.StartAsync(), pubSub.StartAsync());

builder
    .AddProject<Projects.app>("app")
    .WithHttpEndpoint(port: 5080)
    .WithReplicas(4)
    .WithEnvironment("postgres", postgres.GetConnectionString())
    .WithEnvironment("pubsub", pubSub.GetEmulatorEndpoint());

builder.Build().Run();
