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
      OMyFish.SpeciesService.Infrastructure/ MongoDB.Driver (species catalog), EF Core (predictions), AI client, MassTransit
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
- **Exception:** species-service's species catalog lives in MongoDB instead
  (`OMyFish.SpeciesService.Infrastructure/Persistence/SpeciesDocument.cs` +
  `Repositories/SpeciesRepository.cs`, `MongoDB.Driver`) — read-mostly,
  flexible-schema reference data with no relational integrity needs
  (BACKLOG.md item E). `Prediction` stays on Postgres in the same
  `SpeciesDbContext`/`PredictionRepository.cs` as everything below, because
  it commits in the same transaction as the MassTransit outbox message on
  every `/identify` call (item F §2.3) — something MongoDB can't take part
  in. `Species.Reconstitute(...)` restores a persisted id on read; never use
  `Species.Create(...)` (which mints a new one) when mapping a Mongo
  document back to the domain type.
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
- **Publish side (SpeciesService/ObservationService) uses MassTransit's EF Core transactional
  outbox** (`AddEntityFrameworkOutbox<TDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); })`)
  — this replaced a dual-write (DB save, then a separate publish call) where a crash between
  the two silently dropped the event (BACKLOG.md item F §2.3). Each repository's `AddAsync`
  only stages the entity; `IUnitOfWork.SaveChangesAsync(ct)` (one per service) is what the
  command handler calls last, after publishing via `IPublishEndpoint` — both the domain write
  and the outbox row commit in one transaction. Outbox tables (`InboxState`/`OutboxMessage`/
  `OutboxState`) are suffixed `_species`/`_observation` in `migrations/<Service>/002 and
  004_add_outbox.sql` since both services share one physical Postgres database (same reason
  DbUp's journal table is `schemaversions_<service>`, not shared).

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
- `MongoDB.Driver` (species-service's species catalog only — see Database above)
- `Yarp.ReverseProxy` (API Gateway)
- `OpenTelemetry.Extensions.Hosting` (traces)
- `prometheus-net.AspNetCore` (metrics)
- `Serilog.AspNetCore` (structured logging)

## Testing

Test projects live in `tests/` (`OMyFish.ApiGateway.Tests`, `OMyFish.IdentityService.Tests`, `OMyFish.NotificationService.Tests`, `OMyFish.ObservationService.Tests`, `OMyFish.SpeciesService.Tests`) — run with `make test`.

- Unit: xUnit, NSubstitute (or Moq) — no infrastructure deps
- Repository-level integration: `Testcontainers.PostgreSql` on the same
  `postgis/postgis:16-3.4-alpine` image `docker-compose.yml` uses, migrated
  with the real `migrations/<Service>/*.sql` files (`PostgresFixture`, in
  every service's test project except ApiGateway) — not EF's model, so
  schema/entity drift fails a test instead of only surfacing live.
  SpeciesService additionally has `SpeciesMongoRepositoryTests.cs`
  (`Testcontainers.MongoDb`) for the Mongo-backed species catalog
  (BACKLOG.md item E) — covers the id-restoration path explicitly
  (`Species.Reconstitute` vs. `Create`).
- Endpoint-level integration: `WebApplicationFactory<Program>` against the
  real ASP.NET Core pipeline — every Api project needs `public partial
  class Program;` added (top-level statements generate that class
  `internal` otherwise, invisible cross-assembly). ApiGateway's
  `GatewayAuthorizationTests.cs` has no infra deps. SpeciesService/
  ObservationService's `SpeciesApiFixture`/`ObservationApiFixture` add
  `Testcontainers.RabbitMq` (explicit `guest`/`guest` credentials —
  `RabbitMqBuilder` generates random ones by default) alongside Postgres, so
  `/identify`/`POST /observations`'s MassTransit EF Core outbox (§2.3) can
  be proven to actually drain to a real broker, not just write a DB row;
  SpeciesApiFixture further adds `Testcontainers.MongoDb` and fakes only
  `IAIServiceClient`/`IStorageService` (the two genuinely-external
  dependencies `/identify` has) via `ConfigureTestServices`. Read
  `builder.Configuration` lazily (inside a DI factory delegate, or inside a
  deferred callback like `UsingRabbitMq`'s) rather than into a top-level
  variable in `Program.cs` — an eager read runs before
  `WebApplicationFactory`'s test config overrides are merged in and will
  silently pick up the production default instead.
- Use `IMediator` mocks for endpoint unit tests
