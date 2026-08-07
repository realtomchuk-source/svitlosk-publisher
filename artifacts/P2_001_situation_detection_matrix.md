# P2-001 Situation Detection Matrix

## S-01 Morning Startup
**Trigger**: First input of the day / No active Edition exists.
**Compared objects**: Current Edition State, System Clock.
**Compared properties**: `Edition.State`, `TargetDate`.
**Required comparison operator**: Existence Check (`Edition == null` or `State == Closed`).
**Expected output**: Boolean (True if new Edition needs to be opened).

---

## S-02 Changed Addresses
**Trigger**: `TextInputPackage` contains modified address text for an existing Territory.
**Compared objects**: Current Publication, Incoming `TextInputPackage`.
**Compared properties**: `Publication.Addresses` vs `InputPackage.Addresses`.
**Required comparison operator**: Set Inequality (or normalized string diff on intersection).
**Expected output**: `ChangedAddresses[]`

---

## S-03 Removed Addresses
**Trigger**: `TextInputPackage` contains fewer addresses for an existing Territory.
**Compared objects**: Current Publication, Incoming `TextInputPackage`.
**Compared properties**: `Publication.Addresses` vs `InputPackage.Addresses`.
**Required comparison operator**: Set Subtraction (`Publication.Addresses` EXCEPT `InputPackage.Addresses`).
**Expected output**: `RemovedAddresses[]`

---

## S-04 Added Addresses
**Trigger**: `TextInputPackage` contains more addresses for an existing Territory.
**Compared objects**: Current Publication, Incoming `TextInputPackage`.
**Compared properties**: `Publication.Addresses` vs `InputPackage.Addresses`.
**Required comparison operator**: Set Subtraction (`InputPackage.Addresses` EXCEPT `Publication.Addresses`).
**Expected output**: `AddedAddresses[]`

---

## S-05 Territory Appeared
**Trigger**: `InputPackage` introduces a Territory not present in the current Edition.
**Compared objects**: Current Edition, Incoming `InputPackage`.
**Compared properties**: `Edition.Publications.TerritoryIdentifier` vs `InputPackage.Territories`.
**Required comparison operator**: Set Subtraction (Input Territories EXCEPT Edition Territories).
**Expected output**: `NewTerritoryIdentifiers[]`

---

## S-06 Territory Disappeared
**Trigger**: `InputPackage` omits a Territory that is present in the current Edition.
**Compared objects**: Current Edition, Incoming `InputPackage`.
**Compared properties**: `Edition.Publications.TerritoryIdentifier` vs `InputPackage.Territories`.
**Required comparison operator**: Set Subtraction (Edition Territories EXCEPT Input Territories).
**Expected output**: `RemovedTerritoryIdentifiers[]`

---

## S-07 Tomorrow Forecast Appeared
**Trigger**: Tomorrow forecast indicator changes from absent to present.
**Compared objects**: Current Publication, Incoming `TextInputPackage`.
**Compared properties**: `Publication.HasTomorrowForecast` vs `InputPackage.HasTomorrowForecast`.
**Required comparison operator**: Boolean Transition (`False` to `True`).
**Expected output**: Boolean (True)

---

## S-08 Tomorrow Forecast Disappeared
**Trigger**: Tomorrow forecast indicator changes from present to absent.
**Compared objects**: Current Publication, Incoming `TextInputPackage`.
**Compared properties**: `Publication.HasTomorrowForecast` vs `InputPackage.HasTomorrowForecast`.
**Required comparison operator**: Boolean Transition (`True` to `False`).
**Expected output**: Boolean (True)

---

## S-09 Graphic Schedule Changed
**Trigger**: The structural schedule payload differs from the published graphic.
**Compared objects**: Current Publication, Incoming `GraphicInputPackage`.
**Compared properties**: `Publication.ScheduleHash` vs `InputPackage.ScheduleData`.
**Required comparison operator**: Hash Equality (`Hash(ScheduleData) != ScheduleHash`).
**Expected output**: Boolean (True)

---

## S-10 Technical Info Expired
**Trigger**: A Technical Publication has existed longer than its validity threshold.
**Compared objects**: Current Publication (Technical type), System Clock.
**Compared properties**: `Publication.CreatedAt` vs `DateTimeOffset.UtcNow`.
**Required comparison operator**: Timestamp Comparison (`UtcNow - CreatedAt > Threshold`).
**Expected output**: Boolean (True)

---

## S-11 Cleanup Started
**Trigger**: End-of-day timeline reached, requiring Ephemeral publications to be removed.
**Compared objects**: Current Edition, System Clock.
**Compared properties**: `Edition.State`, Current Time.
**Required comparison operator**: Timestamp Comparison (`CurrentTime >= CleanupTime`).
**Expected output**: Boolean (True)

---

## S-12 Edition Closing
**Trigger**: Final end-of-day timeline reached, terminating the active Edition.
**Compared objects**: Current Edition, System Clock.
**Compared properties**: `Edition.State`, Current Time.
**Required comparison operator**: Timestamp Comparison (`CurrentTime >= CloseTime`).
**Expected output**: Boolean (True)

---

## S-13 No Changes Detected
**Trigger**: The incoming InputPackage perfectly matches the existing Publication baseline.
**Compared objects**: Current Publication, Incoming `InputPackage`.
**Compared properties**: `Publication.ContentHash` vs Input Content.
**Required comparison operator**: Hash Equality (`Hash(InputContent) == ContentHash`).
**Expected output**: Boolean (True)

---

## S-14 External Producer Unavailable
**Trigger**: Failure to receive valid packages from upstream producer (DSO).
**Compared objects**: Channel Adapter / Infrastructure.
**Compared properties**: Connectivity / Polling state.
**Required comparison operator**: Boolean Check (`IsAvailable == False`).
**Expected output**: Boolean (True)

---

## S-15 Graphic Unavailable
**Trigger**: Failure to generate PNG from GraphicInputPackage schedule data.
**Compared objects**: Graphic Generator Infrastructure.
**Compared properties**: Generation Result Status.
**Required comparison operator**: Boolean Check (`GenerationSuccess == False`).
**Expected output**: Boolean (True)

---

## S-16 Comment Flood
**Trigger**: The public Telegram channel receives comments exceeding moderation limits.
**Compared objects**: Telegram Adapter / Channel State.
**Compared properties**: Comment Rate (count/time).
**Required comparison operator**: Threshold Comparison (`Rate > Threshold`).
**Expected output**: Boolean (True)

---

## S-17 Unexpected Inconsistency
**Trigger**: Fallback for deep structural discrepancies not covered by specific delta checks.
**Compared objects**: Current Edition, Incoming `InputPackage`.
**Compared properties**: Full structural aggregate vs Full input.
**Required comparison operator**: Deep Equality Mismatch.
**Expected output**: Boolean (True)

