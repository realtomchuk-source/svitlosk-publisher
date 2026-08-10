# SvitloSk.Publisher Deployment Guide

This is the standalone SvitloSk.Publisher repository. This worker service is responsible for claiming `publication intent` outbox messages from PostgreSQL and delivering them to Telegram.

## Prerequisites
- **Docker** and **Docker Compose**
- Telegram Bot Token
- Telegram Target Chat ID

## Configuration

The application requires valid Telegram configuration values. It will fail fast on startup if the secrets are missing or set to `YOUR_BOT_TOKEN_HERE` / `YOUR_CHAT_ID_HERE`.

**NEVER place real secrets in source control.**

Create a `.env` file (which is ignored by Git via `.dockerignore`) with the following environment variables:

```env
TELEGRAM_BOT_TOKEN=your_real_bot_token
TELEGRAM_CHAT_ID=your_real_chat_id
POSTGRES_PASSWORD=your_secure_password
```

See `.env.example` for a safe template.

## Database Migration (Operator Procedure)

The publisher does **NOT** auto-migrate the production database on startup to prevent conflicts during scaling. You must explicitly migrate the database before starting the workers.

Run the migration mode:

```bash
docker compose run --rm publisher dotnet SvitloSk.Publisher.Host.dll --migrate
```

This command parses `--migrate`, configures the DB context, runs `Database.Migrate()`, and exits with code `0`. It will **not** start Kestrel or background workers.

## Docker Build and Startup

1. Build the image:
```bash
docker build -t svitlosk-publisher .
```

2. Start the stack (PostgreSQL + Publisher):
```bash
docker compose up -d
```

## Observability & Health Endpoints

The publisher exposes health endpoints on port `8080`.

- **Liveness:** `http://localhost:8080/health/live`
  - Returns HTTP 200 OK if the application process is running. No external dependencies are checked.
- **Readiness:** `http://localhost:8080/health/ready`
  - Returns HTTP 200 OK if the PostgreSQL database is reachable.
  - Returns HTTP 503 Service Unavailable if PostgreSQL is down.

## Rollback Procedure

To roll back to the stable baseline before Docker deployability was introduced:

```bash
git checkout p5-029-stable-baseline
```

## Known Limitations

- **CREATE Operation**: Operations are `AT-LEAST-ONCE` because the Telegram Bot API lacks idempotency keys for message creation.
- **MEDIA_COLLECTION**: Not currently supported.
