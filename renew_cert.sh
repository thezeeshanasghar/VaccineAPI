#!/bin/bash

# Set script to exit immediately if a command exits with a non-zero status
set -euo pipefail

# Path variables
DOMAIN="myapi.vaccinationcentre.com"
PROJECT_DIR="/home/ec2-user/VaccineAPI"
CERT_DIR="/etc/letsencrypt/live/$DOMAIN"
PFX_PASSWORD="Ae!8bfb666"

umask 077

# Log function
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$PROJECT_DIR/cert_renewal.log"
}

log "Starting certificate renewal process"

# Ensure we can write logs (helpful when the script is run with sudo after a previous run)
if [ -f "$PROJECT_DIR/cert_renewal.log" ]; then
    chmod u+rw "$PROJECT_DIR/cert_renewal.log" || true
fi

nginx_listening_on_80() {
    ss -ltnp '( sport = :80 )' 2>/dev/null | grep -q '("nginx",'
}

stop_nginx() {
    log "Stopping nginx to free port 80 for certbot renewal"

    # Try systemd first (if nginx is managed by it)
    if command -v systemctl >/dev/null 2>&1; then
        systemctl stop nginx >/dev/null 2>&1 || true
    fi

    # Then try nginx's own signal handling
    if command -v nginx >/dev/null 2>&1; then
        nginx -s quit >/dev/null 2>&1 || true
    fi

    # Final fallback: signal the processes directly
    pkill -QUIT nginx >/dev/null 2>&1 || true

    # Wait for port 80 to be released
    for _ in {1..30}; do
        if ! nginx_listening_on_80; then
            return 0
        fi
        sleep 0.5
    done

    log "WARNING: nginx still appears to be listening on port 80 after stop attempts"
    return 1
}

start_nginx() {
    log "Starting nginx"

    if command -v systemctl >/dev/null 2>&1; then
        systemctl start nginx >/dev/null 2>&1 || true
    fi

    # If systemd didn't start it (or isn't present), start nginx directly.
    if ! pgrep -x nginx >/dev/null 2>&1; then
        if command -v nginx >/dev/null 2>&1; then
            nginx >/dev/null 2>&1 || true
        fi
    fi
}

# Step 1: Renew the certificate
log "Renewing Let's Encrypt certificate"
NGINX_STOPPED=0
cleanup() {
    if [ "${NGINX_STOPPED:-0}" -eq 1 ]; then
        start_nginx
    fi
}
trap cleanup EXIT INT TERM

if nginx_listening_on_80; then
    stop_nginx
    NGINX_STOPPED=1
fi

set +e
certbot renew --cert-name "$DOMAIN" --no-random-sleep-on-renew
CERTBOT_EXIT_CODE=$?
set -e

if [ "$NGINX_STOPPED" -eq 1 ]; then
    start_nginx
    NGINX_STOPPED=0
fi

# Step 2: Convert to PFX (only if renewal was successful)
if [ "$CERTBOT_EXIT_CODE" -ne 0 ]; then
    log "Certificate renewal failed (exit code: $CERTBOT_EXIT_CODE)"
    exit "$CERTBOT_EXIT_CODE"
fi

log "Certificate renewed successfully, converting to PFX"
cd "$PROJECT_DIR"
openssl pkcs12 -export -out ./certs/myapi.pfx \
    -inkey "$CERT_DIR/privkey.pem" \
    -in "$CERT_DIR/fullchain.pem" \
    -password pass:"$PFX_PASSWORD"

# Step 3: Set correct permissions (container runs as uid 1000, which is typically ec2-user)
chown ec2-user:ec2-user ./certs/myapi.pfx
chmod 600 ./certs/myapi.pfx

# Step 4: Restart the Docker container
log "Restarting Docker container"
cd "$PROJECT_DIR"
docker-compose restart api

log "Certificate renewal completed successfully"