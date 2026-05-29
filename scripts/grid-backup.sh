#!/bin/bash
set -euo pipefail

# Daily backup of GRID server (MySQL + PKI + config).
# Usage: ./grid-backup.sh <backup_dir>
# Cron:  0 2 * * * /opt/ransomguard-grid/scripts/grid-backup.sh /var/backups/grid

BACKUP_DIR="${1:-/var/backups/grid}"
TIMESTAMP=$(date -u +%Y%m%d-%H%M%S)
RETENTION_DAYS=30

mkdir -p "$BACKUP_DIR"
TEMP_DIR=$(mktemp -d)
trap "rm -rf $TEMP_DIR" EXIT

echo "=== GRID Backup $TIMESTAMP ==="

echo "[1/3] Dumping MySQL..."
docker compose -f /opt/ransomguard-grid/deployment/grid/docker-compose.yml exec -T mysql \
    mysqldump --single-transaction --routines --triggers --quick \
    -u root -p"$MYSQL_ROOT_PASSWORD" ransomguard_grid \
    > "$TEMP_DIR/database.sql"

echo "[2/3] Archiving PKI..."
cp -r /opt/ransomguard-grid/deployment/grid/pki "$TEMP_DIR/pki"

echo "[3/3] Creating archive..."
tar -czf "$TEMP_DIR/backup.tar.gz" -C "$TEMP_DIR" database.sql pki

if [ -n "${BACKUP_GPG_RECIPIENT:-}" ]; then
    gpg --batch --yes --trust-model always \
        --recipient "$BACKUP_GPG_RECIPIENT" \
        --encrypt --output "$BACKUP_DIR/grid-backup-$TIMESTAMP.tar.gz.gpg" \
        "$TEMP_DIR/backup.tar.gz"
else
    echo "WARN: BACKUP_GPG_RECIPIENT not set, backup unencrypted"
    cp "$TEMP_DIR/backup.tar.gz" "$BACKUP_DIR/grid-backup-$TIMESTAMP.tar.gz"
fi

find "$BACKUP_DIR" -name "grid-backup-*" -mtime +$RETENTION_DAYS -delete
echo "Backup complete: $BACKUP_DIR/grid-backup-$TIMESTAMP.*"
