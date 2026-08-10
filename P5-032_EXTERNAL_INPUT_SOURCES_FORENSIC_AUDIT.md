# P5-032 Forensic Audit Report: External Input Sources

## 1. Current Publisher Baseline (P5-031 State)
- **Repository Path:** `SvitloSk.Publisher` (Standalone)
- **HEAD:** `fb7249dd731a116acab8299fe2a0c6ae65f8ab25`
- **Baseline Tag:** `p5-030-stable-baseline` (Matches HEAD)
- **Worktree Status:** CLEAN (ignoring untracked audit reports)
- **Build:** PASS
- **Tests:** 94/94 PASS
- **Docker:** AVAILABLE

## 2. External Source Repositories & Exact Paths
**1. SvitloSk (GRAPH / JSON SOURCE)**
- **Path:** `parser/tg_posts/`
- **Actual File Discovered:** `2026-08-04.json`
- **HTTP Raw URL Pattern:** `https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts/{yyyy-MM-dd}.json`

**2. OutagesSk (TEXT SOURCE)**
- **Path:** `data/tg_posts/`
- **Actual Files Discovered:** `today.txt`, `tomorrow.txt`
- **HTTP Raw URL Pattern:** `https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/today.txt`

## 3. Discovered File & Data Structures
- **SvitloSk JSON Structure:** Contains fields `"date"`, `"updated_at"`, `"mode"`, `"message"`, `"queues"` (a mapping of queue identifiers like `"1.1"` to a 24-character string of states, likely hour-by-hour), and `"meta"` (containing `"generated_at"`, `"state"`, `"target_date"`).
- **OutagesSk Text Structure:** Contains fully pre-rendered, Telegram-formatted markdown text with grouped streets under settlements (e.g., `- вул. Болохівська: буд. 1, 2...`).

## 4. Current Provider Behavior & Real Input Flow
- **Current Provider:** `RealOutagesSkInputPackageProvider`
- **Current Behavior:** It ignores the `tg_posts` directories entirely. Instead, it makes an HTTP GET to `https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/outages_snapshot.json`. It attempts to parse this snapshot into structured `EventDto` objects (Settlement, Streets, Intervals) and maps them into the `InputPackage` domain entity.
- **Complete Real Input Flow:** 
  `outages_snapshot.json (raw HTTP)` → `RealOutagesSkInputPackageProvider` → `InputPackage` → `EditionAssembly` → `SynchronizationEngine` (diff evaluation) → `ReasonedConclusion` → `EditorialDecisionEngine` → `EditorialDecision` → `OutboxMessage` (PostgreSQL) → `OutboxDispatcherWorker` → `PublicationPipeline` → `TelegramAdapter` → `Telegram API`.

## 5. Mismatches & Gaps
1. **Source Target Mismatch:** The current provider targets `outages_snapshot.json`, completely missing the newly specified boundary (`tg_posts/*.txt` and `tg_posts/*.json`).
2. **Format Abstraction Mismatch:** The real external boundary provides pre-rendered Telegram text (`today.txt`) and high-level schedule metadata (`2026-08-04.json`). However, the `InputPackage` domain model expects highly granular structured data (`IReadOnlyCollection<Event>` with `Streets` and `Intervals`).
3. **Single vs Dual Source:** The publisher currently assumes a single monolithic data source (`outages_snapshot.json`). It lacks the capability to fetch from two independent repositories and compose them.

## 6. Correlation Analysis & Determinism (Dual-Source Risk)
- Because JSON data and Text data reside in two *independent* GitHub repositories, they are updated via separate Git commits and GitHub Actions.
- **Race Conditions:** `today.txt` might be updated before `2026-08-04.json` is pushed. If the Publisher pulls during this window, it will fetch mismatched states.
- **Determinism Failure:** Without explicit correlation (e.g., matching `updated_at` or `target_date` timestamps between the two sources), the Publisher risks generating an `InputPackage` that combines stale images with fresh text, violating determinism and safety.

## 7. Failure-Mode Analysis
The current provider relies on `HttpClient.GetStringAsync`. 
- **Source Unavailable (404 / 503):** Throws `HttpRequestException`, which is caught, logged, and re-thrown. The polling cycle fails safely without producing a corrupted `InputPackage`.
- **Malformed JSON:** `JsonSerializer.Deserialize` throws `JsonException`. Fails safely.
- **Missing/Mismatched Dual Data (Future):** The current provider cannot handle dual-source failure because it only queries one file. A dual-source provider must implement atomic failure (if one source 404s, the entire composition must fail).

## 8. Production Input Readiness Verdict
**VERDICT: NOT READY FOR NEW BOUNDARY**
The current `SvitloSk.Publisher` is highly stable and deployable for its *current* abstraction (`outages_snapshot.json`), but it is structurally incapable of consuming the true dual-repository `tg_posts` boundary without a new composition layer.

## 9. Recommended Next Task
**NEXT TASK: Implement Dual-Source Input Composition Provider**
Develop a new `IInputPackageProvider` implementation that explicitly fetches from both `SvitloSk/parser/tg_posts/{date}.json` and `OutagesSk/data/tg_posts/today.txt`, verifies their temporal correlation to prevent race conditions, and maps the composed data safely into the protected `InputPackage` domain model without altering the domain architecture.
