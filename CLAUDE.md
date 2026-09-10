# CLAUDE.md — OMyFish Enterprise .NET 10

## Commands

```bash
make up                         # start all Docker services
make build-up                   # rebuild images first, then start (code/deps/Dockerfile changed)
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
    NotificationService/OMyFish.NotificationService/  Web API (notifications read/mark-read) + MassTransit consumers
frontend/omyfish-web/                       Next.js 15 + TypeScript (pages: / [Timing], /identify, /regs, /observations, /notifications, /login, /register)
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
- **Applied automatically on service startup via DbUp** — each Api project embeds its own
  `migrations/<Service>/*.sql` at build time (`EmbeddedResource` + `LogicalName="Migrations.*"`
  in the `.csproj`) and runs them through `DeployChanges.To.PostgresqlDatabase(...)` before
  `app.Run()`, failing fast (throwing) if a migration fails — this replaced
  `EnsureCreatedAsync()`, which only created schema on an empty database and silently no-op'd
  on an existing one, masking any new column/table until someone hit the resulting
  `PostgresException` at runtime (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.1)
- `make migrate` / `psql` directly still works as a manual fallback (e.g. inspecting schema
  state without starting a service), but is no longer the primary path
- **Migration files, once applied by DbUp in any environment, must never be edited** — DbUp
  tracks applied scripts by name in its own journal table and won't re-run them; add a new
  numbered file for any further change, the same convention this repo already uses
  (`00N_description.sql`)
- New migration files must be idempotent-safe for their *first* run against a database that
  already has the target objects (from a pre-DbUp `EnsureCreatedAsync()`, or a fresh empty
  DB) — use `CREATE TABLE/INDEX IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`, `CREATE OR REPLACE
  FUNCTION`, and `DROP TRIGGER IF EXISTS` + `CREATE TRIGGER` (Postgres has no `CREATE TRIGGER
  IF NOT EXISTS`)
- Spatial types: `NetTopologySuite.Geometries.Point` maps to PostGIS `geometry(Point,4326)`

## Messaging (MassTransit + RabbitMQ)

- Integration events: `OMyFish.Shared.Contracts/Events/`
- Receive endpoints (NotificationService only — SpeciesService/ObservationService only
  publish) use quorum queues via `e.SetQuorumQueue()` per `ReceiveEndpoint(...)` call
- Failed messages land in MassTransit's own `<queue>_error` fault queue (its default
  application-level dead-lettering, not a custom `.dlq` suffix or RabbitMQ's native DLX)
- Retry policy: 3 attempts, exponential backoff — `r.Exponential(3, min: 5s, max: 5min, delta: 30s)`,
  configured on NotificationService's two receive endpoints only (not on the publish side)

## AI Service

Python service (built from `../omyfish-ai` — see docker-compose.yml) called via `IAIServiceClient` (typed HttpClient).
Adapter in `OMyFish.SpeciesService.Infrastructure/ExternalServices/AIServiceClient.cs`.
Do not add ML.NET or ONNX Runtime to .NET services — keep AI in Python.

Besides fish ID (`POST /predict`), ai-service exposes the Bite Score forecast (`GET /bite-score/forecast|today|species-key`). Bite-score responses always include a six-factor breakdown — pass it through to clients untouched, never reduce it to just the headline score. `GET /bite-score/species-key?name=` maps a confirmed fish ID to the species key to store per user for tuned forecasts.

species-service proxies these at `GET /api/v1/species/bite-score/forecast|today` (rides the existing YARP species catch-all; `species` accepts a key or any name — `AIServiceClient` resolves it via `/bite-score/species-key`, general fallback). Frontend: the `/` (Timing) landing page (`src/app/page.tsx`, components in `src/components/timing/`) — 7-day outlook strip with 14-day calendar, hourly activity curve, Major/Minor peak windows, storm safety alerts — plus `BiteScorePanel` on each located observation card.

ai-service also exposes the Quebec Regs Advisor (`/regs/*` — limits, consumption advisory, zones GeoJSON, free-form Q&A), proxied the same way at `GET/POST /api/v1/species/regs/*`. Frontend: the `/regs` chat page (`RegsChat.tsx`), two info cards on the identify result (`FishUploader.tsx`), and a toggle-able zones/stations map overlay on `/observations` (`ObservationMap.tsx`).

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

Test projects live in `tests/` (`OMyFish.IdentityService.Tests`, `OMyFish.ObservationService.Tests`, `OMyFish.SpeciesService.Tests`) — run with `make test`.

- Unit: xUnit, NSubstitute (or Moq) — no infrastructure deps
- Integration: `WebApplicationFactory<Program>` + Testcontainers.PostgreSql
- Use `IMediator` mocks for endpoint unit tests
