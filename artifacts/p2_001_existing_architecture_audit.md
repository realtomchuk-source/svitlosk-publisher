# P2-001 Existing Architecture Audit

## 1. Current Domain Map

| Entity/Value Object | Properties | Invariants | Methods | Lifecycle | Ownership |
|---------------------|------------|------------|---------|-----------|-----------|
| **`Edition`** | `Id`, `TargetDate`, `State` (Created, Active, Closed), `Publications` | Cannot modify publications if Closed | `Activate()`, `Close()`, `AddPublication()`, `RemovePublication()` | Created -> Active -> Closed | Aggregate Root |
| **`Publication`** | `Id`, `TerritoryId`, `Content`, `Classification` (Persistent, Ephemeral) | Immutable (record) | None | N/A | Owned by `Edition` |
| **`Territory`** | `Id`, `CanonicalName` | Immutable (record) | None | N/A | Independent |
| **`InputPackage`** | `RawPayload` | Immutable (record) | None | N/A | Independent |
| **`PublicationArtifact`** | `Territory`, `Classification`, `Content` | Immutable (record) | None | Execution output | Execution layer |

## 2. Current Core Map

- **`EditorialDecisionEngine`**: Evaluates a string-based `ReasonedConclusion` to produce an `EditorialDecision` (CREATE, UPDATE, DELETE, etc.). Operates purely on strings, lacking domain entity awareness.
- **`SynchronizationEngine`**: Empty shell (`throw new NotImplementedException()`).
- **`SynchronizationRegistry`**: Assumed empty shell (placeholder).

## 3. Existing Comparison Mechanisms

An exhaustive codebase search revealed:
- **`Equals(...)`**: 0 occurrences.
- **`SequenceEqual`**: 0 occurrences.
- **`Except` / `Intersect`**: 0 occurrences.
- **`Hash` / `SHA`**: 0 occurrences.
- **`Comparer` / `IEqualityComparer`**: 0 occurrences.
- **Normalization / Canonicalization**: 0 occurrences.
*(Note: Basic LINQ like `.FirstOrDefault` and `.Where(p => p.Territory != ...)` exist in `EditionAssembly`, but no collection equality algorithms exist).*

## 4. Existing State

The Publisher state is currently represented purely by the **`Edition`** aggregate, which holds a collection of **`Publication`** entities in memory.

## 5. Existing Extension Points

- **`SvitloSk.Publisher.Core` namespace**: Naturally hosts pure logic. The Situation Model fits here as a stateless service (e.g., `ISituationModel`) that takes `Edition` and `InputPackage` as arguments and returns detected Situations.
- **`SynchronizationEngine`**: Naturally serves as the runtime orchestrator that will invoke the Situation Model.

## 6. Architectural Risks

- The `Publication` entity lacks the properties (`Addresses`, `ContentHash`, `ScheduleHash`, `HasTomorrowForecast`, `CreatedAt`) required to perform any of the comparisons defined in the Situation Detection Matrix.
- `InputPackage` is purely a string container and lacks any structural typing (Text vs Graphic) or properties required for matrix evaluation.

## 7. Missing Implementation Points (FACTS ONLY)

- No cryptographic or structural hashing algorithm exists.
- No address equality or set-subtraction logic exists.
- No correlation mechanism for tracking InputPackage `PackageId` against Publication baseline exists.
- No `Situation` enum or representations exist.
- No structural representation of text or graphic input packages exists within the Domain layer (only raw JSON DTOs exist in `Contracts`).

