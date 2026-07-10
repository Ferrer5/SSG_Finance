#!/usr/bin/env bash

set -Eeuo pipefail

log_info() { echo "[INFO] $1"; }
log_ok() { echo "[ OK ] $1"; }
log_warn() { echo "[WARN] $1"; }
log_error() { echo "[ERROR] $1"; }

APP_IMAGE="ssgfinance-app"
APP_TAG_LATEST="${APP_IMAGE}:latest"
APP_TAG_PREVIOUS="${APP_IMAGE}:previous"

on_error() {
    echo
    log_error "Rollback failed."
    log_info "Showing recent container logs..."
    docker compose logs --tail=100 || true
}

trap on_error ERR

echo
echo "========================================"
echo "      SSG Finance Image Rollback        "
echo "========================================"
echo

# =========================
# Load environment
# =========================
if [ ! -f ".env" ]; then
    log_error ".env not found. Run from the project root after an initial deploy."
    exit 1
fi

log_info "Loading environment..."
set -a
source .env
set +a
log_ok "Environment loaded."

# =========================
# Verify previous image exists
# =========================
log_info "Checking for previous app image..."
if ! docker image inspect "$APP_TAG_PREVIOUS" >/dev/null 2>&1; then
    log_error "No previous image found: ${APP_TAG_PREVIOUS}"
    log_error "A successful prior deploy is required before rollback is available."
    exit 1
fi

previous_id=$(docker image inspect --format '{{.Id}}' "$APP_TAG_PREVIOUS" | cut -c8-19)
log_ok "Found ${APP_TAG_PREVIOUS} (${previous_id})."

# =========================
# Restore previous image as latest
# =========================
log_info "Tagging ${APP_TAG_PREVIOUS} as ${APP_TAG_LATEST}..."
docker tag "$APP_TAG_PREVIOUS" "$APP_TAG_LATEST"
log_ok "Image restored."

# =========================
# Recreate containers (no rebuild)
# =========================
log_info "Recreating containers from restored image..."
docker compose up -d --remove-orphans --no-build
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

echo
echo "========================================"
echo "      Rollback completed successfully   "
echo "========================================"
echo
echo "Application: http://localhost:${APP_PORT}"
echo "Restored image: ${APP_TAG_PREVIOUS} (${previous_id}) → ${APP_TAG_LATEST}"
echo

docker compose ps
