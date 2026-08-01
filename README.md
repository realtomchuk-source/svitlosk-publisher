# svitlosk-publisher

This repository contains the executable implementation of the Svitlosk Publisher.

> [!IMPORTANT]
> **Architecture Contract:**
> This repository is an implementation. It is **NOT** the architectural source of truth.
> 
> **Architecture:** [svitlosk-specification](https://github.com/svitlosk/svitlosk-specification)
> 
> If the implementation and the specification disagree, **the specification prevails**.

## Overview
This project is a concrete realization of the Universal Publisher Specification. It provides the Runtime engine, the Execution Handlers, the Domain models, and the Platform Adapters necessary to process publishing workloads.

## Structure
- `src/Domain/`: Implements the Business Truth (L1)
- `src/Handlers/`: Implements Execution Handlers (L2)
- `src/Adapters/`: Implements Platform Adapters (L3)
- `src/Runtime/`: Implements the Universal Runtime Engine (L4)
- `docs/`: Translational implementation documentation
- `tests/`: Conformance and Unit tests

## Implementation Status
See [IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md) for current progress against the specification.

## Roadmap
1. Scaffolding (Completed)
2. Domain Models implementation
3. Runtime Engine core
4. Telegram Adapter
5. Conformance testing validation
