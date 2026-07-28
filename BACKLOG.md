# OMyFish .NET — Backlog

Deferred ideas and future work. Not committed scope — parking lot for things worth doing.

Cross-repo context lives in the family alignment plan
(`/home/bigblue/.claude/plans/wondrous-shimmying-ripple.md`) — this file tracks
just .NET's slice of it.

---

## [ ] A1 — Contract alignment: auth field/role naming + real image storage on identify

**Status:** NOT STARTED. Blocks the frontend unification work (Workstream C).

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

## [ ] A2 — Port features Java already has, plus real bugs found

**Status:** NOT STARTED.

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

## [ ] B — Proxy the Quebec Regs Advisor feature

**Status:** NOT STARTED. Depends on nothing here (routes already versioned).

All chatbot/retrieval logic lives in `omyfish-ai` (frozen) at `/regs/*`
(`GET /limits`, `GET /zones/geojson`, `GET /consumption/stations`,
`GET /consumption`, `POST /ask`). Add a thin proxy in SpeciesService —
`RegsEndpoints.cs` under `/api/v1/regs/*` — registered in the YARP gateway
route config.

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
