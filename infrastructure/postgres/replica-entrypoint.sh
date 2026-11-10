#!/bin/sh
# Overrides the stock postgres image's entrypoint for the read replica only. On first boot
# (empty $PGDATA) it clones the primary via pg_basebackup instead of running initdb — -R makes
# pg_basebackup write standby.signal + primary_conninfo itself, so the normal entrypoint below
# starts postgres straight into standby mode streaming from the primary. Later boots skip the
# clone and just resume as a standby. Same script as omyfish-java's.
set -e

if [ -z "$(ls -A "$PGDATA" 2>/dev/null)" ]; then
    echo "replica-entrypoint: \$PGDATA is empty — waiting for the primary, then cloning it"
    until pg_isready -h "$PRIMARY_HOST" -U "$POSTGRES_USER" -q; do
        sleep 2
    done
    if ! pg_basebackup -h "$PRIMARY_HOST" -U "$POSTGRES_USER" -D "$PGDATA" -Fp -Xs -P -R; then
        rm -rf "${PGDATA:?}"/*
        echo "replica-entrypoint: pg_basebackup was refused. If postgres_data predates"
        echo "  infrastructure/postgres/primary-init/allow-replication.sh, enable replication in place:"
        echo "  docker compose exec postgres sh -c 'echo \"host replication all all trust\" >> \$PGDATA/pg_hba.conf'"
        echo "  docker compose exec postgres psql -U omyfish -c 'SELECT pg_reload_conf()'"
        echo "  shop-service keeps serving catalog reads from the primary meanwhile."
        exit 1
    fi
fi

exec docker-entrypoint.sh postgres
