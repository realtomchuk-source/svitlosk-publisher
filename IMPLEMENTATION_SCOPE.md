# Implementation Scope

**Purpose:** This document clarifies exactly what belongs in this repository and what is strictly prohibited.

## Included (IN SCOPE)
This repository contains executable code only. It includes:
- Production code implementing the Domain, Runtime, and Adapters.
- Automated tests (Conformance, Unit, Integration).
- Deployment scripts (Docker, Kubernetes).
- Configuration templates (`.env.example`).
- Implementation documentation (How to build, test, and deploy).

## Excluded (OUT OF SCOPE)
This repository **intentionally does not contain**:
- Architecture Decisions
- Domain Specification
- Runtime Specification
- Operational Specification

These remain authoritative in [svitlosk-specification](https://github.com/svitlosk/svitlosk-specification). **Do not duplicate them here.**
