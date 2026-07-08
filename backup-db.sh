#!/bin/bash

PROJECT_DIR="/home/ssgfinance/projects/SSG_Finance"
BACKUP_DIR="/home/ssgfinance/projects/backups"

# Load environment variables
set -a
source "$PROJECT_DIR/.env"
set +a

mkdir -p "$BACKUP_DIR"

DATE=$(date +"%Y-%m-%d_%H-%M-%S")
BACKUP_FILE="$BACKUP_DIR/${DB_NAME}_${DATE}.sql"

docker exec ssgfinance-mysql \
    mysqldump \
    -u root \
    -p"$DB_ROOT_PASSWORD" \
    "$DB_NAME" > "$BACKUP_FILE"

gzip "$BACKUP_FILE"

# Delete backups older than 30 days
find "$BACKUP_DIR" -type f -name "*.sql.gz" -mtime +30 -delete