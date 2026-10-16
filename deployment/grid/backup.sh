#!/usr/bin/env bash
#
# RansomGuard-CM GRID — nightly backup.
#
# Captures everything that cannot be rebuilt from the git repository:
#   * the MySQL database (alerts, audit chain, users, tenants)
#   * the internal PKI (losing ca.key means re-enrolling every endpoint)
#   * the generated secrets (.env)
#
# Backups are written to /opt/ransomguard/backups and pruned after RETENTION
# days. They stay on the same host, so copy them off-box as well -- a ransomware
# incident on this server would otherwise take the evidence with it.
#
set -euo pipefail

SHARED_DIR="${GRID_SHARED_DIR:-/opt/ransomguard/shared}"
BACKUP_DIR="${GRID_BACKUP_DIR:-/opt/ransomguard/backups}"
RETENTION="${GRID_BACKUP_RETENTION_DAYS:-14}"
STAMP="$(date +%Y%m%d-%H%M%S)"

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

[ -f "$SHARED_DIR/.env" ] || { echo "[FAIL] $SHARED_DIR/.env missing" >&2; exit 1; }
set -a; . "$SHARED_DIR/.env"; set +a

echo "=== GRID backup $STAMP ==="

# ── Database ──────────────────────────────────────────────────────
# --single-transaction keeps InnoDB consistent without locking the API out.
DB_DUMP="$BACKUP_DIR/db-$STAMP.sql.gz"
docker exec grid-mysql sh -c \
    "exec mysqldump --single-transaction --quick --routines --events \
     -u root -p\"\$MYSQL_ROOT_PASSWORD\" \"$MYSQL_DATABASE\"" \
    | gzip -9 > "$DB_DUMP"

if [ ! -s "$DB_DUMP" ]; then
    echo "[FAIL] database dump is empty" >&2
    rm -f "$DB_DUMP"
    exit 1
fi
# A truncated dump is worse than no dump: prove it decompresses and ends on the
# marker mysqldump writes last.
gzip -t "$DB_DUMP"
zcat "$DB_DUMP" | tail -5 | grep -q 'Dump completed' || {
    echo "[FAIL] dump is truncated" >&2; rm -f "$DB_DUMP"; exit 1
}
echo "[ok] database  $(du -h "$DB_DUMP" | cut -f1)  $DB_DUMP"

# ── Secrets and PKI ───────────────────────────────────────────────
SECRETS_TAR="$BACKUP_DIR/secrets-$STAMP.tar.gz"
tar czf "$SECRETS_TAR" -C "$SHARED_DIR" .env pki
chmod 600 "$SECRETS_TAR"
echo "[ok] secrets   $(du -h "$SECRETS_TAR" | cut -f1)  $SECRETS_TAR"

# ── Prune ─────────────────────────────────────────────────────────
find "$BACKUP_DIR" -maxdepth 1 -type f -name '*-*.sql.gz'  -mtime "+$RETENTION" -print -delete
find "$BACKUP_DIR" -maxdepth 1 -type f -name '*-*.tar.gz'  -mtime "+$RETENTION" -print -delete

echo "=== Backup complete (retention ${RETENTION}d) ==="
df -h "$BACKUP_DIR" | tail -1
