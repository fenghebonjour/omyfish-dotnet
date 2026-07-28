# OMyFish Enterprise .NET 10 Architecture

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENTS                                        │
│   Browser (Next.js)    Mobile App (future)    Third-Party APIs              │
└────────────────────────────────┬────────────────────────────────────────────┘
                                 │ HTTPS
┌────────────────────────────────▼────────────────────────────────────────────┐
│                    API GATEWAY (YARP Reverse Proxy)                         │
│         JWT validation · Rate limiting · Routing · CORS · API Keys          │
└──────┬──────────────┬───────────────┬───────────────┬────────────────────── ┘
       │              │               │               │
┌──────▼──────┐ ┌─────▼──────┐ ┌─────▼──────┐ ┌─────▼──────────┐
│  Identity   │ │  Species   │ │Observation │ │ Notification   │
│  Service   │ │  Service   │ │  Service   │ │   Service      │
│  .NET 10   │ │  .NET 10   │ │  .NET 10   │ │   .NET 10      │
│  Minimal   │ │  Minimal   │ │  Minimal   │ │  Minimal API   │
│  APIs + EF │ │  APIs + EF │ │  APIs + EF │ │  + MassTransit │
└──────┬──────┘ └──────┬─────┘ └──────┬─────┘ └────────────────┘
       │               │              │              ▲
       │               │ HTTP         │              │
       │               ▼              │              │
       │        ┌─────────────┐       │       ┌──────────────┐
       │        │  AI Service │       │       │   RabbitMQ   │
       │        │  Python 3.11│       │       │  (Quorum Q)  │
       │        │  FastAPI    │       │       └──────────────┘
       │        │  PyTorch    │       │              ▲
       │        │  ONNX RT    │       │     Domain events via
       │        └─────────────┘       │     MassTransit publish
       ▼                              ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                     PostgreSQL 16 + PostGIS 3.4                            │
│    identity_db           species_db          observation_db                 │
└────────────────────────────────────────────────────────────────────────────┘
┌────────────────────────────────────────────────────────────────────────────┐
│            MinIO (dev) / AWS S3 or Azure Blob (prod)                       │
│                  omyfish-images    omyfish-exports                          │
└────────────────────────────────────────────────────────────────────────────┘
┌────────────────────────────────────────────────────────────────────────────┐
│              Observability                                                  │
│   OpenTelemetry → Jaeger (traces)                                           │
│   ASP.NET Metrics → Prometheus → Grafana (dashboards)                      │
│   Serilog JSON → ELK / Loki (logs)                                         │
└────────────────────────────────────────────────────────────────────────────┘
```

## Microservice Decomposition

| Service              | Responsibility                                              | Port | Tech                            |
|----------------------|-------------------------------------------------------------|------|---------------------------------|
| **ApiGateway**       | YARP routing, JWT validation, rate limiting, CORS           | 8080 | YARP, ASP.NET Core              |
| **IdentityService**  | User auth, JWT issuance, OAuth2/OIDC, API keys             | 8081 | ASP.NET Identity, EF Core       |
| **SpeciesService**   | AI orchestration, species KB, CQRS predictions             | 8082 | MediatR, EF Core, MassTransit   |
| **ObservationService**| Observation CRUD, EXIF extraction, PostGIS, GeoJSON        | 8083 | NetTopologySuite, MinIO SDK     |
| **NotificationService**| Notifications read/mark-read API, async event consumers  | 8084 | Minimal API, EF Core, MassTransit |
| **AIService**        | EfficientNet-B3 inference, CLIP fallback, Bite Score forecast — shared `omyfish-ai` | 8000 | Python 3.11, FastAPI, PyTorch   |

## DDD Bounded Contexts

```
┌──────────────────────────────┐    ┌──────────────────────────────────┐
│     IDENTITY CONTEXT         │    │     SPECIES CONTEXT              │
│                              │    │                                  │
│  Aggregate: User             │    │  Aggregate: Species              │
│  Aggregate: ApiKey           │    │  Entity:    Prediction           │
│  ValueObj:  Email            │    │  ValueObj:  ConfidenceScore      │
│  ValueObj:  HashedPassword   │    │  Event:     FishIdentifiedEvent  │
│  Command:   RegisterUser     │    │  Command:   IdentifyFishCommand  │
│  Command:   IssueToken       │    │  Query:     GetSpeciesQuery      │
│                              │    │  Query:     GetBiteForecastQuery │
└──────────────────────────────┘    └──────────────────────────────────┘

┌──────────────────────────────┐    ┌──────────────────────────────────┐
│   OBSERVATION CONTEXT        │    │  NOTIFICATION CONTEXT            │
│                              │    │                                  │
│  Aggregate: Observation      │    │  Entity: Notification            │
│  ValueObj:  GpsCoordinates   │    │  Consumes integration events     │
│  ValueObj:  ExifMetadata     │    │  from other bounded contexts,    │
│  Event:     ObsCreatedEvent  │    │  persists notifications, and     │
│  Command:   CreateObservation│    │  exposes a read/mark-read API.   │
│  Query:     GetGeoJson       │    │                                  │
└──────────────────────────────┘    └──────────────────────────────────┘
```

## Clean Architecture (per service)

```
OMyFish.SpeciesService.Domain/
│   Entities/Species.cs          — Aggregate root, business rules
│   Entities/Prediction.cs       — Entity
│   ValueObjects/ConfidenceScore.cs
│   Events/FishIdentifiedEvent.cs

OMyFish.SpeciesService.Application/
│   Commands/IdentifyFishCommand.cs         — ICommand<T>
│   Commands/IdentifyFishCommandHandler.cs  — ICommandHandler<T>
│   Queries/GetSpeciesQuery.cs
│   Interfaces/IAIServiceClient.cs          — outbound port
│   Interfaces/ISpeciesRepository.cs        — outbound port

OMyFish.SpeciesService.Infrastructure/
│   Persistence/SpeciesDbContext.cs         — EF Core
│   Repositories/SpeciesRepository.cs
│   ExternalServices/AIServiceClient.cs     — HttpClient → Python AI
│   Messaging/RabbitMQPublisher.cs          — MassTransit publish

OMyFish.SpeciesService.Api/
│   Endpoints/IdentificationEndpoints.cs    — Minimal API routes
│   Program.cs
```

## CQRS with MediatR

```
POST /api/v1/species/identify
  → IdentifyFishCommand (ICommand<IdentifyFishResult>)
    → IdentifyFishCommandHandler
      → IAIServiceClient.PredictAsync()
      → ISpeciesRepository.FindByScientificName()
      → Species.IdentifyFrom() [domain logic]
      → IMessagePublisher.Publish(FishIdentifiedEvent)
  ← IdentifyFishResult { predictions, uncertain, imageKey }

GET /api/v1/species/{name}
  → GetSpeciesQuery (IQuery<SpeciesDto>)
    → GetSpeciesQueryHandler
      → ISpeciesRepository.FindByScientificNameAsync()
  ← SpeciesDto
```

## RabbitMQ Async Workflow

```
User uploads photo
      │
      ▼
species-service (HTTP POST /identify)
      │
      ├── calls AI Service → top-K predictions
      │
      ├── publishes: FishIdentifiedEvent
      │   exchange:    omyfish.species  (topic)
      │   routing_key: fish.identified
      │   queues:
      │     fish.identified.obs-svc      ← observation-service
      │     fish.identified.notif-svc    ← notification-service
      │   dlq: fish.identified.dlq
      │
      ▼
notification-service (MassTransit consumer)
  → sends push notification / email to user

User explicitly saves observation:
      │
      ▼
observation-service (HTTP POST /observations)
      │
      ├── creates Observation aggregate (domain event registered)
      │
      ├── publishes: ObservationCreatedEvent
      │   exchange:    omyfish.observations (topic)
      │   routing_key: observation.created
      │   queue: observation.created.notif-svc
      │
      ▼
notification-service → "Your observation was saved!"
```

## PostgreSQL + PostGIS Schema

### species_db (see migrations/SpeciesService/)
```sql
species    (id UUID PK, scientific_name UNIQUE, common_name, family,
            conservation_status, habitat, geographic_range, is_na_freshwater)
predictions(id UUID PK, species_id FK, image_storage_key,
            confidence DOUBLE, rank INT, user_id UUID, predicted_at)
```

### observation_db (see migrations/ObservationService/)
```sql
observations (id UUID PK, user_id UUID, species_name,
              top_confidence DOUBLE,
              location GEOMETRY(Point, 4326),   -- PostGIS
              latitude, longitude,
              notes TEXT, observed_at, created_at)

-- Spatial index:
CREATE INDEX idx_observations_location ON observations USING GIST(location);
```

### GeoJSON Export (PostGIS)
```sql
SELECT json_build_object(
  'type', 'FeatureCollection',
  'features', json_agg(
    json_build_object(
      'type', 'Feature',
      'geometry', ST_AsGeoJSON(location)::json,
      'properties', json_build_object(
        'id', id::text, 'species', species_name,
        'confidence', top_confidence, 'observedAt', observed_at
      )
    )
  )
) FROM observations WHERE location IS NOT NULL;
```

## Object Storage Strategy

| Bucket               | Content                        | Lifecycle            |
|----------------------|--------------------------------|----------------------|
| `omyfish-images`     | Raw uploaded fish photos       | 90-day glacier tier  |
| `omyfish-thumbnails` | 300×300 preview renditions     | Generated on upload  |
| `omyfish-exports`    | GeoJSON / CSV exports          | 7-day TTL            |
| `omyfish-models`     | ONNX model artifacts           | Version-tagged, CDN  |

## API Contracts

### SpeciesService
```
POST /api/v1/species/identify
  Body: multipart/form-data { image: File, topK: int = 5 }
  Returns: { predictions: [...], uncertain: bool, imageKey: string }

GET  /api/v1/species/{scientificName}
GET  /api/v1/species?family=&conservationStatus=&page=0&size=20
GET  /api/v1/species/bite-score/forecast?lat=&lon=&species=general&hours=336   (proxied to ai-service)
GET  /api/v1/species/bite-score/today?lat=&lon=&species=general                (proxied to ai-service)
```

### ObservationService
```
POST   /api/v1/observations
  Body: { speciesName, scientificName?, topConfidence, imageStorageKey, latitude?, longitude?, notes? }
  (imageStorageKey comes from a prior /api/v1/species/identify call — the
  image is already stored, this just references it)
GET    /api/v1/observations?userId=&species=&from=&to=&page=&size=
GET    /api/v1/observations/{id}
GET    /api/v1/observations/geojson?bbox=&from=&to=
DELETE /api/v1/observations/{id}
```

### IdentityService
```
POST /api/v1/auth/register   { email, password, displayName }
POST /api/v1/auth/login      { email, password } → { token, refreshToken, userId, email, role }
POST /api/v1/auth/refresh    { refreshToken }
POST /api/v1/auth/api-keys   → { apiKey }
GET  /api/v1/auth/me
```

### NotificationService
```
GET /api/v1/notifications              → current user's notifications, newest first
PUT /api/v1/notifications/{id}/read    → mark one as read
```

## Security Architecture

```
Client
  │── Bearer JWT (RS256) ──► YARP Gateway
                                  │
                                  ├── validates JWT signature (identity-service public key)
                                  ├── enforces rate limits (per IP + per userId)
                                  ├── injects X-User-Id, X-User-Roles headers
                                  ├── validates API keys (hashed in DB)
                                  │
                                  ▼ forwards to downstream service
                            service receives pre-validated identity headers
                            (no re-validation needed downstream)

OAuth2/OIDC: ASP.NET Core Identity with OpenIddict (Phase 5)
API Keys: SHA-256 hashed, stored in identity DB, checked in gateway middleware
```

## Observability

```
Each .NET service:
  ├── OpenTelemetry SDK → OTEL Collector → Jaeger (distributed traces)
  ├── ASP.NET built-in metrics → Prometheus → Grafana
  └── Serilog structured JSON → stdout → Fluent Bit → Loki / ELK

Key custom metrics:
  - omyfish_fish_identifications_total (counter, labels: species, confidence_band)
  - omyfish_identification_duration_seconds (histogram)
  - omyfish_observations_created_total
  - omyfish_ai_service_requests_total (status: success/error)
  - rabbitmq_messages_published / consumed (MassTransit built-in)
```

## Kubernetes Deployment

```
Namespace: omyfish
┌────────────────────────────────────────────────────────────┐
│  Ingress (nginx) + cert-manager (Let's Encrypt)            │
│    api.omyfish.io ──► api-gateway Service (ClusterIP 8080) │
└────────────────────────────────────────────────────────────┘
│  Deployments (HPA-managed):                                │
│    api-gateway          2–5  replicas  250m CPU  256Mi     │
│    identity-service     2–5  replicas  250m CPU  256Mi     │
│    species-service      2–20 replicas  500m CPU  512Mi     │
│    observation-service  2–10 replicas  250m CPU  256Mi     │
│    notification-service 1–3  replicas  100m CPU  128Mi     │
│    ai-service           1–4  replicas  1 CPU     4Gi       │
│                                                            │
│  StatefulSets: postgres (PostGIS), rabbitmq (3-node raft)  │
│  PVCs: postgres 20Gi, minio 50Gi                           │
│  Secrets: JWT key, DB passwords, S3 keys (via Vault/ESO)  │
└────────────────────────────────────────────────────────────┘
```

## Scaling Strategy

| Users     | species-svc | observation-svc | ai-service | DB            | Notes                            |
|-----------|-------------|-----------------|------------|---------------|----------------------------------|
| 10        | 1 replica   | 1 replica       | 1 replica  | Single node   | Docker Compose, no HPA           |
| 100       | 2 replicas  | 2 replicas      | 1 replica  | Single node   | K8s + HPA, connection pooling    |
| 1,000     | 4 replicas  | 3 replicas      | 2 replicas | Read replica  | PgBouncer, Redis output cache    |
| 100,000   | 20 replicas | 10 replicas     | 4 replicas | Citus/Aurora  | CDN for thumbnails, async writes |

.NET advantages at scale:
- Minimal APIs have lower overhead than MVC controllers
- .NET 10 AOT compilation reduces cold-start latency on K8s scale-up
- Native AOT containers are 40–60% smaller than JIT containers

## Technology Selection Rationale

**.NET 10 + ASP.NET Core Minimal APIs** — .NET 10 LTS brings Native AOT for production, top-tier async I/O via the .NET thread pool, and sub-millisecond API routing with Minimal APIs. ASP.NET Core consistently ranks first in TechEmpower benchmarks for JSON serialization throughput, making it ideal for a high-frequency prediction API.

**YARP (Yet Another Reverse Proxy)** — A .NET-native API gateway that integrates directly into the ASP.NET Core middleware pipeline. Unlike NGINX or Kong, YARP allows gateway logic (auth middleware, rate limiting, circuit breakers) to be written in C#, sharing code with the service layer and tested with standard .NET tooling.

**MediatR (CQRS)** — Decouples HTTP endpoints from business logic. Each use case is a self-contained handler testable in isolation without a running web server. The pipeline behavior feature provides cross-cutting concerns (validation, logging, caching) as composable middleware per command/query.

**Entity Framework Core + Npgsql + NetTopologySuite** — EF Core with the NetTopologySuite plugin maps PostGIS `geometry` columns to typed .NET objects (`Point`, `Polygon`, etc.), enabling type-safe spatial queries. Npgsql is the highest-performance PostgreSQL driver for .NET.

**MassTransit + RabbitMQ Quorum Queues** — MassTransit abstracts messaging behind a consistent API, adding saga support, retry policies, dead-letter queue routing, and outbox pattern support out of the box. Quorum queues replace classic mirrored queues with Raft-based consensus for true HA.

**omyfish-ai (shared AI microservice)** — No .NET AI library matches PyTorch for production computer vision. The AI service lives in its own repo (`../omyfish-ai`) and is shared across omyfish-python, omyfish-dotnet, and omyfish-java. The docker-compose build context points to `../omyfish-ai` so the data science team can iterate on the model independently of the .NET release cycle.

**OpenTelemetry + Prometheus + Grafana + Jaeger** — The CNCF observability stack. OpenTelemetry SDK is now built into .NET 8+ as `System.Diagnostics.Activity`, making it zero-config for traces. Jaeger provides distributed trace correlation across all 5 services with a single trace ID propagated through RabbitMQ message headers.

## Migration Roadmap from Python

### Phase 1 — Foundation (Weeks 1–4)
- Set up .NET 10 solution structure, shared libraries
- Docker Compose with PostgreSQL/PostGIS, RabbitMQ, MinIO
- IdentityService: user registration, JWT issuance
- YARP API Gateway with JWT validation middleware

### Phase 2 — Core Domain (Weeks 5–8)
- SpeciesService with Clean Architecture + CQRS
- HTTP client adapter to existing Python AI service
- FishIdentifiedEvent published to RabbitMQ via MassTransit
- Seed species knowledge base from `fish_info.json`

### Phase 3 — Observations & GIS (Weeks 9–12)
- ObservationService with PostGIS + NetTopologySuite
- EXIF extraction (MetadataExtractor.NET)
- MinIO/S3 image storage adapter
- GeoJSON export endpoint
- Migrate SQLite observations → PostgreSQL

### Phase 4 — Frontend (Weeks 13–16)
- Next.js 15 + TypeScript frontend
- MapLibre GL JS for observation map
- Replace Streamlit UI — ship to production

### Phase 5 — Observability & Security (Weeks 17–20)
- Full OpenTelemetry instrumentation
- Prometheus metrics + Grafana dashboards
- Jaeger distributed tracing
- API key management for external integrations
- OpenIddict for OAuth2/OIDC

### Phase 6 — Kubernetes & Production (Weeks 21–24)
- Helm chart deployment to K8s
- HPA validation under k6 load test
- GitLab CI/CD pipeline
- .NET Native AOT build for production images
- Decommission FastAPI app layer (keep Python AI service)

## Production Readiness Checklist

- [ ] Health checks: `/health/live` and `/health/ready` on all services
- [ ] JWT RS256 (asymmetric) — not HS256 — with key rotation strategy
- [ ] RabbitMQ Quorum queues (not classic) + DLQ for all consumers
- [ ] MassTransit Outbox pattern to prevent dual-write inconsistency
- [ ] EF Core query logging disabled in production (performance)
- [ ] PgBouncer connection pooling in front of PostgreSQL
- [ ] MinIO/S3 lifecycle rules for image archival
- [ ] OpenTelemetry sampling rate configured (10% in prod)
- [ ] .NET Native AOT build tested for all services
- [ ] HPA validated under k6 ramp test (target: 1K RPS on species-service)
- [ ] CORS restricted to known origins
- [ ] Serilog Seq / ELK sink configured for log aggregation
- [ ] Secret rotation documented (JWT keys, DB passwords, S3 keys)
- [ ] Database backup + restore runbook tested
- [ ] Graceful shutdown configured (`UseShutdownTimeout`)

## Cost Evolution

| Stage      | Users   | Monthly Est. | Stack                                                              |
|------------|---------|--------------|--------------------------------------------------------------------|
| MVP        | 10      | ~$40         | Single EC2 t3.medium, Docker Compose, RDS micro                    |
| Growth     | 1,000   | ~$350        | EKS 3-node, RDS db.t3.large, ElastiCache Redis                    |
| Scale      | 10,000  | ~$1,200      | EKS 6-node, RDS db.r6g.large + read replica, CloudFront CDN       |
| Enterprise | 100,000 | ~$5,000      | EKS 20-node, Aurora Serverless v2, WAF, DDoS Shield, multi-AZ     |

Note: .NET containers with Native AOT are ~40% smaller than JVM equivalents,
reducing EKS node count requirements and ECR storage costs at scale.
