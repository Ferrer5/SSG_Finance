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

info "Validating .env configuration..."
for VAR in APP_PORT MYSQL_DATABASE MYSQL_USER MYSQL_PASSWORD MYSQL_ROOT_PASSWORD; do
    VAL=$(grep -E "^${VAR}=" .env | cut -d'=' -f2- | tr -d "'\" " || true)
    if [ -z "$VAL" ]; then
        error "Missing or empty required .env variable: $VAR"
    fi
done
success ".env configuration is valid."

docker info > /dev/null 2>&1 || error "Docker daemon not running."
success "Docker is running."

docker compose version > /dev/null 2>&1 || error "Docker Compose v2 not found. Install: https://docs.docker.com/compose/install/"
success "Docker Compose v2 is available."

command -v git > /dev/null 2>&1 || error "Git not installed."
success "Git is available."

# --- Production Safety Checks ---
info "Running production safety checks..."

BRANCH=$(git rev-parse --abbrev-ref HEAD)
if [ "$BRANCH" != "main" ]; then
    error "Deploy must be run on the 'main' branch (currently on '$BRANCH')."
fi
success "On main branch."

if ! git diff-index --quiet HEAD --; then
    echo ""
    warning "Deployment aborted — dirty working tree."
    echo ""
    echo "  Uncommitted changes detected:"
    git status --short
    echo ""
    echo "  Production requires a clean checkout. To proceed:"
    echo "    git stash                  # temporarily shelve changes"
    echo "    git add . && git commit    # commit intentional changes"
    echo ""
    error "Aborting deployment."
fi
success "Working tree is clean."

UPSTREAM="@{u}"
LOCAL=$(git rev-parse @)
REMOTE=$(git rev-parse "$UPSTREAM" 2>/dev/null || echo "")
if [ -z "$REMOTE" ]; then
    warning "No upstream branch configured for '$BRANCH'. Skipping sync check."
elif [ "$LOCAL" != "$REMOTE" ]; then
    error "Local branch is not in sync with remote. Run 'git pull --ff-only' first."
fi
success "Branch is in sync with remote."

# --- Pull Code ---
info "Pulling latest code (fast-forward only)..."
git pull --ff-only
success "Code is up to date."

# --- Pre-Deploy Database Backup ---
BACKUP_DIR="deploy-backups"
mkdir -p "$BACKUP_DIR"
DB_CONTAINER=$(docker compose ps --format '{{.Name}}' --status running 2>/dev/null | grep -- '-db-' | head -1) || DB_CONTAINER=""
DB_NAME=$(grep -E '^MYSQL_DATABASE=' .env | cut -d'=' -f2 | tr -d "'\"")
DB_USER=$(grep -E '^MYSQL_USER=' .env | cut -d'=' -f2 | tr -d "'\"")
DB_PASS=$(grep -E '^MYSQL_PASSWORD=' .env | cut -d'=' -f2- | tr -d "'\"")
if [ -n "$DB_CONTAINER" ] && docker ps --format '{{.Names}}' | grep -q "^${DB_CONTAINER}$"; then
    DB_HEALTH=$(docker inspect --format='{{.State.Health.Status}}' "$DB_CONTAINER" 2>/dev/null || echo "unknown")
    if [ "$DB_HEALTH" = "healthy" ]; then
        BACKUP_FILE="${BACKUP_DIR}/ssg_finance_$(date +%Y%m%d_%H%M%S).sql.gz"
        info "Backing up database to ${BACKUP_FILE}..."
        docker exec "$DB_CONTAINER" mysqldump -u"$DB_USER" -p"$DB_PASS" "$DB_NAME" 2>/dev/null | gzip > "$BACKUP_FILE" || warning "Database backup failed (continuing deploy)."
        success "Database backup saved."
    else
        warning "Database container exists but is not healthy. Skipping backup."
    fi
else
    info "No existing database container found. Skipping backup."
fi

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
    DB_HEALTH=$(docker inspect --format='{{.State.Health.Status}}' "$DB_CONTAINER" 2>/dev/null || echo "unknown")
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

# --- App Health Check ---
info "Waiting for application to become responsive..."
APP_PORT_CHECK=$(grep -E '^APP_PORT=' .env | cut -d'=' -f2 || echo "8085")
APP_MAX_WAIT=60
APP_ELAPSED=0
while [ $APP_ELAPSED -lt $APP_MAX_WAIT ]; do
    if curl -sf "http://localhost:${APP_PORT_CHECK}" > /dev/null 2>&1; then
        success "Application is responding."
        break
    fi
    sleep 2
    APP_ELAPSED=$((APP_ELAPSED + 2))
    echo -ne "\r  ${BLUE}[INFO]${NC} Waiting for app... ${APP_ELAPSED}s / ${APP_MAX_WAIT}s"
done
if [ $APP_ELAPSED -ge $APP_MAX_WAIT ]; then
    warning "App health check timed out after ${APP_MAX_WAIT}s. Check 'docker logs ssg-finance-app-1'."
fi

# --- Cleanup ---
info "Pruning dangling Docker images..."
docker image prune -f > /dev/null 2>&1 && success "Old images cleaned up." || warning "Image prune skipped."

# --- Summary ---
# Detect host IP (portable across Ubuntu variants)
HOST_IP=$(ip route get 1 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="src") print $(i+1)}' || hostname -I | awk '{print $1}' || echo "localhost")
APP_PORT=$(grep -E '^APP_PORT=' .env | cut -d'=' -f2 || echo "8085")

echo ""
echo "=========================================="
echo "          Deployment Complete!            "
echo "=========================================="
echo "Version     : $(git describe --always --tags)"
echo "Commit      : $(git rev-parse --short HEAD)"
echo "Branch      : ${BRANCH}"
echo "Server      : ${HOST_IP}"
echo "Application : http://${HOST_IP}:${APP_PORT}"
echo "Database    : ${DB_NAME}"
echo "=========================================="
