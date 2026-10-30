#!/usr/bin/env bash
# Starts Atlas locally: infrastructure, database setup, and the three services.
#
#   1. docker compose up for SQL Server, Seq and Azurite (platform/docker-compose.yml)
#   2. builds the solution
#   3. runs Atlas.DbSetup, retrying while SQL Server finishes starting
#   4. starts the stand-ins (:5300), the Onboarding API (:5100) and the Review API (:5200)
#
# Usage: ./run.sh [--skip-infrastructure]
# Press Ctrl+C to stop the services. The containers keep running; stop them with
# "docker compose -f platform/docker-compose.yml down".
set -euo pipefail
cd "$(dirname "$0")"

if [[ "${1:-}" != "--skip-infrastructure" ]]; then
  echo "Starting SQL Server, Seq and Azurite..."
  docker compose -f platform/docker-compose.yml up -d
fi

echo "Building..."
dotnet build Atlas.sln -nologo -v q

echo "Creating the market databases and service logins..."
ready=false
for attempt in $(seq 1 30); do
  if dotnet run --project src/Atlas.DbSetup --no-build; then
    ready=true
    break
  fi
  echo "SQL Server is not ready yet, retrying in 5 seconds ($attempt/30)..."
  sleep 5
done
[[ "$ready" == true ]] || { echo "Database setup did not succeed."; exit 1; }

# The services are started from their build output rather than with "dotnet run", so that
# stopping this script stops them too.
pids=()
stop() { [[ ${#pids[@]} -gt 0 ]] && kill "${pids[@]}" 2>/dev/null || true; }
trap stop EXIT INT TERM

start() {
  local project="$1" port="$2" name
  name="$(basename "$project")"
  (cd "$project" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$port" \
    exec dotnet "bin/Debug/net8.0/$name.dll") &
  pids+=("$!")
}

start src/Atlas.Stubs 5300
start src/Onboarding/Atlas.Onboarding.Api 5100
start src/Atlas.Review.Api 5200

for port in 5300 5100 5200; do
  for _ in $(seq 1 60); do
    curl -fs "http://localhost:$port/health" > /dev/null && break
    sleep 0.5
  done
  curl -fs "http://localhost:$port/health" > /dev/null \
    || { echo "The service on port $port did not start."; exit 1; }
done

cat <<'EOF'

Atlas is running:
  Onboarding API  http://localhost:5100/swagger
  Review API      http://localhost:5200/swagger
  Stand-ins       http://localhost:5300/swagger
  Logs (Seq)      http://localhost:5341
Try:  curl -s -X POST http://localhost:5100/applications \
        -H 'Content-Type: application/json' --data-binary @samples/approved-mb.json
Press Ctrl+C to stop.
EOF
wait
