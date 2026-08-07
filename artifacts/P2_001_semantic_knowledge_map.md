# P2-001 Semantic Knowledge Map

## 1. Situation Model Knowledge
| Situation | Trigger Condition | Required Inputs | Required Previous State | Resulting Decision | Spec Source |
|-----------|-------------------|-----------------|-------------------------|--------------------|-------------|
| **S-01** Morning Startup | Time / No active edition | None | Edition (None/Closed) | OPEN Edition | `EDITORIAL_SITUATION_MODEL.md` |
| **S-02** Changed Addresses | Address text differs | `TextInputPackage` | `Publication.Addresses` | UPDATE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-03** Removed Addresses | Fewer addresses | `TextInputPackage` | `Publication.Addresses` | UPDATE / REMOVE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-04** Added Addresses | More addresses | `TextInputPackage` | `Publication.Addresses` | UPDATE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-05** Territory Appeared | Territory in Input, not in Edition | `TextInputPackage` | `Edition.Publications` | CREATE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-06** Territory Disappeared | Territory in Edition, not in Input | `TextInputPackage` | `Edition.Publications` | REMOVE / UPDATE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-07** Tomorrow Forecast Appeared | Tomorrow forecast in Input | `TextInputPackage` | `Publication.HasTomorrowForecast` | CREATE (Tomorrow Pub) | `EDITORIAL_SITUATION_MODEL.md` |
| **S-08** Tomorrow Forecast Disappeared | Tomorrow forecast not in Input | `TextInputPackage` | `Publication.HasTomorrowForecast` | REMOVE (Tomorrow Pub) | `EDITORIAL_SITUATION_MODEL.md` |
| **S-09** Graphic Schedule Changed | Schedule data altered | `GraphicInputPackage` | `Publication.ScheduleHash` | REPLACE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-10** Technical Info Expired | Time elapsed > threshold | None | `Publication.CreatedAt` | REMOVE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-11** Cleanup Started | Time (End of day) | None | Edition (Active) | REMOVE (Ephemeral) | `EDITORIAL_SITUATION_MODEL.md` |
| **S-12** Edition Closing | Time (End of day) | None | Edition (Active) | CLOSE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-13** No Changes Detected | Input equals baseline | `InputPackage` | `Publication` (Baseline) | IGNORE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-14** External Producer Unavailable | Infrastructure error | None | Infrastructure state | TECHNICAL UPDATE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-15** Graphic Unavailable | Infrastructure error | None | Infrastructure state | TECHNICAL UPDATE | `EDITORIAL_SITUATION_MODEL.md` |
| **S-16** Comment Flood | Channel metrics | None | Telegram state | CLOSE COMMENTS | `EDITORIAL_SITUATION_MODEL.md` |
| **S-17** Unexpected Inconsistency | Deep structural mismatch | `InputPackage` | `Edition.Publications` | SYNCHRONIZE | `EDITORIAL_SITUATION_MODEL.md` |

## 2. Domain Knowledge
- **Edition**: Container of Publications. Serves as the aggregate root for daily editorial work. Never owns InputPackages. (`EDITORIAL_DOMAIN_MODEL.md`)
- **Publication**: Represents one editorial message for one Territory. Internal representation has `PublicationId`, `ContentHash`, `Addresses`, `HasTomorrowForecast`, `ScheduleHash`. (`EDITORIAL_DOMAIN_MODEL.md`)
- **Territory**: Geographic reference data unit for organization. Immutable. (`EDITORIAL_DOMAIN_MODEL.md`)
- **InputPackage**: The external authoritative stimulus (Text or Graphic). Owned by DSO. Immutable. (`EDITORIAL_INPUT_PACKAGE_MODEL.md`)

## 3. State Ownership
- **Edition State**: Owned by Publisher. Lifecycle: Planned -> Active -> Closed -> Archived. Transitioned exclusively by Edition Assembly based on Editorial Decisions.
- **Publication State**: Owned by Edition. Lifecycle: Created -> Ready -> Published -> Updated -> Archived/Removed. Transitioned exclusively by Edition Assembly based on Editorial Decisions.
- **InputPackage**: Owned by DSO. Completely immutable within Publisher context. Never modified.

## 4. Required Comparisons
- **Address Changes (S-02/03/04)**: `TextInputPackage.Addresses` (Left) vs `Publication.Addresses` (Right). Comparison rule: Set difference / text comparison. Spec Source: `EDITORIAL_SITUATION_MODEL.md`.
- **Territory Changes (S-05/06)**: `TextInputPackage.Territories` (Left) vs `Edition.Publications.TerritoryIdentifier` (Right). Comparison rule: Set inclusion/exclusion. Spec Source: `EDITORIAL_SITUATION_MODEL.md`.
- **Tomorrow Forecast (S-07/08)**: `TextInputPackage.HasTomorrowForecast` (Left) vs `Publication.HasTomorrowForecast` (Right). Comparison rule: Boolean equality. Spec Source: `EDITORIAL_SITUATION_MODEL.md`.
- **Graphic Changes (S-09)**: `GraphicInputPackage.ScheduleData` (Left) vs `Publication.ScheduleHash` (Right). Comparison rule: Hash equivalence. Spec Source: `EDITORIAL_SITUATION_MODEL.md`.
- **Steady State (S-13)**: `InputPackage.PackageId` / `InputPackage.Content` (Left) vs `Publication.ContentHash` (Right). Comparison rule: Identity correlation. Spec Source: `EDITORIAL_SITUATION_MODEL.md`.

## 5. Unknowns
1. **Schedule Data vs Schedule Hash**: How the system mathematically maps or hashes `GraphicInputPackage.ScheduleData` to compare against `Publication.ScheduleHash`. (Blocks S-09).
2. **S-13 Identity Correlation**: How the system concludes "No Changes Detected" using `PackageId` when the `Publication` does not store `PackageId`, or whether `ContentHash` completely replaces `PackageId` logic. (Blocks S-13).
3. **Address Equality Semantics**: The strict mathematical definition of how `Addresses` are considered equal, subset, or disjoint (e.g., canonical sorting, unordered set logic, normalization rules). (Blocks S-02, S-03, S-04).

