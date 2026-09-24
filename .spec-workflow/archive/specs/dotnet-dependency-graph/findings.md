# Findings — `SolutionWatcher`'s tests, 2026-09-22

**This module has no owner today, which is why a gate had to trip over the first of these.** Recorded here rather than left in messages, so whoever takes the module meets all three at once.

Measured by Developer 2 (the sighting, the mechanism and the load runs) and Architect 1 (the sibling survey). Read from the product, not inferred from the failure.

## 1. `ABurstOfChanges_SettlesIntoOneReport` asserts a property of the machine

`SolutionWatcher.Tests.cs:69-99`. It builds a watcher with a **150 ms** settle delay, writes **10 files** in a loop (5 iterations × 2 files), waits for the first report, sleeps 400 ms, and asserts **exactly one** report. It failed one gate with `Expected: 1, Actual: 2`.

**The product is correct.** `SolutionWatcher.cs:99-100` disposes and re-creates its settle timer on **every** file-system event, so it reports once changes have stopped for the settle delay. That is a restart-on-change debounce, and **it is not the timer-expiry race fixed elsewhere on 2026-09-22**. It follows that a second report can only mean the burst itself contained a gap longer than 150 ms.

**The defect is the test's unenforced precondition.** Nothing bounds those ten writes to land within 150 ms of each other. On a contended machine a ≥150 ms stall mid-loop is ordinary; the watcher settles twice, **behaving exactly as specified**, and the test reports a failure.

**Both halves of the fix, and neither alone is enough:**

1. **Raise the settle delay** in the test far above any plausible burst, so the precondition holds by construction.
2. **Stop asserting an exact count over a window the test cannot bound**: wait for quiescence, then assert that one report arrives and none after it.

(1) alone leaves an exact count resting on a bound nothing enforces, merely a larger one. (2) alone keeps a 150 ms window that makes the test slower to no purpose. Together the instrument is honest and still fast.

## 2. Its two negative siblings cannot flake, and are weak

`SolutionWatcher.Tests.cs:44` (`AChangeToAFileTheGraphNeverRead_IsIgnored`) and `:103` (`AfterDisposal_NothingIsReported`) both assert that **nothing arrives within 500 ms**. Load cannot falsify a negative claim, so neither can flake — **but "nothing arrived in 500 ms" also passes when the watcher is merely SLOW.** A regression that delayed a report past 500 ms would pass both while the watcher had stopped being timely, and both tests would report health.

**This is a coverage gap, not a flake, and it is worth meeting in the same change as finding 1** — the file is open, the shapes are related, and the two fixes pull in opposite directions (one loosens a window, these two need a reason to believe a window means something). A future owner who fixes only finding 1 leaves the file reporting health it cannot vouch for.

`:26` (`AChangeToAWatchedFile_ReportsTheGraphStale`) is sound: a positive claim with a 10 s window.

## 3. What the evidence is, and what it is not

- **Two sightings now, on unrelated branches** — this section said "the only sighting" until the second arrived. The first is 2026-09-22, `mrg2`, `20260922T163002Z-25748-red`. The second is 2026-09-23, `mrg1`, `20260923T102044Z-3360-red`, on a branch changing ten files of which **every one is a `.ts` or `.tsx` under `src/client` and not one is a `.cs`**: the failing assembly's sources were byte-identical to `develop`'s. **That settles authorship without appealing to load at all**, and it is the cheaper argument of the two — it holds whatever the machine was doing, while a reproduction attempt only ever reports what it happened to do.
- **Developer 2 could not reproduce it**: 8 runs, 3 quiet and 5 under deliberate load (suites normally taking 2–3 s took 19 s, 22 s and 1 m 13 s during it, so the contention was real), all green. Then the full suite again on **`999b6bff`, the exact merged commit whose gate went red**, under the heaviest load available: **5094 passed, 0 failed**, that test included.
- **Developer 1 could not reproduce the second one either**, on the exact merged binary: **8 runs sequentially, 12 concurrently, and 6 against 24 busy loops on a 32-core machine — 26 green, not one red.** Recorded because the reflex on meeting a second sighting is to go and reproduce it: **34 failed attempts across two sessions, two branches and two days still is not a rate**, and a thirty-fifth would not be either. The mechanism is already established from the code path; what nobody has is a frequency, and no number of green runs will supply one.
- **Holding the tree fixed and varying only the run is what makes this diagnosis evidence rather than a re-roll** — and it is still **not a rate**. Eight runs plus one full-suite re-run that fail to reproduce say the mechanism is timing-dependent, not how often it bites.
- **What is solid is the code path**: nothing else in the watcher can produce a second report.

## How to read this file

The general rule these findings are an instance of is in `processes.md`, *Verifying that a test actually tests something*: an exact count over a window the test cannot bound asserts a property of the machine rather than of the code. **This file is the module's record; the rule is the tree's.**

## Ruling — Architect 3, 2026-09-23

Read from `SolutionWatcher.cs` and `SolutionWatcher.Tests.cs` rather than from the summary above. Findings 1 and 2 are confirmed: the debounce is restart-on-change (`OnFileSystemEvent` disposes and re-creates `_settleTimer` on every matching event), so a second report requires a ≥150 ms gap inside the burst, and nothing in the test bounds one. **The product is correct and the test is wrong.**

**This is a defect, not a specification.** It changes one test file. No product behaviour, no contract, no wire message, no requirement moves, and nothing here needs a design to be agreed before it can be written. A developer can take it directly. Writing a specification for it would cost three approval cycles to arrive at a change whose whole content is already in this file.

**Both fixes from finding 1, but fix (2) needs restating — as written it does not solve the problem it names.** "Wait for quiescence, then assert that one report arrives and none after it" still asserts an exact count over the burst, because reports raised *during* the burst are exactly what is unbounded. Two properties are bounded and they are the ones the module actually promises:

- **Collapsing** — ten file-system events produce *far fewer* than ten reports. This is the guarantee the class comment claims ("a graph rebuilt per file-system event would rebuild dozens of times for one logical change"), it is what a regression would break, and it holds under any stall pattern. Assert a small ceiling, not `1`.
- **Termination** — once writing stops, exactly one report arrives and no further report follows. The test controls when it stops writing, so this window it *can* bound.

Raise the settle delay as well. That makes one report the overwhelmingly likely outcome in practice, while the assertion no longer *depends* on it — which is the distinction fix (1) alone fails.

**Finding 2's remedy is a liveness control, not a longer window.** Both negative tests pass against a watcher that is merely slow, as the finding says — but they also pass against one that is **completely dead**, which is the stronger and more likely regression. Neither test carries evidence its watcher was alive during the window it measures. The positive test is a different instance in a different method, so it is not a floor for these. Each negative test should end by touching a *watched* file and waiting for a report: absence then means "nothing arrived, and something would have".

**The hazard in this change is relaxing an over-strict assertion into a vacuous one**, and it is the reason to do both findings together. Going from `== 1` to a ceiling, and from a 500 ms silence to a longer one, both move in the loosening direction; done carelessly the file ends up reporting health it cannot vouch for, which is worse than the flake it replaces. **Plant the regression and watch the new assertions fail before trusting them** — delete the `_settleTimer?.Dispose()` in `OnFileSystemEvent` so every event schedules its own timer, and the collapsing assertion must go red. A green run proves nothing here.

**Take findings 1 and 2 in one change**, as finding 2 argues: the file is open, and the two pull in opposite directions.
