#!/bin/bash

set -euo pipefail

# =========================
# Paths
# =========================
PROJECT_DIR="/home/ssgfinance/projects/SSG_Finance"
BACKUP_DIR="/home/ssgfinance/projects/backups"
UPLOADS_DIR="$PROJECT_DIR/uploads"

# =========================
# Load environment
# =========================
set -a
source "$PROJECT_DIR/.env"
set +a

mkdir -p "$BACKUP_DIR"

DATE=$(date +"%Y-%m-%d_%H-%M-%S")

# =========================
# Database Backup (Only if changed)
# =========================
echo "Checking database..."

TMP_DUMP=$(mktemp)

docker exec ssgfinance-mysql \
    mysqldump -u root -p"$DB_ROOT_PASSWORD" "$DB_NAME" > "$TMP_DUMP"

CURRENT_DB_HASH=$(sha256sum "$TMP_DUMP" | awk '{print $1}')
DB_HASH_FILE="$BACKUP_DIR/.db_last_hash"

if [ ! -f "$DB_HASH_FILE" ] || [ "$CURRENT_DB_HASH" != "$(cat "$DB_HASH_FILE")" ]; then
    gzip -c "$TMP_DUMP" > "$BACKUP_DIR/${DB_NAME}_${DATE}.sql.gz"
    echo "$CURRENT_DB_HASH" > "$DB_HASH_FILE"
    echo "✓ Database changed. Backup created."
else
    echo "✓ Database unchanged. Skipping backup."
fi

rm -f "$TMP_DUMP"

# =========================
# Uploads Backup (Only if changed)
# =========================
echo "Checking uploads..."

UPLOADS_HASH_FILE="$BACKUP_DIR/.uploads_last_hash"

if [ -d "$UPLOADS_DIR" ]; then
    CURRENT_UPLOADS_HASH=$(
        find "$UPLOADS_DIR" -type f -print0 \
        | sort -z \
        | xargs -0 sha256sum 2>/dev/null \
        | sha256sum \
        | awk '{print $1}'
    )

    if [ ! -f "$UPLOADS_HASH_FILE" ] || [ "$CURRENT_UPLOADS_HASH" != "$(cat "$UPLOADS_HASH_FILE")" ]; then
        tar -czf "$BACKUP_DIR/${DB_NAME}_uploads_${DATE}.tar.gz" -C "$UPLOADS_DIR" .
        echo "$CURRENT_UPLOADS_HASH" > "$UPLOADS_HASH_FILE"
        echo "✓ Uploads changed. Backup created."
    else
        echo "✓ Uploads unchanged. Skipping backup."
    fi
else
    echo "Uploads directory not found. Skipping."
fi

# =========================
# Cleanup
# =========================
echo "Cleaning old backups..."

# Delete database backups older than 3 days
find "$BACKUP_DIR" -type f -name "*.sql.gz" -mtime +3 -delete

# Delete uploads backups older than 3 days
find "$BACKUP_DIR" -type f -name "*_uploads_*.tar.gz" -mtime +3 -delete

echo "Cleanup completed."


# NOTE: The following commands are commented out because they are for restoring the database and uploads from backups. Uncomment and modify the paths as needed to restore.

# Restore database
# gunzip -c /path/to/backup.sql.gz | docker exec -i ssgfinance-mysql mysql -u root -p"$DB_ROOT_PASSWORD" "$DB_NAME"

# Restore uploads
# tar -xzf /path/to/uploads.tar.gz -C /home/ssgfinance/projects/SSG_Finance