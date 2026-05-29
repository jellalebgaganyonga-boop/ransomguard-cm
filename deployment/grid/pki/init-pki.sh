#!/bin/bash
set -euo pipefail

# Sprint 6 PKI Initialization
# Generates Root CA, Intermediate CA, server cert, DH params, and Ed25519 signing keys.
# Usage: ./init-pki.sh <hospital_name> <country_code>

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <hospital_name> <country_code>"
    echo "Example: $0 'Hopital Central Yaounde' CM"
    exit 1
fi

HOSPITAL_NAME="$1"
COUNTRY_CODE="$2"
PKI_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$PKI_DIR"

echo "=== Sprint 6 PKI Initialization ==="
echo "Hospital: $HOSPITAL_NAME"
echo "Country: $COUNTRY_CODE"
echo ""

# Step 1: Root CA
if [ -f "ca.key" ]; then
    echo "[skip] Root CA already exists"
else
    echo "[1/5] Generating Root CA (RSA 4096, 10 years)..."
    openssl genrsa -out ca.key 4096
    chmod 600 ca.key
    openssl req -new -x509 -days 3650 -key ca.key -out ca.crt \
        -subj "/C=$COUNTRY_CODE/O=$HOSPITAL_NAME/CN=RansomGuard-CM Root CA"
    echo "[ok] Root CA generated"
fi

# Step 2: Intermediate CA
if [ -f "intermediate.key" ]; then
    echo "[skip] Intermediate CA already exists"
else
    echo "[2/5] Generating Intermediate CA (RSA 4096, 5 years)..."
    openssl genrsa -out intermediate.key 4096
    chmod 600 intermediate.key
    openssl req -new -key intermediate.key -out intermediate.csr \
        -subj "/C=$COUNTRY_CODE/O=$HOSPITAL_NAME/CN=RansomGuard-CM Intermediate CA"
    openssl x509 -req -in intermediate.csr -CA ca.crt -CAkey ca.key \
        -CAcreateserial -out intermediate.crt -days 1825 \
        -extfile <(echo -e "basicConstraints=CA:TRUE,pathlen:0\nkeyUsage=keyCertSign,cRLSign")
    rm -f intermediate.csr
    echo "[ok] Intermediate CA generated"
fi

# Step 3: Server certificate
if [ -f "server.key" ]; then
    echo "[skip] Server cert already exists"
else
    echo "[3/5] Generating server certificate (RSA 2048, 1 year)..."
    openssl genrsa -out server.key 2048
    chmod 600 server.key
    openssl req -new -key server.key -out server.csr \
        -subj "/C=$COUNTRY_CODE/O=$HOSPITAL_NAME/CN=grid.local"
    cat > server.ext <<EXTEOF
subjectAltName = @alt_names
keyUsage = digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
[alt_names]
DNS.1 = grid.local
DNS.2 = dashboard.grid.local
DNS.3 = localhost
IP.1 = 127.0.0.1
EXTEOF
    openssl x509 -req -in server.csr -CA intermediate.crt -CAkey intermediate.key \
        -CAcreateserial -out server.crt -days 365 -extfile server.ext
    rm -f server.csr server.ext
    cat server.crt intermediate.crt ca.crt > server-fullchain.crt
    echo "[ok] Server certificate generated"
fi

# Step 4: DH parameters
if [ -f "dhparam.pem" ]; then
    echo "[skip] DH parameters already exist"
else
    echo "[4/5] Generating DH parameters (2048 bits)..."
    openssl dhparam -out dhparam.pem 2048
    echo "[ok] DH parameters generated"
fi

# Step 5: Ed25519 threat intel signing key
if [ -f "threat-intel-ed25519.key" ]; then
    echo "[skip] Threat intel Ed25519 key already exists"
else
    echo "[5/5] Generating Ed25519 key for threat intel signing..."
    openssl genpkey -algorithm ED25519 -out threat-intel-ed25519.key
    chmod 600 threat-intel-ed25519.key
    openssl pkey -in threat-intel-ed25519.key -pubout -out threat-intel-ed25519.key.pub
    echo "[ok] Ed25519 keys generated"
fi

echo ""
echo "=== PKI Initialization Complete ==="
echo ""
echo "CRITICAL — Back up offline:"
echo "  - ca.key (Root CA private key)"
echo "  - intermediate.key (Intermediate CA private key)"
echo "  - threat-intel-ed25519.key (Threat intel signing key)"
