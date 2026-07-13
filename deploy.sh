#!/usr/bin/env bash

set -Eeuo pipefail

log_info() { echo "[INFO] $1"; }
log_ok() { echo "[ OK ] $1"; }
log_warn() { echo "[WARN] $1"; }
log_error() { echo "[ERROR] $1"; }

APP_IMAGE="ssgfinance-app"
APP_TAG_LATEST="${APP_IMAGE}:latest"
APP_TAG_PREVIOUS="${APP_IMAGE}:previous"

ROLLBACK_AVAILABLE=false
ROLLING_BACK=false

rollback_app_image() {
    if [ "$ROLLBACK_AVAILABLE" != true ]; then
        log_warn "No previous image to restore."
        return 1
    fi

    if ! docker image inspect "$APP_TAG_PREVIOUS" >/dev/null 2>&1; then
        log_error "Previous image tag not found: ${APP_TAG_PREVIOUS}"
        return 1
    fi

    local previous_id
    previous_id=$(docker image inspect --format '{{.Id}}' "$APP_TAG_PREVIOUS" | cut -c8-19)

    log_info "Restoring previous image (${previous_id}) as ${APP_TAG_LATEST}..."
    docker tag "$APP_TAG_PREVIOUS" "$APP_TAG_LATEST"

    log_info "Recreating containers from restored image..."
    docker compose up -d --remove-orphans --no-build
    log_ok "Containers restored from previous image."
}

on_error() {
    # Avoid recursive trap if rollback itself fails
    if [ "$ROLLING_BACK" = true ]; then
        log_error "Rollback also failed. Run ./rollback.sh manually."
        return
    fi

    echo
    log_error "Deployment failed."
    log_info "Showing recent container logs..."
    docker compose logs --tail=100 || true

    if [ "$ROLLBACK_AVAILABLE" = true ]; then
        echo
        log_info "Attempting automatic rollback to previous image..."
        ROLLING_BACK=true
        # Disable ERR trap during rollback to avoid re-entry
        trap - ERR
        if rollback_app_image; then
            log_ok "Automatic rollback completed."
            log_warn "Stack is running the previous image. Investigate the failed deploy, then re-run ./deploy.sh when ready."
        else
            log_error "Automatic rollback failed. Run ./rollback.sh manually."
        fi
    else
        log_warn "No previous image was saved; automatic rollback is unavailable."
    fi
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
# Snapshot current app image (for rollback)
# =========================
log_info "Saving current app image for rollback..."
if docker image inspect "$APP_TAG_LATEST" >/dev/null 2>&1; then
    image_id=$(docker image inspect --format '{{.Id}}' "$APP_TAG_LATEST" | cut -c8-19)
    docker tag "$APP_TAG_LATEST" "$APP_TAG_PREVIOUS"
    ROLLBACK_AVAILABLE=true
    log_ok "Saved ${APP_TAG_LATEST} (${image_id}) as ${APP_TAG_PREVIOUS}."
else
    log_warn "No existing ${APP_TAG_LATEST} image found (first deploy). Rollback will not be available."
fi

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
# Wait for Nginx
# =========================
log_info "Waiting for Nginx..."
until [ "$(docker inspect -f '{{.State.Health.Status}}' ssgfinance-nginx)" = "healthy" ]; do
    sleep 2
done
log_ok "Nginx is healthy."

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
# Named tags (latest, previous) are retained; only dangling layers are removed.
log_info "Cleaning unused Docker images (keeping ${APP_TAG_PREVIOUS} if present)..."
docker image prune -f >/dev/null
log_ok "Cleanup complete."

echo
echo "========================================"
echo "    Deployment completed successfully   "
echo "========================================"
echo
echo "Application: http://localhost:${APP_PORT}"
if [ "$ROLLBACK_AVAILABLE" = true ]; then
    echo "Rollback:    ./rollback.sh  (restores ${APP_TAG_PREVIOUS})"
fi
echo

docker compose ps
