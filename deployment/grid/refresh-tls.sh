#!/usr/bin/env bash
#
# Issues / renews the browser-facing certificate for the dashboard (:8443).
#
# Tailscale can obtain a real Let's Encrypt certificate for the node's MagicDNS
# name (<node>.<tailnet>.ts.net) even though the server has no public IP -- the
# DNS-01 challenge is answered by Tailscale's own nameservers. That gives
# operators a trusted padlock with zero certificate warnings.
#
# Requires "HTTPS Certificates" to be enabled once in the tailnet admin console
# (https://login.tailscale.com/admin/dns). When it is not enabled, this script
# falls back to the internal PKI so that nginx always starts -- the dashboard is
# then served with a self-signed certificate exactly as before.
#
# Idempotent, and safe to run from a systemd timer (Let's Encrypt certificates
# last 90 days; Tailscale renews when fewer than 14 days remain).
#
set -euo pipefail

SHARED_DIR="${GRID_SHARED_DIR:-/opt/ransomguard/shared}"
TLS_DIR="$SHARED_DIR/tls"
PKI_DIR="$SHARED_DIR/pki"

mkdir -p "$TLS_DIR"

# jq is the reliable reader here: `tailscale status --json` is pretty-printed,
# so a naive "DNSName":" grep silently returns nothing.
TS_DNS="$(tailscale status --json 2>/dev/null | jq -r '.Self.DNSName // empty' | sed 's/\.$//' || true)"

use_internal_pki() {
    echo "[fallback] Serving the dashboard with the internal PKI certificate."
    echo "           Enable HTTPS Certificates at https://login.tailscale.com/admin/dns"
    echo "           then re-run this script to switch to Let's Encrypt."
    cp "$PKI_DIR/server-fullchain.crt" "$TLS_DIR/fullchain.crt"
    cp "$PKI_DIR/server.key" "$TLS_DIR/privkey.key"
    chmod 644 "$TLS_DIR/fullchain.crt"
    chmod 640 "$TLS_DIR/privkey.key"
}

if [ -z "$TS_DNS" ]; then
    echo "[warn] No MagicDNS name for this node."
    use_internal_pki
    exit 0
fi

# `tailscale set --operator=<user>` (run by bootstrap.sh) lets the deploy user
# issue certificates without sudo, which matters because the CD job has no TTY
# to answer a password prompt. sudo -n is only a fallback.
ts_cert() {
    if tailscale cert --cert-file "$1" --key-file "$2" "$3" 2>"$TLS_DIR/.last-error"; then
        return 0
    fi
    if sudo -n true 2>/dev/null; then
        sudo tailscale cert --cert-file "$1" --key-file "$2" "$3" 2>"$TLS_DIR/.last-error" || return 1
        sudo chown "$(id -u):$(id -g)" "$1" "$2"
        return 0
    fi
    return 1
}

echo "[tls] Requesting certificate for $TS_DNS"
if ts_cert "$TLS_DIR/fullchain.crt" "$TLS_DIR/privkey.key" "$TS_DNS"; then
    chmod 644 "$TLS_DIR/fullchain.crt"
    chmod 640 "$TLS_DIR/privkey.key"
    echo "[ok] Let's Encrypt certificate installed for $TS_DNS"
    openssl x509 -in "$TLS_DIR/fullchain.crt" -noout -subject -enddate
else
    echo "[warn] tailscale cert failed:"
    sed 's/^/       /' "$TLS_DIR/.last-error"
    use_internal_pki
fi
