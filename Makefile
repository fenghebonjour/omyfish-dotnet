.PHONY: up down build test migrate logs clean

# ─── Dev environment ──────────────────────────────────────────────────────────

up:
	docker compose up -d

down:
	docker compose down

restart:
	docker compose down && docker compose up -d

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

migrate:
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/SpeciesService/001_initial_species_schema.sql
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/ObservationService/001_initial_observation_schema.sql
	psql "postgresql://omyfish:omyfish_dev@localhost:5432/omyfish" \
	  -f migrations/ObservationService/002_add_postgis_extension.sql

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
