#!/bin/sh
# Orchestrates cache-failure.js: starts k6 in the background, stops the redis container
# partway through the run, restarts it, then waits for k6 to finish. Run from anywhere.
set -eu

cd "$(dirname "$0")/../../../.."

docker run --rm --network omyfish-dotnet_omyfish-net \
    -v "$(pwd)/src/services/ShopService/loadtest/scripts:/scripts:ro" \
    -e BASE_URL=http://shop-service:8080 \
    grafana/k6 run /scripts/cache-failure.js &
K6_PID=$!

sleep 30
echo ">>> stopping redis mid-run"
docker compose stop redis

sleep 30
echo ">>> restarting redis"
docker compose start redis

wait "$K6_PID"
