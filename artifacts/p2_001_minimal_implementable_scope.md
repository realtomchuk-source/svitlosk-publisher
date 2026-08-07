# P2-001 Minimal Implementable Scope

## 1. Already Existing
The following existing elements in the Publisher repository can be directly reused:
- `SvitloSk.Publisher.Domain.Edition`: Provides current Edition state (Active/Closed) for S-01, S-11, S-12.
- `SvitloSk.Publisher.Domain.InputPackage`: Baseline input argument.
- `SvitloSk.Publisher.Domain.Publication`: Baseline entity in Edition collections.
- `SvitloSk.Publisher.Core.EditorialDecisionEngine`: Downstream consumer (will eventually consume the output of the Situation Model).

## 2. Directly Implementable
The following pieces of P2-001 can be coded immediately without any architectural assumptions:

### Situation Enum definition
- **Required files**: `src/SvitloSk.Publisher.Core/Situation.cs`
- **Existing dependencies**: None.
- **Reason it is safe**: S-01 through S-17 are fully and rigidly defined in the Specification.

### Infrastructure State definition
- **Required files**: `src/SvitloSk.Publisher.Core/InfrastructureState.cs`
- **Existing dependencies**: None.
- **Reason it is safe**: Infrastructure error triggers (S-14, S-15, S-16) are clearly defined in the Specification as boolean states (Unavailable, Flood).

### ISituationModel interface
- **Required files**: `src/SvitloSk.Publisher.Core/ISituationModel.cs`
- **Existing dependencies**: `Edition`, `InputPackage`, `InfrastructureState`.
- **Reason it is safe**: The input/output contract (takes current state, returns Situations) is structurally dictated by the existing normative definition.

### SituationModel partial implementation
- **Required files**: `src/SvitloSk.Publisher.Core/SituationModel.cs`
- **Implementable Situations**: S-01 (Morning Startup), S-11 (Cleanup Started), S-12 (Edition Closing), S-14 (External Producer Down), S-15 (Graphic Unavailable), S-16 (Comment Flood).
- **Existing dependencies**: `Edition` state, `InfrastructureState`, System Time.
- **Reason it is safe**: These situations depend strictly on `Edition.State`, Time thresholds, or explicit `InfrastructureState` booleans. They do not depend on the missing structural properties inside `Publication` or `InputPackage`.

## 3. Blocked
The following elements remain completely blocked:

### Address Comparisons (S-02, S-03, S-04)
- **Existing ADR**: ADR-004.
- **Missing Information**: `Publication.Addresses` property missing. Mathematical/set equality rules for Addresses are undefined.

### Territory Transitions (S-05, S-06)
- **Existing ADR**: N/A (Blocked by Domain gap).
- **Missing Information**: `InputPackage` is currently just a string `RawPayload`. No normative mapping exists defining how to extract a structural Territory list into the Domain without inventing DTO parsing logic inside the Core.

### Tomorrow Forecast Transitions (S-07, S-08)
- **Existing ADR**: N/A (Blocked by Domain gap).
- **Missing Information**: `Publication.HasTomorrowForecast` is missing from the codebase.

### Graphic Schedule Comparison (S-09)
- **Existing ADR**: ADR-004.
- **Missing Information**: Cryptographic/structural hashing rules for `ScheduleData` vs `ScheduleHash` are undefined.

### Technical Info Expired (S-10)
- **Existing ADR**: N/A (Blocked by Domain gap).
- **Missing Information**: `Publication.CreatedAt` is missing from the codebase.

### Steady State Detection (S-13)
- **Existing ADR**: ADR-004.
- **Missing Information**: Correlation logic linking `InputPackage.PackageId` to `Publication.ContentHash`.

## 4. Dependency Graph
Step 1: Define `Situation` enum and `InfrastructureState` record
v
Step 2: Define `ISituationModel` interface
v
Step 3: Implement `SituationModel` (S-01, S-11, S-12, S-14, S-15, S-16 only)
v
Step 4: Add unit tests for implemented situations

## 5. Earliest Coding Entry Point
**File**: `src/SvitloSk.Publisher.Core/Situation.cs`
**Explanation**: Defining the enumerations and the `DetectedSituation` record creates the fundamental vocabulary required by the entire module. It requires zero dependencies, zero architectural decisions, and perfectly reflects the frozen Specification.

## 6. Excluded Work
The following must **NOT** be touched during this implementable slice:
- Modifying `SvitloSk.Publisher.Domain.Publication` (blocked).
- Modifying `SvitloSk.Publisher.Domain.InputPackage` (blocked).
- Implementing S-02, S-03, S-04, S-05, S-06, S-07, S-08, S-09, S-10, S-13, S-17.
- Modifying `SvitloSk.Publisher.Runtime.SynchronizationEngine` (blocked by full situation model integration).
- Modifying `SvitloSk.Publisher.Core.EditorialDecisionEngine`.

