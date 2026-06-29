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

# Trap for clean error output
trap 'error "Deployment failed at line $LINENO."' ERR

cd "$(dirname "$0")"

echo "=========================================="
echo "         SSG Finance Deployment"
echo "=========================================="

# --- Prerequisites ---
info "Checking prerequisites..."

[ -f .env ] || error ".env not found. Run: cp .env.example .env && edit it."
success ".env file found."

docker info > /dev/null 2>&1 || error "Docker daemon not running."
success "Docker is running."

docker compose version > /dev/null 2>&1 || error "Docker Compose v2 not found. Install: https://docs.docker.com/compose/install/"
success "Docker Compose v2 is available."

command -v git > /dev/null 2>&1 || error "Git not installed."
success "Git is available."

# --- Pull Code ---
info "Pulling latest code..."
git pull
success "Code is up to date."

# --- Build & Start ---
info "Building and starting containers..."
docker compose up -d --build --remove-orphans
success "Containers started."

# --- Wait for MySQL health ---
info "Waiting for MySQL to become healthy..."
MYSQL_MAX_WAIT=60
MYSQL_ELAPSED=0
while [ $MYSQL_ELAPSED -lt $MYSQL_MAX_WAIT ]; do
    # Check if the db container's healthcheck is passing
    DB_HEALTH=$(docker inspect --format='{{.State.Health.Status}}' ssg-finance-db-1 2>/dev/null || echo "unknown")
    if [ "$DB_HEALTH" = "healthy" ]; then
        success "MySQL is healthy."
        break
    fi
    sleep 2
    MYSQL_ELAPSED=$((MYSQL_ELAPSED + 2))
    echo -ne "\r  ${BLUE}[INFO]${NC} Waiting for MySQL... ${MYSQL_ELAPSED}s / ${MYSQL_MAX_WAIT}s"
done

if [ $MYSQL_ELAPSED -ge $MYSQL_MAX_WAIT ]; then
    warning "MySQL health check timed out after ${MYSQL_MAX_WAIT}s. The app may still be starting."
fi

# --- Summary ---
# Detect host IP (portable across Ubuntu variants)
HOST_IP=$(ip route get 1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="src") print $(i+1)}' || hostname -I | awk '{print $1}' || echo "localhost")
APP_PORT=$(grep -E '^APP_PORT=' .env | cut -d'=' -f2 || echo "8085")

echo ""
echo "=========================================="
echo "          Deployment Complete!"
echo "=========================================="
echo "  Server IP  : ${HOST_IP}"
echo "  App Port   : ${APP_PORT}"
echo "  Database   : MySQL 8.0"
echo ""
echo "  LAN Access : http://${HOST_IP}:${APP_PORT}"
echo "=========================================="
