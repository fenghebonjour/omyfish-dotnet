# CLAUDE.md — OMyFish Enterprise .NET 10

## Commands

```bash
make up                         # start all Docker services
make down                       # stop all services
make build                      # dotnet build omyfish-dotnet.sln
make test                       # dotnet test (all projects)
make migrate                    # apply SQL migrations via psql
make logs service=species-service   # tail service logs
make shell-postgres             # psql into omyfish DB
make fmt                        # dotnet format
```

```bash
# Build single service
dotnet build src/services/SpeciesService/

# Run a service locally (outside Docker)
cd src/services/SpeciesService/OMyFish.SpeciesService.Api
dotnet run --launch-profile Development
```

## Repository Structure

```
src/
  shared/
    OMyFish.Shared.BuildingBlocks/   AggregateRoot, DomainEvent, CQRS interfaces
    OMyFish.Shared.Contracts/        Integration events (FishIdentifiedEvent, etc.)
  services/
    ApiGateway/OMyFish.ApiGateway/   YARP config, auth middleware
    IdentityService/
      OMyFish.IdentityService.Api/          Minimal API endpoints
      OMyFish.IdentityService.Domain/       User, ApiKey aggregates
      OMyFish.IdentityService.Infrastructure/ EF Core, repositories
    SpeciesService/
      OMyFish.SpeciesService.Api/           Minimal API endpoints
      OMyFish.SpeciesService.Application/   Commands, Queries, Interfaces
      OMyFish.SpeciesService.Domain/        Species, Prediction, ConfidenceScore
      OMyFish.SpeciesService.Infrastructure/ EF Core, AI client, MassTransit
    ObservationService/                     (same 4-project Clean Architecture)
    NotificationService/OMyFish.NotificationService/  Worker + MassTransit consumers
frontend/omyfish-web/                       Next.js 15 + TypeScript
infrastructure/kubernetes/                  K8s manifests
infrastructure/helm/omyfish/                Helm chart
migrations/                                 Raw SQL (applied by make migrate)
```

## Architecture: Clean Architecture

Each service follows 4 projects:
- **Domain** — Aggregates, Entities, Value Objects, Domain Events. No framework deps.
- **Application** — MediatR Commands/Queries, Interfaces (ports). Depends only on Domain.
- **Infrastructure** — EF Core, HttpClient, MassTransit, MinIO. Implements Application interfaces.
- **Api** — Minimal API route registration, DI wiring in `Program.cs`.

**Rule:** Domain and Application must not reference EF Core, ASP.NET, or MassTransit.

## CQRS

All writes are `ICommand<TResult>` handled by `ICommandHandler<,>`.
All reads are `IQuery<TResult>` handled by `IQueryHandler<,>`.
Both use MediatR. Register handlers with `services.AddMediatR(...)`.

## Database

- PostgreSQL + PostGIS via Npgsql + NetTopologySuite EF Core plugin
- Migrations: raw SQL in `migrations/` (no EF Core Migrations — use explicit SQL)
- Apply with: `make migrate` or `psql` directly
- Spatial types: `NetTopologySuite.Geometries.Point` maps to PostGIS `geometry(Point,4326)`

## Messaging (MassTransit + RabbitMQ)

- Integration events: `OMyFish.Shared.Contracts/Events/`
- Quorum queues configured in `RabbitMqHostSettings`
- DLQ suffix: `.dlq` — configure in `IReceiveEndpointConfigurator`
- Retry policy: 3 attempts, exponential backoff (5s, 30s, 5min)

## AI Service

Python service (built from `../omyfish-ai` — see docker-compose.yml) called via `IAIServiceClient` (typed HttpClient).
Adapter in `OMyFish.SpeciesService.Infrastructure/ExternalServices/AIServiceClient.cs`.
Do not add ML.NET or ONNX Runtime to .NET services — keep AI in Python.

Besides fish ID (`POST /predict`), ai-service exposes the Bite Score forecast (`GET /bite-score/forecast|today|species-key`). Bite-score responses always include a six-factor breakdown — pass it through to clients untouched, never reduce it to just the headline score. `GET /bite-score/species-key?name=` maps a confirmed fish ID to the species key to store per user for tuned forecasts.

## Key NuGet Packages

- `MediatR` (CQRS pipeline)
- `MassTransit.RabbitMQ` (messaging)
- `Npgsql.EntityFrameworkCore.PostgreSQL` (EF Core driver)
- `NetTopologySuite` (spatial types for PostGIS)
- `Yarp.ReverseProxy` (API Gateway)
- `OpenTelemetry.Extensions.Hosting` (traces)
- `prometheus-net.AspNetCore` (metrics)
- `Serilog.AspNetCore` (structured logging)

## Testing

- Unit: xUnit, NSubstitute (or Moq) — no infrastructure deps
- Integration: `WebApplicationFactory<Program>` + Testcontainers.PostgreSql
- Use `IMediator` mocks for endpoint unit tests
