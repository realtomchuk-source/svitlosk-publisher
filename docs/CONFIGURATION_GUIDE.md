# Configuration Guide

*(Translates `OPERATIONAL_CONFIGURATION.md` into implementation guidance).*

This repository uses environment variables (`.env`) for operational configuration. 
In accordance with the specification, configuration **only** defines the environment; it **never** redefines Runtime Policies.

## Example `.env` Configuration
```env
# Operational Connectivity
DATABASE_URL=postgres://user:pass@localhost:5432/svitlosk
REDIS_URL=redis://localhost:6379

# Identity
NODE_ID=worker-node-01

# L3 Platform Config
TELEGRAM_BOT_TOKEN=secret
```
