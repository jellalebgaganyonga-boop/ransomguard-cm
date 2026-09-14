#!/usr/bin/env bash
#
# RansomGuard-CM GRID — deployment.
#
# Runs on the server, called either by hand or by the GitHub Actions CD job over
# SSH. One code path for both so a manual deployment can never diverge from an
# automated one.
#
#   1. wire the release to the persistent shared state (.env, PKI, assets)
#   2. refresh the dashboard certificate
#   3. build + start the stack (alembic migrations run in the API entrypoint)
#   4. seed the first tenant/admin when the database is empty
#   5. smoke-test the agent and dashboard endpoints
#   6. roll back to the previous image when the smoke test fails
#
set -euo pipefail

SHARED_DIR="${GRID_SHARED_DIR:-/opt/ransomguard/shared}"
STACK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HEALTH_TIMEOUT="${GRID_HEALTH_TIMEOUT:-180}"
COMPOSE=(docker compose --project-name grid --env-file "$SHARED_DIR/.env")

cd "$STACK_DIR"

log() { printf '\n=== %s ===\n' "$*"; }

# ── 1. Wire release to shared state ───────────────────────────────
log "1/6 Linking shared state"
[ -f "$SHARED_DIR/.env" ] || { echo "[FAIL] $SHARED_DIR/.env missing -- run bootstrap.sh first" >&2; exit 1; }

# `. .env` reports the status of the LAST line only, so a broken line in the
# middle (an unquoted value with spaces, say) silently half-loads the file and
# the deployment carries on with missing secrets. Validate before sourcing.
if bad="$(grep -nE '^[A-Za-z_][A-Za-z0-9_]*=[^"'"'"']*[[:space:]]' "$SHARED_DIR/.env")"; then
    echo "[FAIL] unquoted value(s) containing spaces in $SHARED_DIR/.env:" >&2
    echo "$bad" | sed 's/^/       /' >&2
    exit 1
fi
set -a; . "$SHARED_DIR/.env"; set +a

for required in MYSQL_PASSWORD REDIS_PASSWORD GRID_JWT_SECRET_KEY BIND_ADDR                 TLS_PUBLIC_DIR DASHBOARD_DIST_DIR AGENT_PACKAGES_DIR; do
    [ -n "${!required:-}" ] || { echo "[FAIL] $required is empty in .env" >&2; exit 1; }
done

# compose mounts ./pki relative to this directory; the real material lives in
# shared state so that it survives a source tree replacement.
rm -rf "$STACK_DIR/pki"
ln -sfn "$SHARED_DIR/pki" "$STACK_DIR/pki"
ln -sfn "$SHARED_DIR/.env" "$STACK_DIR/.env"
echo "pki -> $(readlink -f "$STACK_DIR/pki")"

# ── 2. Dashboard certificate ──────────────────────────────────────
log "2/6 Dashboard certificate"
"$STACK_DIR/refresh-tls.sh"

# ── 3. Build and start ────────────────────────────────────────────
log "3/6 Building and starting the stack"
PREV_IMAGE="$(docker inspect -f '{{.Image}}' grid-api 2>/dev/null || true)"
[ -n "$PREV_IMAGE" ] && echo "previous grid-api image: ${PREV_IMAGE:0:19}"

"${COMPOSE[@]}" build grid-api

# The server reaches the registry through a NAT link that drops mid-transfer;
# a single failed layer must not abort a deployment. Docker resumes completed
# layers, so a retry costs only what was actually lost.
pull_ok=0
for attempt in 1 2 3 4 5; do
    if "${COMPOSE[@]}" pull --quiet mysql redis nginx; then pull_ok=1; break; fi
    echo "[warn] registry pull attempt $attempt failed -- retrying in 10s"
    sleep 10
done
[ "$pull_ok" -eq 1 ] || { echo "[FAIL] could not pull base images after 5 attempts" >&2; exit 1; }

"${COMPOSE[@]}" up -d --remove-orphans

# ── 4. Wait for health ────────────────────────────────────────────
log "4/6 Waiting for grid-api to become healthy (timeout ${HEALTH_TIMEOUT}s)"
deadline=$(( $(date +%s) + HEALTH_TIMEOUT ))
state=""
while [ "$(date +%s)" -lt "$deadline" ]; do
    state="$(docker inspect -f '{{.State.Health.Status}}' grid-api 2>/dev/null || echo starting)"
    [ "$state" = "healthy" ] && break
    if [ "$(docker inspect -f '{{.State.Status}}' grid-api 2>/dev/null)" = "exited" ]; then
        echo "[FAIL] grid-api exited during startup"; state="exited"; break
    fi
    sleep 5
done
echo "grid-api health: $state"

rollback() {
    echo "[rollback] restoring previous image"
    if [ -n "$PREV_IMAGE" ]; then
        docker tag "$PREV_IMAGE" "ransomguard/grid-api:${GRID_IMAGE_TAG:-local}"
        "${COMPOSE[@]}" up -d --no-build grid-api
        echo "[rollback] previous image restored"
    else
        echo "[rollback] no previous image recorded -- stack left stopped for inspection"
    fi
    echo "---- grid-api logs (last 60) ----"
    docker logs --tail 60 grid-api 2>&1 || true
    exit 1
}

[ "$state" = "healthy" ] || rollback

# ── 5. Seed ───────────────────────────────────────────────────────
log "5/6 Seeding initial tenant/admin (idempotent)"
# The GRID_SEED_* values are deployment inputs, not runtime configuration, so
# they are deliberately absent from the compose service environment. Pass them
# to this one command instead of widening the container's environment.
seed_env=(-e PYTHONPATH=/app/src -e "GRID_ENVIRONMENT=${GRID_ENVIRONMENT:-production}")
for var in GRID_SEED_TENANT_NAME GRID_SEED_TENANT_CODE GRID_SEED_TENANT_EMAIL            GRID_SEED_ADMIN_EMAIL GRID_SEED_ADMIN_NAME GRID_SEED_ADMIN_PASSWORD            GRID_SEED_ANALYST_EMAIL GRID_SEED_ANALYST_PASSWORD            GRID_SEED_AUDITOR_EMAIL GRID_SEED_AUDITOR_PASSWORD; do
    [ -n "${!var:-}" ] && seed_env+=(-e "$var=${!var}")
done

docker exec "${seed_env[@]}" grid-api python /app/scripts/seed_initial_data.py || {
    echo "[FAIL] seeding failed -- no administrator account was created" >&2
    exit 1
}

# ── 6. Smoke test ─────────────────────────────────────────────────
log "6/6 Smoke test"
HOST="${GRID_TAILSCALE_IP:-127.0.0.1}"
fail=0
check() { # name url
    code="$(curl -sk -o /dev/null -w '%{http_code}' --max-time 15 "$2" || echo 000)"
    if [ "$code" = "${3:-200}" ]; then
        printf '  [ok]   %-28s %s\n' "$1" "$code"
    else
        printf '  [FAIL] %-28s %s (expected %s)\n' "$1" "$code" "${3:-200}"; fail=1
    fi
}
check "agent health (:443)"      "https://${HOST}/api/v1/health"
check "dashboard health (:8443)" "https://${HOST}:8443/api/v1/health"
check "dashboard SPA"            "https://${HOST}:8443/"
check "openapi blocked"          "https://${HOST}:8443/openapi.json" 404

[ "$fail" -eq 0 ] || rollback

log "Deployment successful"
"${COMPOSE[@]}" ps --format 'table {{.Name}}\t{{.Status}}'
