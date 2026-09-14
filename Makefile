.PHONY: up build-up down build test migrate logs clean

# ─── Dev environment ──────────────────────────────────────────────────────────

# Uses the external ai-service (../omyfish-ai's own compose) if it's already up on
# :8000; otherwise falls back to starting this repo's bundled copy (profiles: [bundled]).
up:
	@if curl -sf http://localhost:8000/health >/dev/null 2>&1; then \
		docker compose up -d; \
	else \
		echo "ai-service not reachable on :8000 — starting bundled ai-service instead"; \
		if [ ! -f ../omyfish-ai/.env ] || ! grep -qE '^VISUALCROSSING_API_KEY=.+' ../omyfish-ai/.env || ! grep -qE '^GROQ_API_KEY=.+' ../omyfish-ai/.env; then \
			echo "warning: ../omyfish-ai/.env is missing VISUALCROSSING_API_KEY and/or GROQ_API_KEY — bite-score forecast and /regs/ask will 503 in bundled mode"; \
		fi; \
		docker network create omyfish-shared >/dev/null 2>&1 || true; \
		COMPOSE_PROFILES=bundled docker compose up -d; \
	fi

# Use when code, dependencies, or Dockerfiles changed — rebuilds images first
build-up:
	@if curl -sf http://localhost:8000/health >/dev/null 2>&1; then \
		docker compose up -d --build; \
	else \
		echo "ai-service not reachable on :8000 — starting bundled ai-service instead"; \
		if [ ! -f ../omyfish-ai/.env ] || ! grep -qE '^VISUALCROSSING_API_KEY=.+' ../omyfish-ai/.env || ! grep -qE '^GROQ_API_KEY=.+' ../omyfish-ai/.env; then \
			echo "warning: ../omyfish-ai/.env is missing VISUALCROSSING_API_KEY and/or GROQ_API_KEY — bite-score forecast and /regs/ask will 503 in bundled mode"; \
		fi; \
		docker network create omyfish-shared >/dev/null 2>&1 || true; \
		COMPOSE_PROFILES=bundled docker compose up -d --build; \
	fi

down:
	docker compose --profile bundled down

restart:
	$(MAKE) down
	$(MAKE) up

logs:
	docker compose logs -f $(service)

ps:
	docker compose ps

# ─── Build ────────────────────────────────────────────────────────────────────

build:
	dotnet build omyfish-dotnet.slnx

build-docker:
	docker compose build

release:
	dotnet publish omyfish-dotnet.slnx -c Release

# ─── Test ─────────────────────────────────────────────────────────────────────

test:
	dotnet test omyfish-dotnet.slnx --logger "console;verbosity=minimal"

test-service:
	dotnet test src/services/$(service) --logger "console;verbosity=minimal"

# ─── Database ─────────────────────────────────────────────────────────────────

# Manual fallback only — each service now applies its own migrations/<Service>/*.sql
# automatically on startup via DbUp (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.1). Re-running
# this against a DB DbUp has already migrated is safe (every migration file is idempotent),
# but it isn't needed for normal `make build-up` use.
migrate:
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/IdentityService/001_initial_identity_schema.sql
	# 002 was missing here, so `subscriptions` was never created by `make migrate` (BACKLOG.md item F)
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/IdentityService/002_add_subscriptions.sql
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/SpeciesService/001_initial_species_schema.sql
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/ObservationService/001_initial_observation_schema.sql
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/ObservationService/002_add_postgis_extension.sql
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/NotificationService/001_initial_notification_schema.sql
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/NotificationService/002_add_source_event_id.sql

# ─── Utilities ────────────────────────────────────────────────────────────────

fmt:
	dotnet format omyfish-dotnet.slnx

clean:
	dotnet clean
	docker compose down -v

shell-postgres:
	docker compose exec postgres psql -U omyfish -d omyfish

minio-create-buckets:
	docker compose exec minio mc alias set local http://localhost:9000 omyfish omyfish_dev
	docker compose exec minio mc mb local/omyfish-images --ignore-existing
	docker compose exec minio mc mb local/omyfish-exports --ignore-existing

# ─── Frontend ──────────────────────────────────────────────────────────────────

frontend-dev:
	cd frontend/omyfish-web && npm run dev

frontend-install:
	cd frontend/omyfish-web && npm install

frontend-build:
	cd frontend/omyfish-web && npm run build

frontend-test:
	cd frontend/omyfish-web && npm test
