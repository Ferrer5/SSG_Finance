#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

log()  { echo -e "\n\033[1;32m==>\033[0m $*"; }
warn() { echo -e "\033[1;33mWARN:\033[0m $*"; }
die()  { echo -e "\033[1;31mERROR:\033[0m $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "Run with sudo:  sudo bash deploy-native.sh"

[ -f .env ] || die ".env not found. Run:  cp .env.native.example .env  then edit it."
set -a; . ./.env; set +a

: "${DB_NAME:?DB_NAME missing in .env}"
: "${DB_USER:?DB_USER missing in .env}"
: "${DB_PASSWORD:?DB_PASSWORD missing in .env}"
APP_PORT="${APP_PORT:-8085}"
APP_DIR="${APP_DIR:-/opt/ssg}"
SERVICE_USER="${SERVICE_USER:-www-data}"
SMTP_HOST="${SMTP_HOST:-}"
SMTP_PORT="${SMTP_PORT:-587}"
SMTP_USERNAME="${SMTP_USERNAME:-}"
SMTP_PASSWORD="${SMTP_PASSWORD:-}"
SERVICE_NAME="ssg"

[ "$DB_PASSWORD" = "change_me_to_a_strong_password" ] && \
  die "Set a real DB_PASSWORD in .env before deploying."

ensure_dotnet() {
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    log ".NET 10 SDK already installed."
    return
  fi
  log "Installing .NET 10 SDK..."
  apt-get update -y
  if apt-get install -y dotnet-sdk-10.0; then return; fi

  warn "dotnet-sdk-10.0 not in the default feed; adding the Microsoft package repo."
  . /etc/os-release
  local deb="/tmp/packages-microsoft-prod.deb"
  wget -q "https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb" -O "$deb" \
    || die "Could not download the Microsoft package repo for Ubuntu ${VERSION_ID}."
  dpkg -i "$deb"
  apt-get update -y
  apt-get install -y dotnet-sdk-10.0 \
    || die "Failed to install .NET 10 SDK. Install it manually, then re-run."
}

ensure_mysql() {
  if dpkg -l 2>/dev/null | grep -q '^ii  mysql-server'; then
    log "MySQL already installed."
  else
    log "Installing MySQL server..."
    DEBIAN_FRONTEND=noninteractive apt-get install -y mysql-server
  fi
  systemctl enable --now mysql
}

setup_database() {
  log "Provisioning database '${DB_NAME}' and user '${DB_USER}'..."
  mysql --protocol=socket -uroot <<SQL
CREATE DATABASE IF NOT EXISTS \`${DB_NAME}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${DB_USER}'@'localhost' IDENTIFIED BY '${DB_PASSWORD}';
ALTER USER '${DB_USER}'@'localhost' IDENTIFIED BY '${DB_PASSWORD}';
GRANT ALL PRIVILEGES ON \`${DB_NAME}\`.* TO '${DB_USER}'@'localhost';
FLUSH PRIVILEGES;
SQL
}

publish_app() {
  log "Publishing application to ${APP_DIR}..."
  systemctl stop "${SERVICE_NAME}" 2>/dev/null || true
  mkdir -p "${APP_DIR}"
  dotnet publish -c Release -o "${APP_DIR}"

  mkdir -p "${APP_DIR}/wwwroot/uploads/expenses" "${APP_DIR}/wwwroot/uploads/avatars"

  id "${SERVICE_USER}" >/dev/null 2>&1 || die "Service user '${SERVICE_USER}' does not exist."
  chown -R "${SERVICE_USER}:${SERVICE_USER}" "${APP_DIR}"
}

install_service() {
  log "Installing systemd service '${SERVICE_NAME}'..."
  local unit="/etc/systemd/system/${SERVICE_NAME}.service"
  local conn="server=localhost;port=3306;database=${DB_NAME};uid=${DB_USER};pwd=${DB_PASSWORD};"

  local smtp_block=""
  if [ -n "${SMTP_HOST}" ]; then
    smtp_block=$(cat <<SMTP
Environment=SmtpSettings__Host=${SMTP_HOST}
Environment=SmtpSettings__Port=${SMTP_PORT}
Environment=SmtpSettings__UserName=${SMTP_USERNAME}
Environment=SmtpSettings__Password=${SMTP_PASSWORD}
Environment=SmtpSettings__EnableSsl=true
SMTP
)
  else
    warn "SMTP_HOST is blank — password-reset email will be disabled."
  fi

  cat > "${unit}" <<UNIT
[Unit]
Description=SSG Finance (ASP.NET Core, native)
After=network.target mysql.service
Requires=mysql.service

[Service]
WorkingDirectory=${APP_DIR}
ExecStartPre=/bin/sh -c 'fuser -k ${APP_PORT}/tcp 2>/dev/null || true'
ExecStart=/usr/bin/dotnet ${APP_DIR}/MyMvcApp.dll
Restart=always
RestartSec=10
User=${SERVICE_USER}
KillSignal=SIGINT
SyslogIdentifier=${SERVICE_NAME}
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:${APP_PORT}
Environment=DOTNET_NOLOGO=true
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment="ConnectionStrings__DefaultConnection=${conn}"
${smtp_block}

[Install]
WantedBy=multi-user.target
UNIT

  chmod 600 "${unit}"
  systemctl daemon-reload
  systemctl enable "${SERVICE_NAME}"
  fuser -k "${APP_PORT}/tcp" 2>/dev/null || true
  systemctl restart "${SERVICE_NAME}"
}

open_firewall() {
  if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
    log "Opening port ${APP_PORT}/tcp in ufw..."
    ufw allow "${APP_PORT}/tcp" || true
  fi
}

ensure_dotnet
ensure_mysql
setup_database
publish_app
install_service
open_firewall

sleep 3
HOST_IP="$(hostname -I | awk '{print $1}')"
GIT_HASH=$(git -C "$SCRIPT_DIR" rev-parse --short HEAD 2>/dev/null || echo "N/A")
GIT_BRANCH=$(git -C "$SCRIPT_DIR" rev-parse --abbrev-ref HEAD 2>/dev/null || echo "N/A")
SERVICE_STARTED=$(systemctl show -p ActiveEnterTimestamp --value "${SERVICE_NAME}" 2>/dev/null || echo "N/A")
DOTNET_VER=$(dotnet --version 2>/dev/null || echo "N/A")
MYSQL_VER=$(mysql --version 2>/dev/null | awk '{print $5}' | tr -d ',' || echo "N/A")
HOSTNAME=$(hostname 2>/dev/null || echo "N/A")
OS_INFO=$(lsb_release -d -s 2>/dev/null || { . /etc/os-release 2>/dev/null && echo "$PRETTY_NAME"; } || echo "N/A")
SERVER_UPTIME=$(uptime -p 2>/dev/null | sed 's/up //' || echo "N/A")
DISK_FREE=$(df -h / 2>/dev/null | awk 'NR==2{print $4}' || echo "N/A")
MEM_FREE=$(free -h 2>/dev/null | awk '/^Mem:/{print $7}' || echo "N/A")
APP_DIR_SIZE=$(du -sh "$APP_DIR" 2>/dev/null | awk '{print $1}' || echo "N/A")
echo
echo "========================================================"
if systemctl is-active --quiet "${SERVICE_NAME}"; then
  echo "  SSG Finance is RUNNING."
else
  echo "  Service failed to start. Check:  journalctl -u ${SERVICE_NAME} -e"
fi
echo "  LAN access:   http://${HOST_IP}:${APP_PORT}"
echo "  Commit:       ${GIT_HASH} (${GIT_BRANCH})"
echo "  Database:     ${DB_NAME}"
echo "  Started:      ${SERVICE_STARTED}"
echo "  .NET:         ${DOTNET_VER}"
echo "  MySQL:        ${MYSQL_VER}"
echo "  Hostname:     ${HOSTNAME}"
echo "  OS:           ${OS_INFO}"
echo "  Uptime:       ${SERVER_UPTIME}"
echo "  Disk free:    ${DISK_FREE}"
echo "  Memory free:  ${MEM_FREE}"
echo "  App dir:      ${APP_DIR_SIZE}  ${APP_DIR}"
echo
echo "  Status:  systemctl status ${SERVICE_NAME}"
echo "  Logs:    journalctl -u ${SERVICE_NAME} -f"
echo "========================================================"
