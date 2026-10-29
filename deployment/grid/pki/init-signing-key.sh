#!/usr/bin/env bash
# Creates the GRID threat-intel Ed25519 signing key, once (GRID-KEY-001).
#
# Agents trust the public half of this key: every threat-intel package the GRID
# sends is signed with it. The GRID never creates it outside development -- it
# refuses to start without it -- so this script is the only way a production key
# comes into existence. It NEVER overwrites an existing key: a new key would make
# every agent reject the following packages.
#
# Usage: init-signing-key.sh [PKI_DIR]     (default: the directory of this script)
# Called by init-pki.sh and bootstrap.sh; safe to run again.
set -euo pipefail

PKI_DIR="${1:-$(cd "$(dirname "$0")" && pwd)}"
KEY="$PKI_DIR/threat-intel-ed25519.key"

if [ -e "$KEY" ]; then
    echo "[skip] Threat intel Ed25519 key already exists: $KEY (never overwritten)"
    exit 0
fi

mkdir -p "$PKI_DIR"
umask 077
openssl genpkey -algorithm ED25519 -out "$KEY"
chmod 600 "$KEY"
openssl pkey -in "$KEY" -pubout -out "$KEY.pub"
echo "[ok] Threat intel Ed25519 key generated: $KEY"
echo "     Back it up offline now (runbook, section 7): losing it means re-keying every agent."
