#!/bin/sh
# Runs once, during the primary's first initdb (official postgres image convention:
# /docker-entrypoint-initdb.d/*.sh). The image's own pg_hba.conf never adds a replication
# line, so postgres-replica's pg_basebackup would otherwise be refused. trust, not a password,
# because this only matters inside the isolated omyfish-net bridge network — no port is
# published for replication traffic. Same script as omyfish-java's.
#
# An existing postgres_data volume (initialized before this script existed) never re-runs it —
# see infrastructure/postgres/replica-entrypoint.sh for the one-liner that enables it in place.
set -e
echo "host replication all all trust" >> "$PGDATA/pg_hba.conf"
