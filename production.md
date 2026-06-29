# SSG Finance — Production Deployment Guide

## Prerequisites

- Docker & Docker Compose v2
- Git
- Linux server (Ubuntu recommended) on the school LAN

## Setup

1. Clone the repo: `git clone https://github.com/Ferrer5/SSG_Finance.git`
2. Copy `.env.example` to `.env` and fill in secrets
3. Run `bash deploy.sh`

## Accessing the Application

- URL: `http://<server-ip>:8085`
- Default admin: `admin@ssg.com` / `admin123` (change after first login)

## Accessing MySQL

- `docker exec -it ssg_finance-db-1 mysql -u ssgfinance -p`
- Check container name with `docker ps` if different

## Useful Commands

- View logs: `docker compose logs -f`
- Restart app: `docker compose restart app`
- Stop all: `docker compose down`
- Database backup: `mysqldump ssg_system > ssg_$(date +%F).sql`

## Troubleshooting

- App not responding: `docker compose ps` and `docker compose logs app`
- MySQL not healthy: `docker compose logs db`
