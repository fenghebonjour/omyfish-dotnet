# OMyFish .NET
Startup Strategies & Architecture Map
github.com/fenghebonjour/omyfish-dotnet
.NET 10  ·  Clean Architecture  ·  CQRS  ·  MediatR  ·  RabbitMQ  ·  PostGIS  ·  Next.js  ·  omyfish-ai

## Project Family
AI inference is no longer bundled inside this repo. It now lives in a standalone, reusable microservice shared across all three OMyFish stacks.

| Repo | Role |
| --- | --- |
| omyfish-python | Python origin -- Streamlit + FastAPI, deployed on HuggingFace Spaces |
| omyfish-dotnet | .NET 10 enterprise rewrite -- Clean Architecture + CQRS (this repo) |
| omyfish-java | Java 21 enterprise rewrite -- Hexagonal Architecture + Event-Driven |
| omyfish-ai | Shared AI microservice (fish ID + Bite Score forecast) -- used by all three above |

> **Best for The internal AI projection (formerly inside this repo) has been replaced. SpeciesService now calls the shared omyfish-ai repository over HTTP, mounted with read-only checkpoints from omyfish-python.**

## Three Ways to Run the Stack

|  | Visual Studio | Docker Compose | Hybrid |
| --- | --- | --- | --- |
| Debugger | Full (F5) | None (logs only) | Full on 1 service |
| Setup effort | High | Low | Medium |
| Code change speed | Instant | Rebuild required | Instant on local svc |
| Full stack | Partial (infra Docker) | Yes | Yes (Docker) |
| AI service | Needs omyfish-ai running locally | External shared instance (start omyfish-ai first) | Shared instance in Docker (via omyfish-ai) |
| Frontend | Separate terminal | Included |  |
| Best for | Day-to-day dev | Demo / smoke test | Learning layers |

## Option 1 — Visual Studio (Full Local Debug)

> `Most complex setup`  ·  `Full VS debugger`  ·  `Infra needs Docker`

### Prerequisites
Visual Studio 2022 v17.8+ with .NET 10 workload
Docker Desktop (for infra containers and for the standalone omyfish-ai service)
Node 20 (for the Next.js frontend)
The omyfish-ai and omyfish-python repos cloned as sibling directories next to omyfish-dotnet
Confirm: dotnet --version, node --version

### Clone the sibling repos first

> **cd ~/ git clone https://github.com/fenghebonjour/omyfish-dotnet git clone https://github.com/fenghebonjour/omyfish-ai git clone https://github.com/fenghebonjour/omyfish-python # omyfish-ai expects checkpoints/metadata mounted from omyfish-python: # ../omyfish-python/checkpoints/best.pt # ../omyfish-python/data/metadata/fish_info.json**

### Step-by-step
### Start infrastructure in Docker (not app services)
The .NET services run locally but need Postgres, RabbitMQ, MinIO, and Jaeger. Bring up only infra:

> **docker compose up -d postgres rabbitmq minio jaeger**

### Start the shared AI service
Run omyfish-ai standalone -- this is no longer built inside omyfish-dotnet. It serves both fish ID (POST /predict) and the Bite Score forecast (GET /bite-score/*, its bite_prediction module) that the frontend /timing page renders via SpeciesService.

> **cd ../omyfish-ai docker compose up # Service runs on http://localhost:8000**

### Open the solution
Open omyfish-dotnet.slnx in Visual Studio. Solution Explorer shows all projects under src/services/: ApiGateway, IdentityService, SpeciesService, ObservationService, NotificationService.
### Configure Multiple Startup Projects
Right-click solution -> Properties -> Startup Project -> Multiple startup projects. Set all 5 .Api projects to Start. ApiGateway should start last (it routes to the others).
### Set connection strings in launchSettings.json
There are no appsettings.json files -- each Program.cs reads config straight from IConfiguration, with Docker-only fallbacks (Host=rabbitmq, http://ai-service:8000). So for a local F5 run the environmentVariables block in each service's Properties/launchSettings.json is the only source of these values. Keys use the double-underscore form (ConnectionStrings__Default maps to GetConnectionString("Default")); point every host at localhost, since infra runs in Docker with published ports. Only the three data services have a DB connection string, and Jwt__Secret must be identical across all of them (>=32 chars). The generated launchSettings assign random high ports -- set each applicationUrl to the fixed :8081/:8082/:8083 (and gateway :8080) scheme used throughout this guide if you want ports to match.

IdentityService -- DB + JWT:

> **"ConnectionStrings__Default": "Host=localhost;Database=omyfish;Username=omyfish;Password=omyfish_dev", "Jwt__Secret": "dev-secret-change-in-production-min-32-chars"**

SpeciesService -- DB + JWT + AI + RabbitMQ (+ optional seed):

> **"ConnectionStrings__Default": "Host=localhost;Database=omyfish;Username=omyfish;Password=omyfish_dev", "Jwt__Secret": "dev-secret-change-in-production-min-32-chars", "AIService__BaseUrl": "http://localhost:8000", "RabbitMQ__Host": "localhost", "Seeding__MetadataPath": "../omyfish-python/data/metadata/fish_info.json"**

ObservationService -- DB + JWT + MinIO + RabbitMQ:

> **"ConnectionStrings__Default": "Host=localhost;Database=omyfish;Username=omyfish;Password=omyfish_dev", "Jwt__Secret": "dev-secret-change-in-production-min-32-chars", "RabbitMQ__Host": "localhost", "MinIO__Endpoint": "localhost:9000", "MinIO__AccessKey": "omyfish", "MinIO__SecretKey": "omyfish_dev"**

NotificationService (worker, no DB) -- RabbitMQ only (username/password default to guest/guest):

> **"RabbitMQ__Host": "localhost"**

ApiGateway has no connection string -- it just needs the same Jwt__Secret as the others so tokens validate across services.

### Run migrations (once)
Run from each service that has EF migrations (IdentityService, SpeciesService, ObservationService):

> **dotnet ef database update --project src/services/SpeciesService/... # or use the Makefile: make migrate**

### Press F5 -- debugger attached to all services
Each service opens its own console window. Breakpoints work across all projects simultaneously. ApiGateway listens on :8080 and routes by path prefix to IdentityService (:8081), SpeciesService (:8082), and ObservationService (:8083). NotificationService (:8084) is a background worker with no gateway route.

> **Gateway routing gotcha (local F5) The ApiGateway's default ReverseProxy cluster addresses in appsettings.json point at Docker hostnames (http://identity-service:8080, http://species-service:8080, http://observation-service:8080), which do not resolve on the host. To route through the gateway locally, override each cluster address to the service's localhost port via launchSettings -- e.g. "ReverseProxy__Clusters__species-cluster__Destinations__primary__Address": "http://localhost:8082" -- or simply hit each service directly on its own port and skip the gateway. If you only need the gateway for one downstream service, the Hybrid option (run the gateway in Docker, debug one service locally) avoids this entirely.**

> **WSL-path gotcha (Visual Studio on Windows) If the repo lives on the WSL filesystem (\\wsl.localhost\...) but you F5 from Visual Studio on Windows, the services often start as processes but hang before Kestrel binds -- nothing ends up listening on :8081-:8083, and the frontend shows "Failed to fetch". The config file-watcher also thrashes across the UNC boundary (endless "Loading proxy data from config" on the gateway). The same projects run fine from the CLI: dotnet run --project <svc> --launch-profile <name> (add DOTNET_hostBuilder__reloadConfigOnChange=false to silence the reload thrash), which binds the ports correctly but gives no breakpoints. For breakpoint debugging with the repo in WSL, run the debugger inside WSL -- VS Code + the WSL extension, or JetBrains Rider -- so the .NET process runs next to the code instead of over the UNC share.**
### Run the frontend separately
The Next.js frontend is not part of the .slnx. Open a separate terminal:

> **cd frontend/omyfish-web npm install npm run dev        # -> http://localhost:3000**

Set NEXT_PUBLIC_API_URL=http://localhost:8080 in frontend/.env.local

| Advantages ✓  Full VS debugger: breakpoints, hot reload, call stack ✓  IntelliSense across all Clean Architecture layers ✓  Edit & Continue on .NET code ✓  omyfish-ai runs once and is reused by every service that calls it | Drawbacks ✗  Manual launchSettings.json config per service ✗  Frontend always needs a separate terminal ✗  Requires cloning 2 extra sibling repos (omyfish-ai, omyfish-python) ✗  Infra and AI service both require Docker running |
| --- | --- |

> **Best for Deep .NET debugging -- stepping through CQRS handlers, MediatR pipelines, EF Core queries, and the HttpClient call into omyfish-ai. The right mode for day-to-day development once you are onboarded.**

## Option 2 -- Docker Compose (Full Stack)

> `Easiest to start`  ·  `No debugger`  ·  `Closest to prod`

### Full stack in 5 commands
### Clone all 3 sibling repos at the same directory level
docker-compose.yml references omyfish-ai and omyfish-python as relative build contexts and volume mounts, so directory layout matters.

> **mkdir omyfish-platform && cd omyfish-platform git clone https://github.com/fenghebonjour/omyfish-dotnet git clone https://github.com/fenghebonjour/omyfish-ai git clone https://github.com/fenghebonjour/omyfish-python cd omyfish-dotnet**

### Confirm AI checkpoints exist
omyfish-ai mounts these read-only volumes from omyfish-python -- without them it falls back to stub predictions with uncertain: true.

> **ls ../omyfish-python/checkpoints/best.pt ls ../omyfish-python/data/metadata/fish_info.json**

### Start the shared AI service first
The AI service is no longer built by this repo's compose. Bring up omyfish-ai from its own compose -- this creates the external omyfish-shared network that species-service attaches to, and publishes the model on http://ai-service:8000.

> **cd ../omyfish-ai && docker compose up -d cd ../omyfish-dotnet**

### Build and start everything
First build downloads base images, restores NuGet packages, and builds the .NET service images (3-5 min). Subsequent runs are fast. This does not build or start the AI service -- species-service reaches the shared instance over the omyfish-shared network.

> **make up      # docker compose up -d make build   # dotnet build inside containers**

### Run migrations and create object storage buckets

> **make migrate make minio-create-buckets**

### Everything is up
Frontend on :3000, ApiGateway on :8080, AI service on :8000 (from omyfish-ai's compose), RabbitMQ UI on :15672, MinIO on :9001, Jaeger on :16686.

### How the AI service is wired in docker-compose.yml
species-service joins the external omyfish-shared network (created by ../omyfish-ai) and calls the shared instance at http://ai-service:8000. The bundled ai-service definition is kept behind the `bundled` profile for self-contained demos -- it is not started by default.

> **networks: omyfish-net: driver: bridge omyfish-shared:            # created by ../omyfish-ai's compose external: true ... species-service: environment: AIService__BaseUrl: "http://ai-service:8000" networks: [omyfish-net, omyfish-shared] ... # Bundled fallback -- only with --profile bundled: ai-service: profiles: [bundled] build: context: ../omyfish-ai dockerfile: Dockerfile volumes: - ../omyfish-python/checkpoints:/checkpoints:ro - ../omyfish-python/data/metadata:/metadata:ro ports: - "8000:8000"**

### Useful commands while running

> **docker compose logs -f species-service docker compose restart species-service          # reload after change docker compose down                              # stop everything (leaves omyfish-ai running) # Pick up code changes: docker compose build species-service docker compose up -d species-service # Tail / rebuild the shared AI service from its own compose: docker compose -f ../omyfish-ai/docker-compose.yml logs -f ai-service docker compose -f ../omyfish-ai/docker-compose.yml up -d --build # Self-contained demo without the external instance (host :8000 -- run only one): docker compose --profile bundled up -d ai-service**

| Advantages ✓  One command starts the complete .NET stack, wired to the shared AI service over omyfish-shared ✓  Frontend and observability all included ✓  Service networking matches production environment (shared AI reused across stacks) ✓  RabbitMQ event flows visible in management UI | Drawbacks ✗  No breakpoints -- must use logs and Jaeger traces ✗  Container rebuild required after every code change ✗  Directory layout must match (3 sibling repos) ✗  Must start ../omyfish-ai first so the omyfish-shared network exists ✗  Slower inner dev loop compared to local run |
| --- | --- |

> **Best for Demo and smoke testing the full system, CI verification, or first-look exploration before configuring Visual Studio. Also the right mode for observing distributed traces in Jaeger.**

## Option 3 -- Hybrid (Recommended for Learning)

> `Best for learning`  ·  `Debugger + live infra`  ·  `Recommended`

Run all infrastructure and supporting services in Docker, then launch only the service you are studying locally with a debugger. This gives you a live system while being able to step through the exact layer you are learning.
### Setup workflow
### Start everything in Docker
Bring up the shared AI service from its own compose first (creates the omyfish-shared network), then start this repo's stack: Postgres, RabbitMQ, MinIO, Jaeger, and all the .NET services running end-to-end.

> **cd ../omyfish-ai && docker compose up -d cd ../omyfish-dotnet && make up**

### Stop only the service you want to debug
All other services keep running. The ApiGateway will 502 on species routes temporarily -- that is fine.

> **docker compose stop species-service**

### Run that service locally via VS or CLI
In VS: set SpeciesService.Api as single startup project and press F5. Make sure AiService__BaseUrl=http://localhost:8000 is set. Or from terminal:

> **cd src/services/SpeciesService/OMyFish.SpeciesService.Api dotnet run**

### Set breakpoints and trace a real request
Hit the frontend at :3000 or the gateway directly. The request flows through: Browser -> ApiGateway (:8080) -> your local SpeciesService (:8082) -> omyfish-ai (:8000, in Docker) -> Postgres. You will hit breakpoints inside the CQRS handlers.
### Swap services as you learn each layer
When done with SpeciesService: docker compose start species-service, then stop ObservationService and debug that next. The AI service stays running in Docker throughout -- you rarely need to debug it directly since it's shared infrastructure.

> **docker compose start species-service docker compose stop observation-service**

### Entry point trace through SpeciesService
Follow this path top-to-bottom to understand Clean Architecture in one request, including the call out to the shared AI service:

> **Program.cs                           <- WebApplication builder, DI setup -> SpeciesEndpoints.MapRoutes()    <- Minimal API route registration -> IMediator.Send(command)       <- MediatR dispatches to handler -> IdentifySpeciesHandler      <- Application layer (CQRS command) -> IAiServiceClient (port)   <- Infrastructure interface -> AiServiceClient.cs      <- HttpClient -> omyfish-ai :8000 POST /predict           <- { image_base64, top_k } -> ISpeciesRepository        <- Domain interface -> SpeciesRepository.cs    <- Infrastructure (EF Core + Npgsql) -> PostgreSQL            <- PostGIS spatial data**

Tip: Set the first breakpoint inside the command handler -- that is where the CQRS pattern lives. Step into AiServiceClient to see the image get base64-encoded before the HttpClient POST to omyfish-ai.

| Advantages ✓  Real system context -- actual DB, live RabbitMQ messages, real AI predictions ✓  Full VS debugger on exactly the service you are studying ✓  Jaeger shows distributed traces spanning all services including the AI call ✓  Swap which service is local at any time | Drawbacks ✗  Initial Docker startup takes a few minutes ✗  Port conflicts if you forget to stop the container first ✗  Need to manage two terminals: Docker + VS/CLI |
| --- | --- |

> **Best for Learning the architecture systematically -- trace a request from the gateway through CQRS -> MediatR -> EF Core -> Postgres, and out to the shared omyfish-ai microservice, with full debugger visibility, while the rest of the real system runs around it.**

## Architecture Map
### Request flow

> **Browser / Next.js Next.js 15 - TypeScript - MapLibre GL :3000**

|  HTTP  |

> **API Gateway (YARP) Reverse proxy - JWT validation - routing :8080**

|  routes by path prefix  |

| IdentityService JWT - OAuth2 - API keys :8081 | SpeciesService CQRS - MediatR - AI orchestration :8082 | ObservationService PostGIS - EXIF - MinIO :8083 |
| --- | --- | --- |

|  RabbitMQ events (MassTransit)  |  HTTP for AI  |

| NotificationService MassTransit consumer - Worker :8084 | omyfish-ai (shared) POST /predict - EfficientNet / CLIP :8000 |
| --- | --- |

|  persists to  |  reads checkpoints from  |

| PostgreSQL + PostGIS Shared DB - one schema/service :5432 | MinIO Object store (fish images) :9000 / :9001 | omyfish-python checkpoints/best.pt - fish_info.json (volume mount) |
| --- | --- | --- |

### Shared AI service -- API contract (omyfish-ai)
All three OMyFish stacks call the same JSON-over-HTTP contract. Note this is JSON with a base64 image, not multipart form data.

| Method | Endpoint | Description |
| --- | --- | --- |
| POST | /predict | Body: { image_base64, top_k }. Returns top-K species predictions. |
| GET | /health | Returns { status, model_loaded } |
| GET | /species | Returns the full list of supported species keys |

### Example request / response

> **POST http://localhost:8000/predict Content-Type: application/json { "image_base64": "<base64-encoded image>", "top_k": 5 } -> { "predictions": [ { "scientific_name": "Micropterus salmoides", "common_name": "Largemouth Bass", "confidence": 0.91, "rank": 1 }, ... ], "uncertain": false }**

> **Best for If no trained checkpoint is mounted, omyfish-ai returns hardcoded stub predictions with uncertain: true -- this is the cause of identical predictions seen during early local testing before checkpoints/best.pt was wired up from omyfish-python.**

> **Note If the shared AI service is unreachable or still warming up, SpeciesService's identify endpoint returns 503 (ProblemDetails: "AI service unavailable") instead of an unhandled 500. Start ../omyfish-ai and give it a moment to load the checkpoint, then retry.**

### omyfish-ai environment variables

| Variable | Default | Description |
| --- | --- | --- |
| MODEL_PATH | /checkpoints/best.pt | Path to the EfficientNet checkpoint |
| CLASSES_PATH | /checkpoints/classes.json | Path to the class list |
| METADATA_PATH | /metadata/fish_info.json | Path to species metadata |

### Clean Architecture inside each .NET service

| ...Api | Minimal API endpoints — HTTP surface |
| --- | --- |
| ...Application | CQRS handlers, MediatR, use cases |
| ...Domain | Entities, AggregateRoot, domain events |
| ...Infrastructure | EF Core, Npgsql, repositories |
| ...Shared.BuildingBlocks | CQRS interfaces shared across services |
| ...Shared.Contracts | Integration events (RabbitMQ messages) |

### Observability stack (always in Docker)

| Tool | Purpose | Port |
| --- | --- | --- |
| Jaeger | Distributed traces (OpenTelemetry) | :16686 |
| Prometheus | Metrics scraping | :9090 |
| Grafana | Dashboards (admin / admin) | :3001 |
| RabbitMQ UI | Message queue browser | :15672 |
| MinIO UI | Object storage browser | :9001 |
