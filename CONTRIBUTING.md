# Contributing

All contributions to this repository must respect the "Spec-First" principle.

## Architecture Decision Workflow

If you discover that the codebase needs to behave differently than what the architecture allows, you **must not** change the code first.

Follow this strict workflow:
1. **Need architecture change?**
2. **Change Specification:** Submit a PR to the [svitlosk-specification](https://github.com/svitlosk/svitlosk-specification) repository.
3. **Review:** Architectural review occurs in the spec repository.
4. **Merge:** The spec is merged.
5. **Version Bump:** The specification receives a Semantic Version bump (Patch/Minor/Major).
6. **Implementation Update:** Update `.spec-version` in this repo and write the code.
7. **Conformance Tests:** Verify compliance.
8. **Release.**

> [!CAUTION]
> **Implementation PRs `MUST NOT` introduce architectural behavior that is absent from the specification.**
