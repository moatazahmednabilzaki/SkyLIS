#!/usr/bin/env bash
# Sky LIS nightly backup: pg_dump (custom format) from the postgres container,
# keeping the newest $KEEP dumps. Schedule via cron, e.g.:
#   0 2 * * * /opt/skylis/deploy/scripts/backup.sh /opt/skylis/backups
set -euo pipefail

BACKUP_DIR="${1:-./backups}"
KEEP="${KEEP:-14}"
STAMP="$(date +%Y%m%d-%H%M%S)"

mkdir -p "$BACKUP_DIR"
docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U skylis -d skylis --format=custom > "$BACKUP_DIR/skylis-$STAMP.dump"

# Rotate: keep the newest $KEEP dumps
ls -1t "$BACKUP_DIR"/skylis-*.dump 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm --
echo "Backup written: $BACKUP_DIR/skylis-$STAMP.dump"
