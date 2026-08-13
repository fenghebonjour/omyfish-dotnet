# OMyFish — Enterprise .NET 10 Platform

> **OMyFish — Your AI Fishing Companion.** *When, Where, What you catch.* A Bite Score timing forecast for **when** to fish, AI fish identification with GPS-logged observations on a map for **where** fish are being caught, and a Regs & Tips chatbot (powered by Groq) for **what** you can legally keep — built on .NET 10 + ASP.NET Core with Clean Architecture, CQRS (MediatR), and Event-Driven design.

> [!NOTE]
> **Repo reorganization (July 2026):** the OMyFish platform is split into six repos: [omyfish-python](https://github.com/fenghebonjour/omyfish-python) (the AI-first origin, previously named `omyfish` — the old link redirects there — now kept in place for training the fish-ID model), [omyfish-ai](https://github.com/fenghebonjour/omyfish-ai) (standalone AI microservice shared by all), [omyfish-java](https://github.com/fenghebonjour/omyfish-java), **omyfish-dotnet** (this one), and the two newest siblings, [omyfish-python-web](https://github.com/fenghebonjour/omyfish-python-web) (Django, public) and omyfish-ios (SwiftUI, private).

## Stack

| Layer        | Technology                                               |
|--------------|----------------------------------------------------------|
| Frontend     | Next.js 15 · TypeScript · MapLibre GL JS                 |
| API Gateway  | YARP Reverse Proxy                                       |
| Services     | .NET 10 · ASP.NET Core Minimal APIs · EF Core · Npgsql  |
| AI Layer     | Python 3.11 · PyTorch · ONNX Runtime · FastAPI           |
| Messaging    | RabbitMQ 3.13 (Quorum Queues) · MassTransit             |
| Database     | PostgreSQL 16 + PostGIS 3.4 · NetTopologySuite           |
| Object Store | MinIO (dev) · AWS S3 / Azure Blob (prod)                 |
| Infra        | Docker Compose · Kubernetes · Helm 3                     |
| Observability| OpenTelemetry · Prometheus · Grafana · Jaeger · Serilog  |
| CI/CD        | GitLab CI/CD                                             |

## Quick Start (Development)

```bash
# Prerequisites: Docker, .NET 10 SDK, Node 20

# 1. Start infrastructure + all services
make up

# 2. Build all .NET services
make build

# 3. Run database migrations
make migrate

# 4. Create MinIO buckets
make minio-create-buckets

# 5. Open the app
open http://localhost:3000          # Frontend
open http://localhost:8080/health   # API Gateway health
open http://localhost:15672         # RabbitMQ Management (omyfish/omyfish_dev)
open http://localhost:9001          # MinIO Console
open http://localhost:16686         # Jaeger UI
open http://localhost:3001          # Grafana (admin/admin)
```

## Project Structure

```
omyfish-dotnet/
  src/
    services/
      ApiGateway/              ← YARP + auth middleware
      IdentityService/         ← JWT, OAuth2/OIDC, API keys
      SpeciesService/          ← AI orchestration, CQRS, species KB
      ObservationService/      ← PostGIS, EXIF, GeoJSON, MinIO
      NotificationService/     ← notifications web API + MassTransit consumers
      AIService/               ← builds from ../omyfish-ai (shared: Bite Score + fish ID + Regs & Tips chatbot)
    shared/
      OMyFish.Shared.BuildingBlocks/   ← CQRS interfaces, AggregateRoot
      OMyFish.Shared.Contracts/        ← Integration events
  frontend/omyfish-web/        ← Next.js 15 frontend (/, /timing, /observations, /notifications, /login, /register)
  infrastructure/
    kubernetes/                ← Deployments, HPA, Ingress
    helm/omyfish/              ← Helm chart
  migrations/                  ← SQL migration scripts
  .gitlab-ci.yml
  docker-compose.yml
  Makefile
```

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for full diagrams, DDD bounded contexts, CQRS flow, database schemas, scaling strategy, and migration roadmap.

## Service Ports (dev)

| Service              | Port |
|----------------------|------|
| Frontend             | 3000 |
| API Gateway (YARP)   | 8080 |
| Identity Service     | 8081 |
| Species Service      | 8082 |
| Observation Service  | 8083 |
| Notification Service | 8084 |
| AI Service (Python)  | 8000 |
| PostgreSQL           | 5432 |
| RabbitMQ AMQP        | 5672 |
| RabbitMQ Management  | 15672|
| MinIO API            | 9000 |
| MinIO Console        | 9001 |
| Prometheus           | 9090 |
| Grafana              | 3001 |
| Jaeger UI            | 16686|
