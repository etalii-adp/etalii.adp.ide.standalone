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

- **The gate's red is the only sighting.**
- **Developer 2 could not reproduce it**: 8 runs, 3 quiet and 5 under deliberate load (suites normally taking 2–3 s took 19 s, 22 s and 1 m 13 s during it, so the contention was real), all green. Then the full suite again on **`999b6bff`, the exact merged commit whose gate went red**, under the heaviest load available: **5094 passed, 0 failed**, that test included.
- **Holding the tree fixed and varying only the run is what makes this diagnosis evidence rather than a re-roll** — and it is still **not a rate**. Eight runs plus one full-suite re-run that fail to reproduce say the mechanism is timing-dependent, not how often it bites.
- **What is solid is the code path**: nothing else in the watcher can produce a second report.

## How to read this file

The general rule these findings are an instance of is in `processes.md`, *Verifying that a test actually tests something*: an exact count over a window the test cannot bound asserts a property of the machine rather than of the code. **This file is the module's record; the rule is the tree's.**
