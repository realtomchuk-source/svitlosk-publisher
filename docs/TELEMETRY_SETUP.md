# Telemetry Setup

*(Translates `OBSERVABILITY_ARCHITECTURE.md` into implementation setup).*

The implementation uses OpenTelemetry to project Runtime facts.

## Setup
Telemetry must be initialized at application startup to capture:
- **Metrics:** Emitted to Prometheus.
- **Logs:** Structured JSON to Stdout (forwarded to ELK/Loki).
- **Traces:** Exported to Jaeger/Tempo.

All telemetry **must** attach the canonical `JobId` and `AttemptId` as attributes.
