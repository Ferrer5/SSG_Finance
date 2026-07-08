#!/usr/bin/env bash

set -euo pipefail

log_info() {
    echo "[INFO] $1"
}

log_ok() {
    echo "[ OK ] $1"
}

log_warn() {
    echo "[WARN] $1"
}

log_error() {
    echo "[ERROR] $1"
}

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

# Create .env on first deployment
if [ ! -f ".env" ]; then
    log_info "Creating .env from .env.example..."
    cp .env.example .env

    log_ok ".env created."
    log_warn "Edit .env with your production values."
    log_warn "Run ./deploy.sh again after updating it."
    exit 1
fi

log_info "Loading environment variables..."
set -a
source .env
set +a
log_ok "Environment variables loaded."

log_info "Building Docker images..."
docker compose up -d --build
log_ok "Containers started."

log_info "Waiting for MySQL health check..."
until [ "$(docker inspect -f '{{.State.Health.Status}}' ssgfinance-mysql)" = "healthy" ]; do
    sleep 2
done
log_ok "MySQL is healthy."

log_info "Waiting for application health check..."
until [ "$(docker inspect -f '{{.State.Health.Status}}' ssgfinance-app)" = "healthy" ]; do
    sleep 2
done
log_ok "Application is healthy."

log_info "Validating Nginx configuration..."
docker exec ssgfinance-nginx nginx -t
log_ok "Nginx configuration is valid."

log_info "Checking application endpoint..."
curl --fail --silent "http://localhost:${APP_PORT}/" > /dev/null
log_ok "Application is reachable."

echo
echo "========================================"
echo "    Deployment completed successfully   "
echo "========================================"
echo
echo "Application: http://localhost:${APP_PORT}"
echo

docker compose ps