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

## [ ] C — Frontend: source of truth for the unified baseline

**Status:** NOT STARTED. This repo's frontend was independently identified as
the more current/complete of the three (AuthContext, namespaced api client,
dedicated /register page, abstracted ObservationMap) — it becomes the baseline
copied into Java and python-web, after two adjustments here:

- `src/lib/api.ts` + `AuthContext.tsx`: revert to `token`/`refreshToken`/
  uppercase role (not `accessToken`/lowercase) per the A1 decision.
- `FishUploader.tsx`: change the save-observation call to send `imageStorageKey`
  in a JSON body instead of building multipart form data, per A1.
- Add the Regs Advisor UI once B lands: chat panel, identify-result info cards,
  zones/stations map layer — then it ships to all three frontends when copied.
