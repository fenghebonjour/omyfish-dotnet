# OMyFish .NET — Backlog

Deferred ideas and future work. Not committed scope — parking lot for things worth doing.

Cross-repo context lives in the family alignment plan
(`/home/bigblue/.claude/plans/wondrous-shimmying-ripple.md`) — this file tracks
just .NET's slice of it.

---

## [x] A1 — Contract alignment: auth field/role naming + real image storage on identify

**Status:** DONE (2026-07-28, commit bc91d33). All three pieces landed and
verified (41 tests green): token/role rename, real MinIO upload wired into
species-service's identify, and observation-create switched to the JSON
imageStorageKey body. Frontend's api.ts/AuthContext/FishUploader updated to
match. Still blocks Workstream C until B and the other repos' A1 land too.

Family-wide decision: keep Java's field names/casing, adopt .NET's `/api/v1/...`
versioning everywhere (.NET already does this — no route changes needed here).

- Rename `TokenResponse.AccessToken` → `Token` (or add a
  `[JsonPropertyName("token")]` override); change role storage/checks from
  lowercase (`"user"`/`"admin"`) to uppercase (`"USER"`/`"ADMIN"`) in `User.cs`
  and the `RequireRole(...)` policies.
- species-service `IdentificationEndpoints.cs`/`AIServiceClient.cs`: implement
  the real MinIO upload during identify (the code's own flagged "Phase 5" TODO
  — currently returns the base64 blob as `imageStorageKey` instead of a real key).
- observation-service: change `POST /api/v1/observations` from multipart
  `IFormFile` to a JSON body `{speciesName, scientificName, topConfidence,
  imageStorageKey, latitude, longitude, notes}`, matching Java's
  `CreateObservationRequest` — the image is already stored by identify, no
  need to re-upload on observation-create. Drop the direct-upload-to-MinIO call
  from `CreateObservationCommandHandler.cs`.

---

## [x] A2 — Port features Java already has, plus real bugs found

**Status:** MOSTLY DONE (2026-07-28, commit 1366a81). API-key endpoint, CORS
fix, ARCHITECTURE.md doc corrections, BillingService extraction (+15 new
tests), OMyFish.NotificationService.Tests (+2 tests), Docker healthchecks, and
the observation-service HPA manifest all landed. **Not done**: full
`WebApplicationFactory`-based HTTP-level slice tests for auth/observations/
notifications — scoped out because it needs either Testcontainers or careful
per-service MassTransit hosted-service mocking, a bigger separate lift. Left
as a follow-up below.

- API-key issuance: port Java's `AuthController` `POST /api/v1/users/{userId}
  /api-keys` + `CreateApiKeyUseCase` (the `ApiKey` entity already exists here,
  needs a repository + endpoint).
- **Security fix**: gateway CORS policy is currently
  `SetIsOriginAllowed(_ => true).AllowCredentials()` — wildcard origin with
  credentials. Restrict to the known frontend origin (matches Java's
  `application.yml` and this repo's own documented checklist, which this
  contradicts today).
- Fix `ARCHITECTURE.md`'s claim that the gateway injects trusted `X-User-*`
  headers and downstream services skip re-validation — it doesn't; each
  service self-validates the JWT independently. Rather than building real
  gateway-level header trust (a legitimate zero-trust alternative), just
  correct the doc to describe reality.
- Refactor the inline billing/admin logic in `Program.cs` into a testable
  service (mirroring Java's `BillingService`) to create a unit-test seam —
  currently untestable since it's inline minimal-API lambdas.
- Add a `OMyFish.NotificationService.Tests` project (doesn't exist — the only
  service with zero test coverage).
- Add `WebApplicationFactory`-based slice tests for auth/observations/
  notifications (Java has these via `@WebMvcTest`; none exist here).
- Add per-service Docker healthchecks + `depends_on: condition: service_healthy`
  to `docker-compose.yml` (currently only postgres/rabbitmq/ai-service have
  them — none of the 5 .NET app services do, so `docker compose up` can report
  ready before services can actually serve traffic).
- Add `infrastructure/kubernetes/hpa/observation-service-hpa.yaml` (only
  species-service has an HPA manifest today, despite this repo's own
  ARCHITECTURE.md scaling table listing observation-service as autoscaled too).

---

## [x] B — Proxy the Quebec Regs Advisor feature

**Status:** DONE (2026-07-28, commit 16f8ed7). Implemented at
`/api/v1/species/regs/*` — **corrected from this file's original
`/api/v1/regs/*`**: species-service's YARP route only catches
`/api/v1/species/**`, and bite-score is already nested the same way, so this
avoids any gateway config change. Same correction applies to Java's and
python-web's BACKLOG entries.

## [ ] Follow-up — WebApplicationFactory HTTP-level slice tests

**Status:** NOT STARTED. Deferred out of A2 (see above). Needs either
Testcontainers.PostgreSql or per-service MassTransit hosted-service mocking to
avoid hitting real Postgres/RabbitMQ in tests — worth a dedicated session
rather than folding into the family-alignment pass.

---

## [x] C — Frontend: source of truth for the unified baseline

**Status:** DONE (2026-07-29, commits d41bd47 + 7573a5a). The two A1
adjustments (token/role naming, imageStorageKey JSON body) were already in
place from A1 itself, so this was just: add the Regs Advisor UI (new `/regs`
chat page, two info cards on `FishUploader.tsx`, toggle-able zones/stations
overlay on `ObservationMap.tsx`), bump Next.js 15.1.0 → 15.5.22 (fixes a
critical RCE + ~15 other CVEs, done here since this frontend was about to be
copied into two more repos), then sync verbatim into `omyfish-java` and
`omyfish-python-web`. Verified with a real `next build` in all three repos and
a `diff -rq` confirming all three are byte-identical.

**All workstreams for this repo are now complete** (aside from the
WebApplicationFactory slice-test follow-up noted above).

---

## [ ] E — Migrate species catalog persistence to MongoDB

**Status:** NOT STARTED (added 2026-08-19). `omyfish-java` did this first
(commit 36c0200, see its `BACKLOG.md` item E) — species catalog is
read-mostly, flexible-schema reference data with no relational integrity
needs, so it doesn't belong on Postgres. Port the same move here:

- Replace `SpeciesDbContext` (EF Core + Npgsql,
  `OMyFish.SpeciesService.Infrastructure/SpeciesDbContext.cs`) and
  `SpeciesRepository.cs`'s EF-backed implementation of `ISpeciesRepository`
  with a MongoDB.Driver-backed one — keep the same `ISpeciesRepository`
  interface (`OMyFish.SpeciesService.Application/Interfaces/`) so
  application/domain layers don't change.
- `Species` entity (`OMyFish.SpeciesService.Domain/Entities/Species.cs`) has a
  private `Species(Guid id)` constructor already — check whether the
  EF-to-domain mapping today restores the persisted id correctly before
  assuming it's fine; Java's equivalent bug (`toDomain()` minting a fresh
  random id instead of restoring the persisted one) is worth explicitly
  ruling out here, not assumed away.
- Drop the species-service EF Core migration(s) for Postgres; add a `mongodb`
  service to `docker-compose.yml` (mirror Java's: `mongo:7` image, root
  user/pass env vars, healthcheck via `mongosh --eval`).
- Remove `Npgsql`/EF-Postgres package refs from
  `OMyFish.SpeciesService.Infrastructure.csproj`, add `MongoDB.Driver`.
- Verify with this repo's test suite plus an end-to-end `docker compose up
  --build` check, same as Java's verification pass.

---

## [~] F — Weakness audit follow-up (security, resilience, data consistency)

**Status:** IN PROGRESS (added 2026-09-10). Findings from a senior-dev-style
codebase audit; full explanation + fix snippets in `docs/WEAKNESS_AUDIT.md`.
Grouped by priority. Landed 2026-09-10: quick/low-risk tier (commit d06c4db),
security tier (commit cfb942e), most of the resilience tier (idempotency +
quorum queues, commit 01ac2b6), and §3.1/§3.2/§3.4/§3.3 of the data layer
tier. Landed and verified 2026-09-11: the outbox pattern (§2.3), via a real
`make build-up` — see its note below. Testing/CI and cleanup are still open.

**Security (critical) — DONE 2026-09-10:**
- ~~Gateway configures JWT auth but never calls `.RequireAuthorization()` on
  `MapReverseProxy()`~~ — fixed: `MapReverseProxy().RequireAuthorization()` is
  now the default, with `"AuthorizationPolicy": "Anonymous"` opt-outs in
  `appsettings.json` for the routes that are genuinely public today
  (identity auth routes, the whole species surface, observations geojson).
  (`WEAKNESS_AUDIT.md` §1.1)
- ~~`POST /api/v1/species/identify` and the bite-score endpoints are
  `.AllowAnonymous()` with no rate limiting~~ — fixed: per-IP fixed-window
  rate limiting added (`identify`: 10/min, `bite-score`: 30/min). (§1.2)
- ~~Refresh tokens (30-day) stored in `localStorage`~~ — fixed: refresh token
  now travels only as an httpOnly, SameSite=Strict cookie scoped to
  `/api/v1/auth`; added `POST /api/v1/auth/logout` to clear it. Gateway CORS
  now allows credentials (still restricted to the exact configured frontend
  origin, never a wildcard). (§1.3)
- ~~No `USER` directive in any of the 5 .NET Dockerfiles; no
  `securityContext`/`runAsNonRoot` in K8s~~ — fixed: all 5 Dockerfiles run as
  `USER $APP_UID`; matching `securityContext.runAsNonRoot`/
  `allowPrivilegeEscalation: false` added to the 5 K8s deployments. **Verified
  2026-09-10 via `make build-up`** (user-run, outside this environment — no
  Docker access here). Helm chart still not addressed (its deployment
  template is a placeholder — see Cleanup below). (§1.4)

**Resilience (high):**
- ~~No Polly/timeout on `AIServiceClient`~~ — fixed in the quick-win tier
  (commit d06c4db): 15s `HttpClient.Timeout`. (§2.1)
- ~~No global exception handler (`IExceptionHandler`) in any Api project~~ —
  fixed in the quick-win tier (commit d06c4db): added to all 5 Api projects. (§2.2)
- ~~Dual-write without an outbox: DB save + event publish are separate calls
  in `CreateObservationCommandHandler`/`IdentifyFishCommandHandler` — a crash
  between them silently drops the event~~ — fixed 2026-09-11: MassTransit's
  EF Core transactional outbox (`AddEntityFrameworkOutbox<TDbContext>(o =>
  { o.UsePostgres(); o.UseBusOutbox(); })`) on both services. Each
  repository's `AddAsync` now only stages the entity; a new
  `IUnitOfWork.SaveChangesAsync(ct)` (one per service) is what the handler
  calls last, after publishing via `IPublishEndpoint` — that's the
  "`IUnitOfWork` seam through the repository layer" this item called for.
  Outbox tables (`InboxState`/`OutboxMessage`/`OutboxState`) are suffixed
  `_species`/`_observation` since both services share one physical Postgres
  database — same reason as `schemaversions_<service>` in §3.1. Their raw
  SQL (`migrations/SpeciesService/002_add_outbox.sql`,
  `migrations/ObservationService/004_add_outbox.sql`) was generated via
  `dotnet ef migrations script` against
  `MassTransit.EntityFrameworkCore` 8.3.7 rather than hand-derived, then
  adapted to this repo's idempotent-migration convention.
  **Also found and fixed while wiring this up:** `IdentifyFishCommandHandler`
  had no DB write at all to protect — species the AI service identified
  outside the catalog were built in memory but never persisted (confirmed via
  `git show d06c4db` that this predates that commit's N+1 fix, not caused by
  it). The top prediction (and its species, if new) is now staged and
  persisted alongside the event publish, so `predictions`/`species` (both
  previously dead) are populated and the outbox has a real write to be atomic
  with.

  **Verified 2026-09-11 end-to-end via `make build-up`** (Docker was
  available this session): rebuilt all 5 images, confirmed both new
  migrations (`Migrations.002_add_outbox.sql` species,
  `Migrations.004_add_outbox.sql` observation) applied cleanly, then drove
  real traffic through the gateway — `POST /api/v1/species/identify` with an
  actual fish photo, and `POST /api/v1/observations` — and confirmed both:
  the outbox tables briefly held the message then drained to 0 once
  delivered, `FishIdentifiedConsumer` logged the identify event, and a real
  `notifications` row appeared for the observation-create (`type
  OBSERVATION_CREATED`, correct `user_id`).

  **Second bug found and fixed during this verification** (a plain photo of
  a fish surfaced it immediately, not an edge case): `POST /identify` 500'd
  with `23502: null value in column "scientific_name" of relation
  "predictions"`. The `predictions` table (from
  `001_initial_species_schema.sql`) has always had a `scientific_name NOT
  NULL` column, but the EF `Prediction` entity/mapping never included it —
  invisible until this session made the very first `Predictions.Add(...)`
  call this table has ever seen. Fixed by adding `Prediction.ScientificName`
  (set from `species.ScientificName` in `Prediction.Create`) and mapping it
  in `SpeciesDbContext`. Re-verified clean after the fix. (§2.3)
- ~~No idempotency in NotificationService consumers~~ — fixed and verified
  2026-09-10 end-to-end (real observation → real notification, post-`make
  build-up`): `Notification` now has a unique `SourceEventId` (the
  publisher's MassTransit `MessageId`); `ObservationCreatedConsumer` skips on
  a redelivered id instead of inserting a duplicate. Migration:
  `migrations/NotificationService/002_add_source_event_id.sql`. **Hit the
  exact §3.1 schema-drift failure mode during verification** — see the
  `EnsureCreatedAsync` note below the Data layer section. (§2.4)
- ~~DLQ/quorum-queue setup was documented in `CLAUDE.md` but not implemented~~
  — `CLAUDE.md` corrected instead of chasing an unverified custom `.dlq`
  suffix: NotificationService's two receive endpoints now use
  `e.SetQuorumQueue()`; failed messages land in MassTransit's own default
  `<queue>_error` fault queue, which is what the retry policy already assumed.
  Retry policy still only exists on the consume side, not the publish side —
  that's inherent to MassTransit's retry model (it retries message
  *processing*, publish failures are a connection-resilience concern instead)
  so no further action needed there.

**Data layer (high):**
- ~~`EnsureCreatedAsync()` on every service startup competed with the
  raw-SQL migrations as a second, divergent schema source~~ — **fixed and
  VERIFIED 2026-09-10 against a real `make build-up` on the existing
  (non-empty) dev Postgres.** Concretely bit us in practice first: adding
  `Notification.SourceEventId` (§2.4) had no effect on the already-existing
  `notifications` table after `make build-up` on top of old Postgres data —
  `EnsureCreatedAsync` silently no-oped, every notification read/write threw
  `42703: column n.source_event_id does not exist`, and the triggering event
  quietly died in MassTransit's fault queue with no user-visible error (fixed
  that one instance by hand-applying the `ALTER TABLE` via
  `make shell-postgres`). Real fix: all 4 Api projects now embed their own
  `migrations/<Service>/*.sql` as build-time resources and apply them via
  `DbUp` (`dbup-postgresql` package) on startup instead of
  `EnsureCreatedAsync`, failing fast if a migration fails. Also made the
  species/observation `001_*.sql` files idempotent-safe (`CREATE TABLE/INDEX
  IF NOT EXISTS`, `DROP TRIGGER IF EXISTS` + `CREATE TRIGGER`) since they
  weren't before — needed for DbUp's first-ever run against the existing
  dev DB, which already had these tables from prior `EnsureCreatedAsync` runs.
  First-round verification (2026-09-10) surfaced two further bugs in the
  original fix itself, both now fixed:
  1. The 4 services' Dockerfiles build with `context: ./src` in
     `docker-compose.yml`, but `migrations/` lives one level above `src/` at
     the repo root — the csproj's `EmbeddedResource` glob
     (`..\..\..\..\migrations\<Service>\*.sql`, relative to the csproj) walked
     past the copied build context and silently matched zero files inside
     Docker, so DbUp found no scripts and no-oped exactly like the old
     `EnsureCreatedAsync` bug this was meant to fix. `dotnet build`/`dotnet
     test` never caught it because those run from the full repo checkout, not
     the Docker build context. Fixed by pointing all 4 services' `context` at
     the repo root (`.`) with `dockerfile: src/services/...` paths, and
     prefixing `src/` onto every `COPY`/`RUN dotnet` path in their
     Dockerfiles; added a root `.dockerignore` (`.git`, frontend
     `node_modules`/`.next`, `**/bin`, `**/obj`) to keep the larger build
     context lean.
  2. All 4 services connect to the same physical `omyfish` database and none
     customized DbUp's journal table, so they raced to create the same
     default `schemaversions` table on concurrent startup — the loser threw
     `23505: duplicate key value violates unique constraint
     "pg_class_relname_nsp_index"` and crashed. This only surfaced once fix
     #1 made DbUp actually try to create the table. Fixed by giving each
     service its own journal table via
     `.JournalToPostgresqlTable("public", "schemaversions_<service>")`.
  **Known limitation, not solved here**: no distributed lock across replicas
  of the *same* service — deployments with `replicas: 2` for one service
  could still run DbUp concurrently on startup. Same risk profile as the old
  `EnsureCreatedAsync` behavior (not worse), just flagging it's not a
  complete fix for multi-replica prod. Migration files, once applied
  anywhere by DbUp, must never be edited again — add a new numbered file
  instead (documented in `CLAUDE.md`). (§3.1)
- ~~`make migrate` never applies
  `migrations/IdentityService/002_add_subscriptions.sql`~~ — fixed in the
  quick-win tier (commit d06c4db). (§3.2)
- ~~PostGIS `location` column/GIST index/`observations_within_radius()`
  function are all dead — `ObservationDbContext` ignores `Location`, so
  coordinates only ever live in plain lat/lon columns~~ — fixed 2026-09-10.
  Rather than wiring NetTopologySuite through EF (which would mean every
  future write path, including any future raw-SQL fix, has to remember to
  keep `location` in sync with `latitude`/`longitude`), migration
  `003_backfill_location.sql` adds a `BEFORE INSERT OR UPDATE` trigger that
  derives `location` from `latitude`/`longitude` at the DB layer — the
  single source of truth stays the two scalar columns the app already reads
  and writes, and `location` can't drift out of sync regardless of what
  writes the row. The same migration backfills existing rows. To actually
  exercise the previously-dead GIST index and `observations_within_radius()`
  function instead of just populating a column nothing reads, added
  `GET /api/v1/observations/nearby?lat=&lon=&radiusKm=` (public, like
  `/geojson`) — calls the SQL function via `SqlQueryRaw`. (§3.3)
- ~~N+1 query in `IdentifyFishCommandHandler`~~ — fixed in the quick-win tier
  (commit d06c4db): batched species lookup instead of one query per AI
  prediction. (§3.4)

**Testing/CI (medium):**
- ApiGateway has zero tests; no Infrastructure-layer tests anywhere
  (repositories, `AIServiceClient`, publishers); no HTTP-level endpoint
  tests — this is the same gap as the existing "WebApplicationFactory
  HTTP-level slice tests" follow-up above, not a new item, just re-flagged
  because it's exactly what would have caught §1.1/§1.2.
- CI only runs `dotnet test` — no `dotnet format`, no frontend build/lint
  (no `npm test` script exists at all), no image build, no dependency scan.

**Cleanup (low):**
- `AddOMyFishTelemetry` shared extension is dead code — never called, every
  service copy-pastes the same OTel setup inline instead.
- Stray `{Consumers}`/`{Endpoints}` scaffold directories (literal braces in
  the name) in NotificationService, ObservationService.Api, SpeciesService.Api.
- Helm chart's `templates/deployment.yaml`/`hpa.yaml`/`Chart.yaml` are
  literal `// placeholder` — `helm install` deploys nothing despite a
  fully-authored `values.yaml`.
