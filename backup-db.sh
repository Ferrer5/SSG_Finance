#!/bin/bash

# Paths
PROJECT_DIR="/home/ssgfinance/projects/SSG_Finance"
BACKUP_DIR="/home/ssgfinance/projects/backups"
UPLOADS_DIR="$PROJECT_DIR/uploads"

# Load environment
set -a
source "$PROJECT_DIR/.env"
set +a

mkdir -p "$BACKUP_DIR"

DATE=$(date +"%Y-%m-%d_%H-%M-%S")

# Database
DB_BACKUP_FILE="$BACKUP_DIR/${DB_NAME}_${DATE}.sql"
docker exec ssgfinance-mysql mysqldump -u root -p"$DB_ROOT_PASSWORD" "$DB_NAME" > "$DB_BACKUP_FILE"
gzip "$DB_BACKUP_FILE"

# Uploads
UPLOADS_BACKUP_FILE="$BACKUP_DIR/${DB_NAME}_uploads_${DATE}.tar.gz"
tar -czf "$UPLOADS_BACKUP_FILE" -C "$UPLOADS_DIR" .

# Cleanup
find "$BACKUP_DIR" -type f \( -name "*.sql.gz" -o -name "*_uploads_*.tar.gz" \) -mtime +30 -delete


# NOTE: The following commands are commented out because they are for restoring the database and uploads from backups. Uncomment and modify the paths as needed to restore.

# Restore database
# gunzip -c /path/to/backup.sql.gz | docker exec -i ssgfinance-mysql mysql -u root -p"$DB_ROOT_PASSWORD" "$DB_NAME"

# Restore uploads
# tar -xzf /path/to/uploads.tar.gz -C /home/ssgfinance/projects/SSG_Finance