# OMyFish .NET — Weakness Audit (learning notes)

Snapshot from a senior-dev-style review on 2026-09-10. Each entry explains
**what's wrong**, **why it matters**, and **how to fix it** with a concrete
snippet. Tracked for real work in `BACKLOG.md` item F — this file is the
"why", that file is the "what to do".

---

## 1. Security

### 1.1 Gateway sets up JWT auth but never enforces it

**Problem:** `ApiGateway/Program.cs` configures `AddAuthentication`/
`AddAuthorization` and calls `UseAuthentication()`/`UseAuthorization()`, but
`app.MapReverseProxy()` (line 63) has no `.RequireAuthorization()`. Auth
enforcement is entirely delegated to each downstream service.

**Why it matters:** the gateway is the one place you'd expect a "deny by
default" posture. Today, a new route added to YARP config with a downstream
service that forgets `.RequireAuthorization()` on its own group is silently
unauthenticated — there's no second layer of defense. Zero-trust between
gateway and services is fine as a design (each service does validate its own
JWT), but "gateway does nothing" isn't zero-trust, it's just "hope every
service remembers."

**Fix:**
```csharp
// ApiGateway/Program.cs
app.MapReverseProxy()
    .RequireAuthorization(); // deny by default; opt individual routes out below
```
Routes that must stay public (health checks, and intentionally-anonymous
endpoints like `/identify`) need an explicit YARP route-level
`AuthorizationPolicy: "Anonymous"` metadata or a separate `MapReverseProxy`
call scoped to just those clusters — don't silently rely on downstream
`.AllowAnonymous()` alone once the gateway enforces by default.

### 1.2 Paid AI endpoints are anonymous + unrate-limited

**Problem:** `POST /api/v1/species/identify` (`IdentificationEndpoints.cs:39`)
and the bite-score endpoints are `.AllowAnonymous()`. There is no rate
limiter anywhere in the codebase (`grep -rn "AddRateLimiter" src/` → nothing).

**Why it matters:** these endpoints drive cost on an external AI service.
Anonymous + unlimited means anyone can script unbounded calls against your
paid AI backend with no auth, no quota, no cost attribution. This is a
direct line to an unbounded bill.

**Fix (minimum viable — add ASP.NET Core rate limiting):**
```csharp
// Program.cs (species-service)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("identify", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});
// ...
app.UseRateLimiter();

app.MapPost("/api/v1/species/identify", IdentifyHandler)
    .RequireRateLimiting("identify")
    .AllowAnonymous(); // still anonymous, but now capped
```
Longer term: decide whether `/identify` should require auth (tie usage to a
subscription/quota the way IdentityService's billing model implies) rather
than just rate-limiting anonymously.

### 1.3 Refresh tokens stored in `localStorage`

**Problem:** `AuthContext.tsx` stores both the access token and the 30-day
refresh token via `localStorage.setItem` (lines 26-29).

**Why it matters:** anything readable by JavaScript on the page is readable
by an XSS payload. A stolen 30-day refresh token is a long-lived account
takeover, not just a session hijack.

**Fix:** move the refresh token to an httpOnly, `Secure`, `SameSite=Strict`
cookie set by the backend on login/refresh, and keep only the short-lived
access token in memory (not even `localStorage` — a JS variable/React
context is enough, since it's meant to be short-lived anyway):
```csharp
// IdentityService — on login/refresh
Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Strict,
    Expires = DateTimeOffset.UtcNow.AddDays(30)
});
```
This requires the frontend refresh call to stop reading the token from
`localStorage` and instead rely on the cookie being sent automatically
(`credentials: "include"` on fetch), and CORS must allow credentials from
the exact frontend origin only (already true per `ApiGateway/Program.cs:45-59`).

### 1.4 Containers run as root

**Problem:** none of the 5 .NET Dockerfiles have a `USER` directive; no
`securityContext`/`runAsNonRoot` in K8s or Helm manifests.

**Why it matters:** a container breakout or a dependency RCE has full root
inside the container by default, which is a larger blast radius for no
benefit — ASP.NET Core doesn't need root to bind :8080.

**Fix:**
```dockerfile
# in each service Dockerfile, final stage
RUN adduser --disabled-password --gecos "" appuser
USER appuser
```
And in K8s deployments:
```yaml
securityContext:
  runAsNonRoot: true
  runAsUser: 1000
  readOnlyRootFilesystem: true
```

---

## 2. Resilience

### 2.1 No timeout / retry / circuit breaker on the AI service client

**Problem:** `AIServiceClient` is a bare typed `HttpClient`
(`SpeciesService/Api/Program.cs:42-46`) with no `Polly` policy attached and no
explicit `HttpClient.Timeout`. A *slow* (not down) `ai-service` hits the BCL
default 100s timeout, throws `TaskCanceledException`, which nothing catches.

**Why it matters:** one slow dependency call ties up a request thread for up
to 100 seconds with no graceful degradation, and since there's also no
global exception handler (2.2), the caller gets a raw 500 instead of a clean
503.

**Fix:**
```csharp
// Program.cs (species-service)
builder.Services.AddHttpClient<IAIServiceClient, AIServiceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddResilienceHandler("ai-service", builder =>
{
    builder.AddTimeout(TimeSpan.FromSeconds(10));
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromMilliseconds(200)
    });
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(15)
    });
});
```
(Uses `Microsoft.Extensions.Http.Resilience`, the built-in Polly v8
integration — no need for a separate Polly package.)

### 2.2 No global exception handling middleware

**Problem:** no service has `UseExceptionHandler`/`IExceptionHandler`.
Unhandled exceptions (like the `TaskCanceledException` above) produce
whatever ASP.NET Core's default developer/production error response is,
inconsistently, with no structured logging of the failure.

**Fix (.NET 8+ `IExceptionHandler`, add to each Api project):**
```csharp
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = exception switch
        {
            TaskCanceledException => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status500InternalServerError
        };

        await httpContext.Response.WriteAsJsonAsync(new
        {
            error = "An unexpected error occurred."
        }, ct);

        return true;
    }
}

// Program.cs
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// ...
app.UseExceptionHandler();
```

### 2.3 Dual-write without an outbox — events can be silently lost

**Problem:** command handlers save to Postgres, then separately publish an
integration event afterward
(`CreateObservationCommandHandler.cs:42-45`,
`IdentifyFishCommandHandler.cs:61-66`). If the process crashes between the
two, the DB write persists but the event is gone forever — no consumer ever
finds out.

**Why it matters:** this is the classic dual-write problem. It's invisible
in dev (nothing crashes mid-request on your laptop) and shows up in prod as
"why didn't this observation trigger a notification" with no error anywhere
to explain it.

**Fix — MassTransit's built-in EF Core outbox** (writes the event to the
same DB transaction as the domain write; a separate delivery service drains
it to RabbitMQ):
```csharp
// Program.cs
builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<ObservationDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
    // ... existing consumer/bus config
});
```
```csharp
// CreateObservationCommandHandler — now atomic with the outbox enabled
await _dbContext.Observations.AddAsync(observation, ct);
await _publisher.PublishAsync(new ObservationCreatedEvent(...), ct); // buffered in same tx
await _dbContext.SaveChangesAsync(ct); // commits DB write + outbox row together
```

### 2.4 No idempotency in consumers — redelivery creates duplicates

**Problem:** `ObservationCreatedConsumer.Consume` inserts a new
`Notification` row on every delivery with no dedup check. MassTransit's
retry policy (and RabbitMQ's at-least-once delivery generally) means a
message *will* be redelivered eventually.

**Fix:** key notifications by the source event's message id and use an
upsert / unique-constraint-then-ignore pattern:
```csharp
// migration: add a unique index
// ALTER TABLE notifications ADD CONSTRAINT uq_notifications_source_event UNIQUE (source_event_id);

public async Task Consume(ConsumeContext<ObservationCreatedEvent> context)
{
    var messageId = context.MessageId ?? throw new InvalidOperationException("Message must have an id");

    var exists = await _db.Notifications.AnyAsync(n => n.SourceEventId == messageId);
    if (exists) return; // already processed — safe no-op on redelivery

    _db.Notifications.Add(Notification.FromObservationCreated(context.Message, messageId));
    await _db.SaveChangesAsync();
}
```

---

## 3. Data layer

### 3.1 Two competing schema sources that silently drift

**Problem:** `migrations/*.sql` is the documented source of truth
(per `CLAUDE.md`), but every service also calls `EnsureCreatedAsync()` on
startup (e.g. `SpeciesService/Program.cs:117-125`), which generates schema
straight from the EF model — a second, independent path that can diverge
from the SQL files without anyone noticing.

**Why it matters:** `EnsureCreated` also means EF Core Migrations aren't
used and `EnsureCreated` **skips schema changes on an existing database** —
it only creates the schema if the database doesn't exist at all. This is why
`002_add_subscriptions.sql` not being applied (3.2) didn't immediately break
anything: on a fresh dev DB, `EnsureCreated` quietly created the table from
the EF model anyway, masking the missing migration.

**Fix:** pick one source of truth. Given `CLAUDE.md` already commits to raw
SQL migrations, remove `EnsureCreatedAsync()` calls and make `make migrate`
the only schema-creation path:
```csharp
// Program.cs — delete this:
// await using var scope = app.Services.CreateAsyncScope();
// var db = scope.ServiceProvider.GetRequiredService<SpeciesDbContext>();
// await db.Database.EnsureCreatedAsync();
```
Add a startup check instead that fails fast if migrations haven't been run
(e.g. probe for the expected table) rather than silently generating schema.

### 3.2 `make migrate` never applies `002_add_subscriptions.sql`

**Problem:** `Makefile:45-55` lists SQL files to apply per service; the
IdentityService line only includes `001_initial_identity_schema.sql`.

**Fix:**
```makefile
# Makefile
migrate:
	psql $(DB_URL) -f migrations/IdentityService/001_initial_identity_schema.sql
	psql $(DB_URL) -f migrations/IdentityService/002_add_subscriptions.sql
	# ... rest unchanged
```
(Once 3.1's fix lands, this is the *only* way `subscriptions` gets created —
so this bug becomes load-bearing, not cosmetic.)

### 3.3 PostGIS column is dead — never populated

**Problem:** `ObservationDbContext.cs:26` does `o.Ignore(x => x.Location)`.
The `location GEOMETRY(Point,4326)` column, its GIST index, and the
`observations_within_radius()` SQL function are all defined in migrations
but never written to by application code — coordinates live only in plain
`double? Latitude/Longitude` columns.

**Why it matters:** you're paying the write/index-maintenance cost of a
PostGIS column that does nothing, and radius-search queries that *should* be
using an indexed spatial function are instead presumably done in
application code (or not at all) against unindexed lat/lon columns.

**Fix:**
```csharp
// ObservationDbContext.cs — stop ignoring it, map it
modelBuilder.Entity<Observation>()
    .Property(o => o.Location)
    .HasColumnType("geometry (point, 4326)");

// Observation.cs — populate on creation
public static Observation Create(double latitude, double longitude, ...)
{
    var location = new Point(longitude, latitude) { SRID = 4326 };
    return new Observation(..., location, latitude, longitude, ...);
}
```
Then the existing `observations_within_radius()` function becomes usable via
a raw SQL query or an EF Core spatial `Distance`/`IsWithinDistance` call
instead of hand-rolled bounding-box math.

### 3.4 N+1 query in fish identification

**Problem:** `IdentifyFishCommandHandler.Handle`
(`Commands/IdentifyFishCommandHandler.cs:39-43`) calls
`_speciesRepository.FindByScientificNameAsync` inside a `foreach` over AI
predictions — one DB round-trip per prediction (default topK = 5).

**Fix:**
```csharp
// ISpeciesRepository — add a batch lookup
Task<IReadOnlyList<Species>> FindByScientificNamesAsync(
    IEnumerable<string> scientificNames, CancellationToken ct);

// SpeciesRepository
public async Task<IReadOnlyList<Species>> FindByScientificNamesAsync(
    IEnumerable<string> scientificNames, CancellationToken ct) =>
    await _db.Species
        .Where(s => scientificNames.Contains(s.ScientificName))
        .ToListAsync(ct);

// IdentifyFishCommandHandler.Handle
var names = predictions.Select(p => p.ScientificName).ToList();
var matches = (await _speciesRepository.FindByScientificNamesAsync(names, ct))
    .ToDictionary(s => s.ScientificName);
foreach (var prediction in predictions)
{
    matches.TryGetValue(prediction.ScientificName, out var species);
    // ... existing per-prediction logic, now with a dictionary lookup instead of a query
}
```

---

## 4. Testing & CI

**Problem:** ApiGateway has zero tests; no Infrastructure-layer tests
(repositories, `AIServiceClient`, publishers) anywhere; no endpoint/HTTP-level
tests despite `CLAUDE.md` documenting `WebApplicationFactory<Program>` +
`Testcontainers.PostgreSql` as the intended pattern. CI
(`.github/workflows/ci.yml`) only runs `dotnet test` — no `dotnet format`
check, no frontend build/lint, no image build, no dependency scanning.

**Why it matters:** the auth-enforcement gaps in section 1 are exactly the
kind of thing an HTTP-level slice test catches (assert an anonymous request
to a protected route gets 401) — and none exist, so this class of bug ships
silently by default.

This one is already tracked as an existing backlog follow-up
("WebApplicationFactory HTTP-level slice tests", `BACKLOG.md`) — folded into
the new backlog item below rather than duplicated, since it's a bigger,
separate lift (needs Testcontainers or MassTransit test-harness mocking).

---

## 5. Dead code worth deleting

- `AddOMyFishTelemetry` (`OMyFish.Shared.BuildingBlocks/Observability/TelemetryExtensions.cs:9-22`) is never called — every service copy-pastes the same OTel setup inline instead. Either wire it up (`builder.Services.AddOMyFishTelemetry(...)` in each `Program.cs`) or delete it — right now it's a trap for the next person who assumes it's in use.
- Stray `{Consumers}` / `{Endpoints}` directories (literal curly braces in the name) sitting next to the real folders in NotificationService, ObservationService.Api, and SpeciesService.Api — almost certainly scaffolding leftovers. Worth confirming empty and deleting.
- Helm chart's `templates/deployment.yaml`, `templates/hpa.yaml`, `Chart.yaml` are literally `// placeholder` — `helm install` deploys nothing today despite a fully-authored `values.yaml`. Either finish the templates or remove the chart so it doesn't look like a working deploy path.

---

*Companion tracking: see `BACKLOG.md` item F for the checklist version of
this list.*
