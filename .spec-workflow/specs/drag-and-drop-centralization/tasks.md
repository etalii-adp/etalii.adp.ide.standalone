# Tasks Document

One worktree for the whole specification (`.claude/worktrees/dnd`, per CLAUDE.md's one-worktree-per-specification rule), one Developer owning it until every task is done. Each task lands only when the four gates — `npm test`, `npm run typecheck`, `dotnet format style --verify-no-changes --severity info`, `dotnet test` — exit zero, judged by exit codes captured before any pipe. Running the app for task 5 happens from the worktree on the implementing Developer's reserved ports, both port files reverted before merging.

**The scope is one discipline in one file.** The structural half of this specification was delivered by `diagram-library-adoption` and is recorded as such in the requirements; nothing here recreates it, and `noPrivateGestures.test.ts` holds module gesture code at zero throughout.

- [-] 1. The gesture-frame scheduler
  - Files: `src/client/src/canvas/library/gestureFrame.ts` (new), `src/client/src/canvas/library/gestureFrame.test.ts` (new)
  - The cached surface rect captured at gesture start; the `requestAnimationFrame` coalescer (latest deltas win, one applied frame per displayed frame, synchronous application when no frame provider exists so jsdom exercises the same write path); the live-write handles; `commit` and `revert`. No React dependency beyond types. Tested alone: N moves yield one applied frame; the last value wins; `revert` undoes every write; unmount cancels a pending frame.
  - _Requirements: 1.2, 1.3, 2.3_

- [-] 2. Reposition through the scheduler
  - Files: `src/client/src/canvas/library/DiagramCanvas.tsx`
  - The `onDragMove` element case writes `transform` on the dragged `<g data-element-id>` group through the scheduler; the `dragOffset` state and the per-element `offset` prop are retired; the rect is read once at gesture start, retiring `unitsPerPixel`'s per-move `getBoundingClientRect` on this path; `onDragEnd` reverts the live writes and performs today's single state write and dispatch; `onDragAbandon` reverts and dispatches nothing. **Every existing module and library test passes unchanged — a test that must change is a finding, not a test to update.** The abandon test (dispatches nothing) is paired with the commit test (dispatches exactly once), because a negative-only assertion at a seam passes whether or not the positive path works.
  - _Requirements: 1.1, 1.3, 1.4, 2.1, 2.2, 2.3_

- [-] 3. Pan and connect preview through the scheduler
  - Files: `src/client/src/canvas/library/DiagramCanvas.tsx`
  - The pan case writes the `<svg>` `viewBox` and the scrollbar thumb positions live, committing `setView` once at gesture end — the thumbs are in the write set because freezing them mid-pan would change what the user watches (Requirement 2.2's scope). The connect case writes the preview node's geometry; per-frame `elementAt` hit-testing stays, being computation rather than rendering. Same commit/abandon pairing and same pass-unchanged bar as task 2.
  - _Requirements: 1.1, 1.5, 2.1, 2.2, 2.3_

- [ ] 4. The guard, keyed on the defect rather than on the fix
  - Files: `src/client/src/canvas/library/dragCost.test.tsx` (new)
  - Two assertions, both mounted through a test definition whose shape component counts its own renders. **The deterministic one:** thirty pointer frames of reposition *and* of pan on a hundred-element model, asserting the non-gesture shapes rendered zero times during each gesture — pan included, because a guard that only drags would be satisfied by a pan regression, which is a detector keyed on the fix. **The required ratio:** the same gestures on a ten-element and a budget-sized model in one run, per-frame cost asserted as a ratio with its tolerance stated in the test, never an absolute millisecond budget. **Accepted only after failing:** restore the per-frame `setDragOffset` and `setView` paths and watch the count go non-zero and the ratio blow its tolerance; a ratio test that has never failed is asserting arithmetic. Two canaries, per the two shapes `processes.md` records: a population floor — the mounted model demonstrably drew its hundred elements — and a named-member presence — one known element id from the test model is asserted present in the rendered output before the gesture starts — so a zero-render verdict means the discipline held, not that the mount drew nothing or drew the wrong model.
  - _Requirements: 1.1, 1.5, 3.1, 3.2, 3.3_

- [ ] 5. The measurements and the manual check
  - Files: implementation log, `src/client/src/canvas/library/DiagramCanvas.tsx` (doc-comment), `tests.md`
  - Fresh before-and-after figures on the Wikidata, laureates, `owl-time` and Helm models — the before side measured on the pre-change commit in the worktree, never compared against the requirements' 2026-09-03 quote — plus a timeline drag at comparable element count in the same run, recorded beside the after figures so the user's "prefer the timeline drag" benchmark is answered with a number beside a number. All recorded in the implementation log and summarized in `DiagramCanvas`'s doc-comment beside the scheduler's reasoning. A `tests.md` entry for dragging the Wikidata and laureates examples in a real browser, run from the worktree on the implementing Developer's reserved ports, because jsdom measures work done rather than smoothness perceived.
  - _Requirements: 4.1, 4.2, 4.3_

- [ ] 6. Gate and merge
  - All four gates exit zero on the merged tree in a per-agent scratch worktree, landed with `--ff-only` from the main checkout; any port change reverted before the merge; the coverage diff re-run against the finished code.
  - _Requirements: 2.1_

## Requirement coverage

The mechanical diff of every acceptance criterion against the task claims above:

| Criterion | Claimed by |
| --- | --- |
| 1.1 | 2, 3, 4 |
| 1.2 | 1 |
| 1.3 | 1, 2 |
| 1.4 | 2 |
| 1.5 | 3, 4 |
| 2.1 | 2, 3, 6 |
| 2.2 | 2, 3 |
| 2.3 | 1, 2, 3 |
| 3.1 | 4 |
| 3.2 | 4 |
| 3.3 | 4 |
| 4.1 | 5 |
| 4.2 | 5 |
| 4.3 | 5 |

No criterion of the fourteen is unclaimed. Of the non-functional requirements, one is a *no task can claim it*: "no module gains gesture code" is held by the existing `noPrivateGestures.test.ts` rather than by any task here, which is exactly the delivered-elsewhere state the requirements record — the guard already runs, and these tasks only have to avoid breaking it, which gate runs prove. The diff runs again, against files and strings rather than task claims, when the implementation is finished.
