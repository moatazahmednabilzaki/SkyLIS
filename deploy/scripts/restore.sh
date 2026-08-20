#!/usr/bin/env bash
# Sky LIS restore from a pg_dump custom-format file. STOPS the API first so no
# writes race the restore, replaces the database content, then restarts.
#   ./deploy/scripts/restore.sh backups/skylis-20260817-020000.dump
set -euo pipefail

DUMP="${1:?usage: restore.sh <dump-file>}"
[ -f "$DUMP" ] || { echo "dump file not found: $DUMP"; exit 1; }

echo "Stopping the API..."
docker compose -f docker-compose.prod.yml stop api

echo "Restoring $DUMP ..."
docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_restore -U skylis -d skylis --clean --if-exists --no-owner < "$DUMP"

echo "Restarting the API..."
docker compose -f docker-compose.prod.yml start api
echo "Restore complete. Verify with: curl -fsS http://localhost:8080/health"
