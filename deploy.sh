#!/usr/bin/env bash

set -euo pipefail

trap 'echo; echo "Deployment failed. Showing recent logs..."; docker compose logs --tail=100' ERR

echo "=== SSG Finance Deployment ==="

# Create .env on first deployment
if [ ! -f ".env" ]; then
    echo "Creating .env from .env.example..."
    cp .env.example .env
    echo
    echo ".env has been created."
    echo "Edit .env with your production values, then run ./deploy.sh again."
    exit 1
fi

echo "Building images and starting services..."
docker compose up -d --build

echo "Waiting for MySQL..."
until [ "$(docker inspect -f '{{.State.Health.Status}}' ssgfinance-mysql)" = "healthy" ]; do
    sleep 2
done

echo "Waiting for application..."
until [ "$(docker inspect -f '{{.State.Health.Status}}' ssgfinance-app)" = "healthy" ]; do
    sleep 2
done

echo "Verifying Nginx configuration..."
docker exec ssgfinance-nginx nginx -t

echo "Verifying application..."
curl --fail --silent http://localhost:8085/ > /dev/null

echo
echo "Deployment completed successfully."
echo

docker compose ps