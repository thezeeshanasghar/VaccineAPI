#!/bin/bash

# Set script to exit immediately if a command exits with a non-zero status
set -e

# Path variables
DOMAIN="myapi.vaccinationcentre.com"
PROJECT_DIR="/home/ec2-user/VaccineAPI"
CERT_DIR="/etc/letsencrypt/live/$DOMAIN"
PFX_PASSWORD="Ae!8bfb666"

# Log function
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$PROJECT_DIR/cert_renewal.log"
}

log "Starting certificate renewal process"

# Step 1: Renew the certificate
log "Renewing Let's Encrypt certificate"
certbot renew --cert-name $DOMAIN

# Step 2: Convert to PFX (only if renewal was successful)
if [ $? -eq 0 ]; then
    log "Certificate renewed successfully, converting to PFX"
    cd $PROJECT_DIR
    openssl pkcs12 -export -out ./certs/myapi.pfx \
        -inkey $CERT_DIR/privkey.pem \
        -in $CERT_DIR/fullchain.pem \
        -password pass:"$PFX_PASSWORD"
    
    # Step 3: Set correct permissions
    chown ec2-user:ec2-user ./certs/myapi.pfx
    chmod 600 ./certs/myapi.pfx
    
    # Step 4: Restart the Docker container
    log "Restarting Docker container"
    cd $PROJECT_DIR
    docker-compose restart api
    
    log "Certificate renewal completed successfully"
else
    log "Certificate renewal failed"
    exit 1
fi 