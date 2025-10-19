# A Microcosm of Distributed Systems

This repo contains a small microcosm of a distributed system using:

- .NET 10
- .NET Aspire
- Test containers for Postgres and Google Cloud Pub/Sub

Pre-requisites:

- Install Docker or Podman
- [Install .NET 10 for your platform](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

```bash
# If you want to build along from scratch
dotnet tool install -g Aspire.Cli --prerelease
```

Start it at the root:

```bash
dotnet build && dotnet run apphost.cs
```
