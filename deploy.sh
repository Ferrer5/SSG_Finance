#!/usr/bin/env bash
set -euo pipefail

# Colors
RED="\033[0;31m"
GREEN="\033[0;32m"
YELLOW="\033[1;33m"
BLUE="\033[0;34m"
NC="\033[0m"

# Output Helpers
info()    { echo -e "${BLUE}[INFO]${NC} $1"; }
success() { echo -e "${GREEN}[ OK ]${NC} $1"; }
warning() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error()   { echo -e "${RED}[FAIL]${NC} $1"; exit 1; }

cd "$(dirname "$0")"

echo "=========================================="
echo "         SSG Finance Deployment"
echo "=========================================="

# Prerequisites
info "Checking prerequisites..."

[ -f .env ] || error ".env not found. Run: cp .env.example .env && edit it."
success ".env file found."

docker info > /dev/null 2>&1 || error "Docker daemon not running."
success "Docker is running."

# Pull Code
info "Pulling latest code..."
git pull
success "Code is up to date."

# Build & Start
info "Building and starting containers..."
docker compose up -d --build
success "All services are running."

# Summary
HOST_IP=$(hostname -I | awk '{print $1}')

echo ""
echo "=========================================="
echo "          Deployment Complete!"
echo "=========================================="
echo "  Server IP  : ${HOST_IP}"
echo "  App Port   : 8085"
echo "  Database   : MySQL 8.0"
echo ""
echo "  LAN Access : http://${HOST_IP}:8085"
echo "=========================================="
