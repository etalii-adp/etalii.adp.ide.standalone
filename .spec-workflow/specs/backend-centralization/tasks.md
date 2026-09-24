# Tasks Document

One worktree for the whole specification (`.claude/worktrees/bcen`, per CLAUDE.md's one-worktree-per-specification rule), and one Developer owning it until every task is done. The tasks are independently landable within a group, which buys ordering freedom and clean merges rather than extra hands.

**The approved design's migration order is load-bearing and the groups below follow it.** It is ordered by risk rather than by module: the save result first, because every later step touches saving and the result type should not change twice; the lifecycle second, because it is the largest single reduction; the cross-tier rules last, because each needs a fixture that two suites read.

**`functional-decomposition-graph`'s module half waits on Groups 1 through 5 and on task 21** (S2, S3, S4, S5, S6, S7 and R11), by the user's ruling that it should not write a fourteenth copy. The dependency is written here rather than remembered.

---

## What is already satisfied, verified against `develop` rather than recalled

**Five criteria are met by work that landed ahead of this specification, so no task below claims them.** Each was checked with `git merge-base --is-ancestor` or by reading the file on `develop` on 2026-09-23, not taken from the design's own table:

| Criterion | Landed as | Verified how |
| --- | --- | --- |
| R2.3 a store ignores a reload of its own save | `350b8f9e` | ancestor of `develop` |
| R2.4 a failed reload keeps the last good document | `350b8f9e` | ancestor of `develop` |
| R2.5 absence is confirmed before installing an empty document | `350b8f9e`, via `IDiagramDocumentReloader.BodyDeleted` | ancestor of `develop` |
| R4.3 a removal is always sent | `350b8f9e`, merged at `88c26d44` | ancestor of `develop` |
| R11.2 an empty relation end is refused | `d2fb4e7e` | ancestor of `develop` |

**The design records two further changes as "in flight rather than landed". Both have since landed, and the tasks below assume them:**

- **Deletes take the same per-destination turn as saves** — `AdpFileWriter.DeleteTakesTheTurn.Tests.cs` is on `develop`. The measured 305-failures-in-400 race is closed.
- **`TextFileBuffer.SaveAsync` publishes through the central writer** — `TextFileBuffer.cs` on `develop` calls `AdpFileWriter.Save(_path, bytes)` rather than opening its own `FileStream`, with the reason stated in the file.

**This changes no criterion and adds no task.** It is recorded because a Developer reading the design will meet "in flight" and needs to know it is now history — and because a tasks document that silently assumed it would be resting on an unstated premise.

## What is NOT satisfied, measured rather than assumed

**R2.9's obligation is unmet at three production call sites**, measured on `develop` by reading each watcher's subscriptions:

| Watcher | Subscribes to | Missing |
| --- | --- | --- |
| `RootFolderWatcher`, `TrackedProblemRoot`, `AnsibleWatchedFolder`, `HelmWatchedFolder` | all five | — |
| `SolutionWatcher` (dotnet-dependency-graph) | `Changed`, `Created`, `Deleted`, `Renamed` | **`Error`** — no lost-events window |
| `MarkdownEditorSession` | `Changed`, `Created`, `Renamed` | **`Deleted`, `Error`** |
| `PlainEditorSession` | `Changed`, `Created`, `Renamed` | **`Deleted`, `Error`** |

**Task 9 carries this.** Two limits on that measurement, stated so the next reader does not over-trust it: it counted `+=` subscriptions textually, so a watcher wired by some other means would not appear; and two integration-test files were skipped because their paths contain a space and the loop that read them split on it. **Both gaps are in test code rather than in a component that watches, which is what R2.9 binds — but "I did not measure those two" is the honest statement rather than "they are fine".** **A measurement whose blind spots are named can be re-run by somebody else; one whose blind spots are not is just a number.**

## The coverage diff, run before this document was raised

**43 acceptance criteria in the requirements; 38 claimed by the tasks below; 5 unclaimed and none claimed that is not a criterion.**

The five unclaimed are **R2.3, R2.4, R2.5, R4.3 and R11.2** — exactly the five in the *already satisfied* table above, each verified against `develop` rather than taken from the design. **That is the third kind of unclaimed criterion**, beside *no task claimed it* (a real gap) and *no task can claim it* (satisfied by the document's own shape): **satisfied before the work began.** Saying which kind it is finishes the job, because all three look identical in the diff's output, and a reader meeting an unexplained silence will either add pointless claims or write the diff off as noisy.

**The diff is run again at task 25**, traced to files and strings rather than to a task's promise, because *did anybody claim this?* and *does the code show this?* are different questions and the second has found gaps the first did not.

---

## Group 1 — The save result, first because every later step touches saving

- [x] 1. Add the seven project references the user's placement ruling costs
  - Files: the seven module backend `.csproj` files that do not yet reference `EtAlii.Adp.Documents`
  - The user ruled placement **(b) `EtAlii.Adp.Documents`** on 2026-09-22, at a stated cost of seven new project references, one line each. Add exactly those seven and nothing else; a module that already references it is left alone.
  - **A fresh worktree is built before anything else is trusted**, because every other gate reads `obj/` and a generated-code break is invisible to them by construction.
  - _Requirements: 8.1_

- [x] 2. The shared save result type
  - Files: new in `EtAlii.Adp.Documents`, plus its tests
  - One result type carrying an error and enough for a caller to decide, returned by every store's save. **The type is named and shaped once here and not changed again**, which is why this group is first.
  - _Requirements: 3.1_

- [x] 3. A guard that a discarded save result fails the build
  - Files: a new test in `EtAlii.Adp.Backend.Tests`, beside `GuardInventory.Tests.cs`
  - A discarded result anywhere in backend or module code reddens. **The guard is seen to fail against a planted discard before it is trusted**, and the planted defect and the message it produced are recorded in the implementation log.
  - **Sabotage the call site, not only the helper**: a unit guard over the result type cannot see a caller that drops it, which is where the regression a later reader would actually cause lives.
  - _Requirements: 3.3_

- [x] 4. mindmap's `Save` stops throwing, and the edit survives a failure
  - Files: mindmap's store and its tests
  - `Save` returns the shared result rather than throwing for a failed write. **On failure the edit stays in memory**, as the other stores already do, so a user can retry rather than lose work.
  - A test asserts the edit is still present after a failed save. **It is seen to fail against the current throwing path.**
  - _Requirements: 3.2, 3.4_

## Group 2 — The document-store lifecycle, the largest single reduction

- [x] 5. The shared lifecycle
  - Files: new in `EtAlii.Adp.Documents`, plus its tests
  - Load, return, forget and reload, with the per-destination turn the writer already takes. **A first load whose file cannot be read produces a shared failure rather than each store's own**, and a parse that throws is reported as that document's problem so one document's failure never costs another's reload.
  - **THE SAVE PATH TAKES THE DOCUMENT AND NEVER READS THE CACHE.** This is a property of the signature rather than a rule to remember: the lifecycle's save accepts the `TDocument` the caller edited, and no cache read is reachable from it, so the defect cannot be written. **Amended into this task on 2026-09-24 because the defect it forbids was live in six stores that morning** - `Save(path)` re-fetched from the cache while `Reload` evicted, so a reload landing between a command's edit and its save discarded the edit **and reported success**, which put the command's inverse on the undo stack for a change that never happened. Fixed at `6c4f90d6`. **A shared lifecycle whose save reads the cache would reproduce that in all ten stores at once, from one line, and it would arrive looking like consolidation rather than like a regression.**
  - Guard: the signature is the enforcement; the backstop is **behaviour, not structure** - interleave a `Reload` between a command's edit and its save and assert the edit survives, which is the exact sequence that lost data. **Seen red against a save that re-fetches**, or it is a green that means nothing.
  - **The central per-behaviour tests REPLACE evidence this group destroys, rather than adding to it.** R2.3, R2.4 and R2.5 are satisfied today by ten separate implementations, so the ten per-store suites are ten independent witnesses. Afterwards they all exercise the same code: **nine of them stop being evidence, because they agree only because they cannot disagree.** So this task carries one shared test per behaviour - self-write suppression, last-good-on-failure, and absence confirmed through `BodyDeleted` - and those tests are the suite's only real witnesses to them from then on. "We added some shared tests as well" and "the shared tests are now the only real witnesses" are different claims about what the suite proves.
  - _Requirements: 2.1, 2.2_
- [ ] 6. Convert the nine writable stores onto it
  - Files: the nine writable module stores - azure-pipeline, c4, causal-loop, databricks, dependency-graph, mindmap, rdf, timeline and wardley-map - and their tests. Nine, not the ten this line first said, because sparql was counted here and again as task 7's read-only store (the user's chat ruling, 2026-09-25).
  - One store at a time, each landing independently. **Each converted module's existing session tests are the proof**, plus the c4 removal guard that `350b8f9e` landed.
  - **R2.4 and R2.5 land in the same change for each store, never R2.4 across the stores first.** `IDiagramDocumentReloader.BodyDeleted` defaults to a reload, which is right only while a failed read installs an empty document. A store given keep-last-good without forwarding `BodyDeleted` to the lifecycle keeps a deleted diagram forever (the user's chat ruling, 2026-09-25).
  - _Requirements: 2.1_
- [ ] 7. The read-only store (sparql) uses the same lifecycle without a save path
  - Files: sparql's store and tests
  - _Requirements: 2.7_
- [ ] 8. A republished unchanged body never installs an empty or unreadable document
  - Files: the shared lifecycle's tests
  - The guard drives the real reload path rather than calling the helper directly, because a wiring regression is what a later reader would cause. **Seen to fail against a planted install-on-unreadable.**
  - _Requirements: 2.6_
- [ ] 9. The three watchers that cannot hear everything the writer does
  - Files: `SolutionWatcher.cs`, `MarkdownEditorSession.cs`, `PlainEditorSession.cs`, and their tests
  - The three components learn of a deletion and of a lost-events window, and **on `Error` they read again rather than only logging**. The subscriptions this bullet first asked for landed at `a2318531`, but all three handlers only logged, so a lost-events window was still unlearned while the wiring guard called them compliant. **A publish arrives as `Renamed`**, which all three already handle (the user's chat ruling, 2026-09-25).
  - **The obligation is the criterion and the event set is today's mechanism**: a test asserts the component learns of a deletion and of a lost-events window, not that it subscribes to two named events, so a writer that publishes by other means fails rather than passes by habit.
  - _Requirements: 2.9_
- [ ] 10. mindmap keeps its two documented departures
  - Files: mindmap's store and tests
  - Skipping documents it never loaded, and raising its own signal, are permitted by criterion. **A test pins each, so a later reader cannot quietly remove them as inconsistency.**
  - _Requirements: 2.8_

## Group 3 — The diff and the change handler, which the lifecycle's change event feeds

- [ ] 11. The shared change-detecting diff
  - Files: new in `EtAlii.Adp.Diagram`, plus its tests. The diff works on `DiagramElement` and `DiagramDelta`, which `Diagram` declares, and `Diagram` already references `Documents`, so placing it there would be a cycle (the user's chat ruling, 2026-09-25).
  - Equality is equal position, type and payload. **Removals first, then additions**, settled by the design.
  - A table-driven test over add, remove, move, retype and payload-change. **Seen to fail against an equality that ignores payload.**
  - _Requirements: 4.1, 4.4, 4.5_
- [ ] 12. Convert the five modules that compute their own deltas
  - Files: ansible-structure, helm-charts, azure-pipeline, c4 and causal-loop sessions, and their tests
  - _Requirements: 4.2_
- [ ] 13. The shared document-change handler
  - Files: new in `EtAlii.Adp.Diagram`, plus its tests. It raises diagram deltas, for the same reason as task 11 (the user's chat ruling, 2026-09-25).
  - A session ignores changes to other paths. **A test drives two sessions on two paths and asserts the second hears nothing**, seen to fail against a handler that ignores the path.
  - _Requirements: 5.1_
- [ ] 14. Convert azure-pipeline, c4 and causal-loop onto the handler
  - Files: those three sessions and their tests
  - _Requirements: 5.2_
- [ ] 15. mindmap keeps reacting to its own structural change
  - Files: mindmap's session and tests
  - Permitted by criterion, and pinned by a test for the same reason as task 10.
  - _Requirements: 5.3_

## Group 4 — The line document, independent of the above

- [ ] 16. The shared line document, and the three modules onto it
  - Files: `LineDocument` in `EtAlii.Adp.Documents`, plus databricks, rdf and azure-pipeline and their tests
  - Read and split once. **A file with as many LF endings as CRLF is written back as CRLF by azure-pipeline**, which is the tie rule the criterion names.
  - **An unchanged document comes back byte-identical**, which is the existing per-module round-trip guard and is why those fixtures are `-text` in `.gitattributes`.
  - A range that ends before it starts is refused, as core does.
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

## Group 5 — The two small shared edits

- [ ] 17. One restore-lines edit, and three modules onto it
  - Files: `EtAlii.Adp.Documents`, which gains a reference to `EtAlii.Adp.History` for the command, plus causal-loop, databricks and rdf, and their tests (the user's chat ruling, 2026-09-25)
  - Usable by a fourth module without copying, which the criterion requires and a second consumer proves.
  - _Requirements: 6.1, 6.2_
- [ ] 18. One YAML node range, and four modules onto it
  - Files: `EtAlii.Adp.Documents`, plus databricks, dependency-graph, timeline and azure-pipeline, and their tests
  - **azure-pipeline's near-copy differs from the three identical copies**, and the design settles which behaviour wins; the task records which difference was dropped and why.
  - _Requirements: 7.1, 7.2_

## Group 6 — The cross-tier rules, last because each needs a fixture two suites read

- [ ] 19. Row rounding, with its golden fixture
  - Files: the shared function, the fixture, backend tests
  - _Requirements: 9.1, 9.2_
- [ ] 20. The text width metric, with its golden fixture
  - Files: the shared metric, the fixture, backend tests
  - **Padding, minimum and maximum stay each module's own decision**; only the metric is shared, so a module that sizes a box differently is not made wrong by this task.
  - _Requirements: 10.1, 10.2, 10.3_
- [ ] 21. The gesture id grammar, with its golden fixture
  - Files: the shared grammar, the fixture, backend tests
  - Building and parsing `new:` and `rel:` ids. R11.2's refusal of an empty end is already on `develop`; this task adopts it rather than reimplementing it.
  - _Requirements: 11.1, 11.3_
- [ ] 22. The element and relation type strings, with their fixture
  - Files: the shared list, the fixture, backend tests
  - A backend constant changing without the fixture fails a backend test. **Seen to fail by changing a constant and leaving the fixture alone.**
  - _Requirements: 12.1, 12.2_
- [ ] 23. The permanent-refusal status codes, with their fixture
  - Files: the shared list, the fixture, backend tests
  - _Requirements: 13.1, 13.2_

## Group 7 — The closing verification, which cannot be done earlier

- [ ] 24. Behaviour changes only where a criterion says so
  - Files: the implementation log, and whichever tests changed
  - **Every existing test that changed in this work is listed with the criterion that permitted it.** A test that changed for convenience rather than for a criterion is a finding, and the log says which it was.
  - _Requirements: 8.1_
- [ ] 25. The closing gate on a fresh tree, and the coverage diff traced to code
  - Files: the implementation log
  - The full backend suite and every module's byte-identical round-trip pass. **On a newly created worktree**, because every other gate reads `obj/` and a generated-code break is invisible to them.
  - **The coverage diff is run a second time here, traced to files and strings rather than to a task's promise** — the first run asked *did anybody claim this?*, and this one asks *does the code show this?*
  - _Requirements: 8.2_
