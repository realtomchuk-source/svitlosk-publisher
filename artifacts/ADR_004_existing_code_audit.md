# ADR-004 Existing Code Audit

## Publisher Knowledge Discovered
An exhaustive semantic audit of the `svitlosk-publisher` repository reveals the following implementation reality:
- **`InputPackage`**: Implemented as a pure placeholder `public record InputPackage(string RawPayload);`. It contains no structured representation of `Addresses`, `ScheduleData`, `PackageId`, or any other semantics.
- **`Publication`**: Implemented as `public record Publication(Guid Id, string TerritoryId, string Content, PublicationClassification Classification);`. It explicitly lacks `Addresses`, `HasTomorrowForecast`, `ContentHash`, and `ScheduleHash`.
- **`Edition`**: Maintains a list of `Publication`s and manages active/closed states.
- **`EditorialDecisionEngine`**: Maps a text string (`ReasonedConclusion.Evaluation`) directly into an `EditorialDecision`. It performs no domain logic, state comparison, or delta computation.
- **`SynchronizationEngine`**: Implemented as a stub throwing `NotImplementedException`.
- **Helpers/Equality**: Zero hash functions, collection comparers, or equality implementations exist within the domain or execution layers.

## Specification Knowledge Discovered (Recap)
- The Situation Model requires detecting changes in addresses (S-02, S-03, S-04).
- The Situation Model requires detecting changes in graphic schedules (S-09).
- The Situation Model requires detecting steady-state (No Changes) (S-13).
- The `EDITORIAL_DOMAIN_MODEL.md` (recently extended) requires `Publication` to expose properties like `Addresses`, `ContentHash`, and `ScheduleHash`.

## Mapping Between Them
There is a massive discrepancy between the normative intent and the current implementation reality:
1. **Address Comparison (S-02/S-03/S-04)**: Cannot be implemented. The `Publication` entity in code does not have an `Addresses` property, and no equality helpers exist.
2. **Graphic Comparison (S-09)**: Cannot be implemented. The `Publication` entity in code does not have a `ScheduleHash`, nor does `GraphicInputPackage` have any hashing algorithms.
3. **Steady-state Detection (S-13)**: Cannot be implemented. `InputPackage` has no `PackageId` property in the domain representation, `Publication` has no `ContentHash`, and `SynchronizationEngine` is a stub.

## Exact Missing Information
The implementation lacks BOTH the structural properties and the algorithmic semantics:
- Missing Domain structural properties (`Addresses`, `ContentHash`, `ScheduleHash`, `HasTomorrowForecast`).
- Missing algorithmic definitions for computing Address set equality.
- Missing cryptographic or structural hashing definitions for `ScheduleHash`.
- Missing correlation definitions linking `PackageId` from DTOs to the `Publication` baseline.

## Final Verdict

### C
ADR-004 remains a true blocker.

The existing Publisher implementation contains no hidden comparison mechanics, equality helpers, or domain properties that could satisfy the Situation Model. The codebase is purely a structural shell at this layer. Architectural definition is strictly required before any implementation can proceed.
