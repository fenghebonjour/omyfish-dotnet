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

## [~] Follow-up — WebApplicationFactory HTTP-level slice tests

**Status:** STARTED 2026-09-11 (BACKLOG.md item F's Testing/CI tier — see
its note for what shipped: ApiGateway auth-enforcement tests via
`WebApplicationFactory<Program>`, and real-Postgres repository integration
tests via `Testcontainers.PostgreSql` for SpeciesService/ObservationService).
Still open: the same treatment for IdentityService/NotificationService, and
endpoint-level (not just repository-level) slice tests for
SpeciesService/ObservationService's own Api projects — those still need
either Testcontainers or per-service MassTransit hosted-service mocking to
avoid a real RabbitMQ dependency, since their endpoints publish through the
outbox. Worth its own session rather than folding in opportunistically.

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
`make build-up` — see its note below. Also landed 2026-09-11: part of the
Testing/CI tier (ApiGateway tests, Species/Observation repository
integration tests) — see its note below for what's still open there. Cleanup
tier done 2026-09-11 (Helm chart templated; the other two cleanup items
turned out already stale). Only the rest of Testing/CI is still open.

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
- ~~ApiGateway has zero tests~~ — fixed 2026-09-11: new
  `tests/OMyFish.ApiGateway.Tests` project, `WebApplicationFactory<Program>`
  against the real HTTP pipeline (not just config parsing) — the same gap as
  the "WebApplicationFactory HTTP-level slice tests" follow-up above, and
  exactly the kind of wiring bug that caused §1.1. Needed `public partial
  class Program;` added to `ApiGateway/Program.cs` (top-level statements
  generate that class `internal` otherwise, invisible cross-assembly). Tests
  cover: default-protected routes return 401 with no token; the same routes
  don't with a valid one; `AuthorizationPolicy: Anonymous` routes aren't
  rejected by auth either way (whatever happens after — a 502 here, since
  the downstream service isn't running in the test host — is a different,
  expected failure mode, not what these tests check).
- ~~No Infrastructure-layer tests anywhere (repositories, `AIServiceClient`,
  publishers)~~ — partially fixed 2026-09-11: real-database integration
  tests added to `OMyFish.SpeciesService.Tests`/`OMyFish.ObservationService.Tests`
  (`PostgresFixture`, `Testcontainers.PostgreSql` on the actual
  `postgis/postgis:16-3.4-alpine` image, migrated with this repo's real raw
  SQL — not EF's model). These specifically close the exact gap that let
  §2.3's `predictions.scientific_name NOT NULL` mismatch through undetected
  — confirmed by temporarily reverting that fix and watching the new test
  fail with the identical `23502` error, then restoring it and watching it
  pass. Extended 2026-09-11 to `OMyFish.IdentityService.Tests` (same
  `PostgresFixture` pattern, migrated with `migrations/IdentityService`) and
  `OMyFish.NotificationService.Tests` — closing the "same treatment for
  IdentityService/NotificationService" half of the
  WebApplicationFactory-slice-tests follow-up above. New coverage: `UserRepository`
  round-trip + the unique email index actually throwing `DbUpdateException`
  on a duplicate (`UserRepositoryIntegrationTests.cs`); `ApiKeyRepository`
  round-trip, the unique `key_hash` index, and the real `ON DELETE CASCADE`
  from `api_keys` to `users` (`ApiKeyRepositoryIntegrationTests.cs`);
  `NotificationDbContext` round-trip and the unique `source_event_id` index
  from §2.4's migration 002 actually being enforced at the DB layer, not
  just by the consumer's own `AnyAsync` check
  (`NotificationDbContextIntegrationTests.cs`, alongside the existing
  InMemory-provider consumer tests, which don't enforce real constraints).
  All 5 test projects pass end-to-end (80 tests total) via `dotnet test
  omyfish-dotnet.slnx`, Docker available this session.

  **`AIServiceClient` — DONE 2026-09-11:** added
  `AIServiceClientTests.cs` to `OMyFish.SpeciesService.Tests`, a
  `FakeHttpMessageHandler`-backed unit test (no Docker/real ai-service
  needed) covering: `PredictAsync`'s JSON-to-`AIPrediction` mapping with
  sequential rank assignment, its two defensive fallbacks (non-success
  status code and a null `predictions` field both return an empty result
  instead of throwing); `GetBiteForecastAsync`'s species-key resolution
  (including the "general" fallback when the lookup returns null) and that
  the six-factor `Breakdown`/`WeightedContribution` dictionaries survive the
  JSON round-trip untouched, per the product invariant documented on
  `BiteForecastDto`; and that `GetBiteForecastAsync`/`AskRegsAsync` throw
  `HttpRequestException` (rather than returning null) on an empty ai-service
  response body, while `GetRegsZonesGeoJsonAsync` falls back to an empty
  dictionary for the same case. 8 new tests, all passing; full solution
  suite re-verified at 88 tests total.

  **Still open:** the `RabbitMQPublisher`s have no tests; endpoint-level
  (not repository-level) slice tests for SpeciesService/ObservationService's
  own Api projects are also still open (see the WebApplicationFactory
  follow-up above — needs Testcontainers or MassTransit hosted-service
  mocking since their endpoints publish through the outbox).
- CI only runs `dotnet test` — no `dotnet format`, no frontend build/lint
  (no `npm test` script exists at all), no image build, no dependency scan.
  `ubuntu-latest` GitHub-hosted runners have Docker preinstalled, so the new
  Testcontainers-based tests need no CI changes to run — not verified against
  actual GitHub Actions in this session, only locally.

**Cleanup (low) — DONE 2026-09-11:**
- ~~`AddOMyFishTelemetry` shared extension is dead code~~ — turned out to be
  stale: `TelemetryExtensions.cs` existed in the initial scaffold commit
  (6c15ce1) but was already removed by the time of this session, with no
  `AddOMyFishTelemetry` references left anywhere in `src/`. No action needed;
  each service's inline OTel setup in `Program.cs` is the current (and only)
  approach.
- ~~Stray `{Consumers}`/`{Endpoints}` scaffold directories~~ — same as above,
  already gone from the initial scaffold commit onward; none found in `src/`.
- ~~Helm chart's `templates/deployment.yaml`/`hpa.yaml`/`Chart.yaml` are
  literal `// placeholder`~~ — fixed: all three authored for real, mirroring
  `infrastructure/kubernetes/services/*.yaml` and `hpa/*.yaml` (same env vars,
  probes, resource shapes, `runAsNonRoot`/`allowPrivilegeEscalation: false`
  security contexts from §1.4) but templatized against `values.yaml` — image
  repo/tag/pullPolicy, `global.imageRegistry` prefix, `global.imagePullSecrets`,
  replica counts, resource requests/limits, and `{{ .Release.Namespace }}`
  instead of the hardcoded `omyfish` namespace. `values.yaml` was also missing
  `image`/`resources` entries for `identityService`/`notificationService`
  despite having `replicaCount` entries for both — filled in to match the raw
  manifests' sizing. `species-service`/`observation-service` Deployments omit
  `replicas` when `autoscaling.enabled` so the HPA (also newly templatized
  from `values.yaml`'s `autoscaling.*` block) owns replica count, matching
  common Helm convention. Verified with `helm lint` (0 failures) and `helm
  template` (all 14 resources — 6 Deployments, 6 Services, 2 HPAs — render as
  valid YAML); not verified against a real cluster (no `helm install` run).
