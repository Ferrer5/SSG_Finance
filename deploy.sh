#!/usr/bin/env bash

set -Eeuo pipefail

log_info() { echo "[INFO] $1"; }
log_ok() { echo "[ OK ] $1"; }
log_warn() { echo "[WARN] $1"; }
log_error() { echo "[ERROR] $1"; }

on_error() {
    echo
    log_error "Deployment failed."
    log_info "Showing recent container logs..."
    docker compose logs --tail=100 || true
}

trap on_error ERR

echo
echo "========================================"
echo "         SSG Finance Deployment         "
echo "========================================"
echo

# =========================
# Create .env on first deployment
# =========================
if [ ! -f ".env" ]; then
    log_info "Creating .env from .env.example..."
    cp .env.example .env

    log_ok ".env created."
    log_warn "Edit .env with your production values."
    log_warn "Run ./deploy.sh again."
    exit 1
fi

# =========================
# Load environment
# =========================
log_info "Loading environment..."
set -a
source .env
set +a
log_ok "Environment loaded."

# =========================
# Build Docker images locally
# =========================
log_info "Building Docker images..."
docker compose build
log_ok "Images built."

# =========================
# Update containers
# =========================
log_info "Updating containers..."
docker compose up -d --remove-orphans
log_ok "Containers updated."

# =========================
# Wait for MySQL
# =========================
log_info "Waiting for MySQL..."
until [ "$(docker inspect -f '{{.State.Health.Status}}' ssgfinance-mysql)" = "healthy" ]; do
    sleep 2
done
log_ok "MySQL is healthy."

# =========================
# Wait for Application
# =========================
log_info "Waiting for application..."
until [ "$(docker inspect -f '{{.State.Health.Status}}' ssgfinance-app)" = "healthy" ]; do
    sleep 2
done
log_ok "Application is healthy."

# =========================
# Validate Nginx
# =========================
log_info "Validating Nginx configuration..."
docker exec ssgfinance-nginx nginx -t >/dev/null
log_ok "Nginx configuration is valid."

# =========================
# Verify Application
# =========================
log_info "Checking application endpoint..."
curl --fail --silent "http://localhost:${APP_PORT}/" >/dev/null
log_ok "Application is reachable."

# =========================
# Cleanup
# =========================
log_info "Cleaning unused Docker images..."
docker image prune -f >/dev/null
log_ok "Cleanup complete."

echo
echo "========================================"
echo "    Deployment completed successfully   "
echo "========================================"
echo
echo "Application: http://localhost:${APP_PORT}"
echo

docker compose ps