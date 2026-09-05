# Tasks Document

**One developer, in series, through every canvas** (Requirement 4.5). The canvases are independently landable for ordering and clean merges, not for extra hands.

**Two numbers to carry, because both correct a figure someone will otherwise inherit.** Eleven canvases are affected, not twelve — `wardley-map` is immune and is the shape being generalised. And **three registered canvases supply no view controls, not one**: `ansible-structure/AnsibleCanvas`, `helm-charts/HelmCanvas` and `causal-loop/CausalLoopCanvas`. Task 6's guard will fail three times on its first run. An implementer expecting one failure and meeting three is under pressure to weaken the guard until it goes green; knowing in advance removes that pressure.

**Worktree.** `.claude/worktrees/ftv` — short, because a long name pushes the deepest project paths past `MAX_PATH` and the symptom is `dotnet test` reporting `Zero tests ran` rather than failing. `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` before gating, and every gate's exit code captured **into a variable before any pipe**.

**Identity, once, at creation**: `git config --worktree user.name "developer-N-fit-to-view"`. Every command run there inherits it, merges included — the half `git -c` on `git commit` does not cover. Read it back with plain `git config user.name` before your first commit in any tree you did not create.

**Merging** through a **per-agent** scratch worktree, with reset, merge, gates and fast-forward chained into one command: develop moves faster than a gate cycle here.

## Tasks

- [ ] 1. The contract: one action carries the document's extent
  - Files: `src/api/deltas.proto`, generated code both sides
  - Add `message Extent { BoundingBox bounds = 1; }` and `Extent extent = 5;` to `Delta`'s `oneof action` (1-4 are taken). Reuse `BoundingBox` from `connection.proto:19` — Requirement 2.5 asks for it by name and a second shape for the same idea is how two shapes come to disagree.
  - **Not a field on a baseline message**: there is no baseline message, the baseline *is* the first run of deltas. An action arrives in the same ordered sequence as the elements it describes, which is what Requirement 2.1 asks for.
  - Nothing sends or reads it yet.
  - _Requirements: 2.1, 2.5_
  - _Prompt: Implement the task for spec fit-to-view-extent, first run spec-workflow-guide to get the workflow guide then implement the task: Role: API developer | Task: Add the Extent action to the Delta oneof, reusing BoundingBox | Restrictions: field number 5; no new envelope; no second bounding-box shape; regenerate both sides; four gates green | Success: the action exists on the wire and nothing populates it. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2. The shared seam: the hook holds the extent, and no module's fold sees it
  - Files: `src/client/src/diagrams/useDiagramStream.ts` and its tests
  - Handle the `extent` action **inside the hook, before delegating to `applyDelta`**, and return it beside `model`. **This is what makes eleven canvases one mechanism**: not one module's fold changes, because the action never reaches a fold.
  - Absent extent is `null`, and every canvas's fallback hangs off that (Requirement 2.6, 5.3).
  - The three modules that hand-roll their open loop gain the same five lines locally. That duplication is accepted and recorded: the alternative is dragging three modules through a stream migration this spec did not ask for.
  - Tests: the hook exposes a reported extent; a module's `applyDelta` is never called with the extent action; absent extent leaves `null`.
  - _Requirements: 1.1, 1.3, 2.6, 4.1, 4.2_
  - _Prompt: Implement the task for spec fit-to-view-extent, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Intercept the extent action in useDiagramStream and expose it beside model, adding the same interception to the three hand-rolled loops | Restrictions: no module applyDelta may change; absent extent is null, never a zero box; do not migrate the hand-rolled loops to the shared hook | Success: the extent reaches canvases through one seam and no fold sees the new action. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 3. One module end to end, as the reference the other ten follow
  - Files: `src/diagrams/timeline/backend/.../TimelineSession.cs`, `TimelineCanvas.tsx`, and their tests
  - **`timeline` first**: it filters by viewport, it is model-derived, and its session was worked recently so its shape is fresh.
  - Backend: report the extent of the **whole document** at baseline and on every document change — not of the elements this connection holds. That distinction is the entire specification.
  - Canvas: `const bounds = extent ?? boundsOfModel(model)` at `TimelineCanvas:208`, and **the scroll extent too** — the bars read the same wrong bounds as the fit, and fixing one leaves the other quietly false (Requirement 4.4).
  - **The backend test asserts the extent covers elements the viewport excluded** (Requirement 3.3) — a fixture large enough to be filtered, because on a small document the extent and the delivered bounds coincide and prove nothing.
  - **The client test is the fixed point** (Requirement 3.2): fit, assert the view covers the reported extent, **fit again, assert the view is unchanged.** A single fit can pass by accident; idempotence is what a reader relies on and what fails today.
  - _Requirements: 1.1, 1.2, 1.3, 3.2, 3.3, 3.4, 4.4_
  - _Prompt: Implement the task for spec fit-to-view-extent, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack developer | Task: Report the document extent from TimelineSession and consume it in TimelineCanvas for both the fit and the scroll extent | Restrictions: the extent is the whole document's, never the delivered set's; correct both consumers; the client test must fit twice and assert the second fit changes nothing; the backend fixture must be large enough to be filtered | Success: both tests fail against today's code and pass after, seen to do both. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4. The seven remaining model-derived canvases
  - Files: `databricks/DatabricksCanvas:172`, `dependency-graph/DependencyGraphCanvas:195`, `rdf/OwlCanvas:144`, `rdf/RdfCanvas:127`, `rdf/ShaclCanvas:111`, `rdf/SkosCanvas:126`, `sparql/SparqlCanvas:126`, and each module's session
  - The same change as task 3, in series. **`SkosCanvas` derives from `placed` and `SparqlCanvas` from `boundsOf(model)`** — the same defect in different words, which is why neither could be classified by grepping for a pattern. Read the body rather than matching the shape.
  - Each canvas corrects **both** consumers. Each module's session reports the whole document's extent.
  - _Requirements: 1.1, 1.2, 1.4, 3.3, 3.4, 4.2, 4.4, 4.5_
  - _Prompt: Implement the task for spec fit-to-view-extent, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack developer | Task: Apply the task 3 pattern to the seven remaining model-derived canvases and their sessions, in series | Restrictions: read each canvas's body rather than matching a pattern - two of them express the same defect differently; correct the scroll extent as well as the fit in each; one developer through all seven | Success: no canvas computes its fit from the delivered model, and each has the fixed-point test. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5. The three that delegate to the shell
  - Files: `azure-pipeline/PipelineCanvas:118`, `c4/C4Canvas:176`, `mindmap/MindmapCanvas:428`, `useRegisterDiagramView` and the shell's null-view fit
  - `fitToView: () => setView(null)` hands the decision to the shell, which fits the elements it holds — **the same defect one layer up**. Carry the extent to the shell through `useRegisterDiagramView`, which already carries the callbacks, and have the null-view fit prefer it.
  - **These three are the most likely to be skipped**, because they contain no bounds arithmetic and read as clean. A fix applied only to the eight leaves them silently broken.
  - _Requirements: 1.1, 4.2, 4.4_
  - _Prompt: Implement the task for spec fit-to-view-extent, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Carry the reported extent through useRegisterDiagramView and make the shell's null-view fit prefer it | Restrictions: the shell falls back to element bounds when no extent is reported; do not change what setView(null) means for a canvas that reports none | Success: all three fit to the document rather than to the delivered set, with the fixed-point test. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 6. The shared guard — which must render, not scan
  - Files: one shared test beside the client's diagram tests
  - Enumerate the **registered** canvases from each module's `registrations` — the population the shell mounts, not the files on disk — mount each, and assert the view-controls registry is non-`null` afterwards, then that the fit derives from the reported extent rather than the model.
  - **It must render rather than scan the source, and this is the load-bearing decision.** `databricks` registers `BundleCanvas`, `JobCanvas` and `PipelineCanvas`; none of them calls `useRegisterDiagramView`, and all three are correct — each renders the shared inner `DatabricksCanvas`, which registers on their behalf. **A guard that greps a registered component's file reports three false positives immediately.** Registration happens through composition and only mounting can see through it.
  - **Expect three failures on the first run**: `ansible-structure/AnsibleCanvas`, `helm-charts/HelmCanvas`, `causal-loop/CausalLoopCanvas`. All three are registered views that call `useRegisterDiagramView` nowhere, so with one of those diagrams open the ribbon disables Zoom In, Zoom Out and Fit to View under *"Open a diagram to use this."* — wrong twice over, since a diagram is open and opening one would not help.
  - **Wiring those three is not this specification's work** (Requirement 4.6): the only `fitToView` writable before this spec lands *is* the defect. The guard that reveals them belongs here; the fix follows.
  - Carry the canary: the enumeration finds more than a dozen canvases, so a guard that walks nothing fails rather than passes.
  - **Seen to fail and seen to pass** (Requirement 3.1), which for the three unregistered canvases means failing until they are wired — so land it with those three excluded **by name and with the reason**, never by weakening the check.
  - _Requirements: 3.1, 3.4, 3.6, 3.7, 4.6, 5.4_
  - _Prompt: Implement the task for spec fit-to-view-extent, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Write one shared guard that mounts every registered canvas and asserts it supplies view controls and fits from the reported extent | Restrictions: render, never grep - three databricks canvases register through a shared inner component and would be false positives; exclude the three known-unregistered canvases by name with the reason, never by loosening the assertion; carry a non-empty canary | Success: the guard fails against today's behaviour, passes after, and names any canvas that registers nothing. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7. The manual check, and gate and merge
  - Files: `tests.md`, then the four gates and the merge
  - Add the Tester's sequence that found this: **zoom out repeatedly, watch the element count climb, press Fit to View, and confirm the count does not fall.** A human doing exactly that found the fixed point and no unit test would have looked — which is the reason this entry exists rather than being replaced by the automated one.
  - `tests.md` is ordinary repository content: worktree, four gates, merge. Record which build the check was observed on.
  - _Requirements: 3.5, 5.1, 5.2, 5.3, 5.4_
  - _Prompt: Implement the task for spec fit-to-view-extent, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Developer | Task: Add the manual fixed-point check to tests.md, then gate and merge the specification | Restrictions: four gates judged by exit codes captured before any pipe; confirm an unfiltered diagram's Fit to View is unchanged; no diagram may have lost a view control | Success: four gates zero on the merged tree, and the manual entry records the sequence and the build. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
