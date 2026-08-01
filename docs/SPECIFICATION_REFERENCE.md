# Specification Reference

This document maps the physical code structure to the formal Architectural Specification IDs. Use these IDs in source code comments to establish a direct link to the canonical truth (e.g., `// Spec: PUB-L4-006`).

| Code Directory | Spec ID | Specification Name | Purpose |
| :--- | :--- | :--- | :--- |
| `src/Domain` | PUB-L1-001 | `DOMAIN_META_MODEL.md` | Defines business truth |
| `src/Handlers` | PUB-L2-001 | `EXECUTION_HANDLERS.md` | Defines execution actions |
| `src/Adapters` | PUB-L3-001 | `PLATFORM_ADAPTER_MODEL.md` | Defines integration |
| `src/Runtime/Objects` | PUB-L4-001 | `RUNTIME_OBJECTS.md` | Defines core data structures |
| `src/Runtime/StateMachine` | PUB-L4-006 | `RUNTIME_JOB_STATE_MACHINE.md` | Defines legal job transitions |
| `src/Runtime/Policies` | PUB-L4-008 | `RUNTIME_POLICIES.md` | Defines behavior and decisions |
| `src/Runtime/Persistence` | PUB-L4-010 | `RUNTIME_PERSISTENCE_MODEL.md` | Defines durable state |
| `src/Runtime/Lease` | PUB-L4-004 | `RUNTIME_CAPABILITIES.md` | Defines coordination |
