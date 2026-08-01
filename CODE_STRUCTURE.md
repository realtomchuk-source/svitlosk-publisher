# Code Structure

This document explains **why** the physical directories in this repository exist and how they map to the abstract Canonical Specification.

## Mapping

- **`src/Domain/`**: Implements **Phase L1 (Domain Model)**. Contains the pure, infrastructure-agnostic business logic and entities.
- **`src/Handlers/`**: Implements **Phase L2 (Execution Handlers)**. Contains the discrete actions that mutate the Domain model.
- **`src/Adapters/`**: Implements **Phase L3 (Platform Adapters)**. Contains the integration layer (e.g., Telegram bots).
- **`src/Runtime/`**: Implements **Phase L4 (Runtime Architecture)**. Contains the universal execution engine, policies, and state machine.
- **`tests/Conformance/`**: Implements **Phase L4 Conformance**. Contains the automated test suite proving mathematical compliance with `RUNTIME_CONFORMANCE_VALIDATION.md`.
