# Tasks Document

Six tasks against the approved design: the arbiter, four conversions, and the gate. One
Developer owns all six, in any order that respects the two constraints below — the
conversions need the arbiter, and the merge needs everything.

**Every reproduction is seen to fail before its fix lands (Requirement 5).** Each conversion
task writes its reproduction against today's code first, runs it, and records the failure —
message and all — in the implementation log before converting. A reproduction that has never
been red proves nothing; this repository has already produced three client tests that passed
against the code they were written to catch. After the conversion, sabotage the fix in the
smallest way that compiles and confirm exactly the reproduction reddens.

**Sequencing with `drag-and-drop-centralization` (Requirement 6.1, design "Sequencing").**
This specification lands before that one's migration begins. If its owner starts first
anyway, stop and raise it rather than racing: its behaviour-preserving migration would
centralize the trailing-click model this specification deletes.

**Worktree.** `.claude/worktrees/sad` — short, because a long worktree path is what turns
`dotnet test` into `Zero tests ran`. Identity at creation:
`git config --worktree user.name "developer-N-selection-after-drag"`. Set
`MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` before gating; capture
every gate's exit code into a variable before any pipe. This spec is client-only, but the
gate set is the gate set: all four run before the merge.

**Merging.** Through your own scratch worktree (`.claude/worktrees/mrg<N>`, never a shared
name): `merge --no-ff` there, all four gates on that merged tree, then `git merge --ff-only`
from the main checkout — inside the scratch tree it merges the branch into itself and prints
`Already up to date`. Chain reset, merge, gates and fast-forward into one command.

**Specification documents are not worktree work**: this file, its approvals and the
implementation logs are written and committed on `develop` in the main checkout with an
explicit pathspec.

**Coverage.** The requirements-to-tasks diff was run before this document was requested for
approval: every acceptance criterion is claimed by a task except **6.3**, which no task can
claim — it is the mutual-scope statement between this specification and
`drag-and-drop-centralization`, satisfied by the two documents' own shape rather than by
work. This note is the record of that, so the diff's one silence reads as checked rather
than missed.

- [x] 1. The gesture arbiter, alone and unit-tested
  - Files: `src/client/src/canvas/gesture/` (new) — the state machine, the `usePointerGesture`
    hook adapter, the single movement-threshold constant, and a readme in the style
    `connections/readme.md` set
  - The machine from the design's Architecture section: idle → pressed → (unmoved release =
    `onPress` | moved = dragging → release = `onDragEnd`), with `setPointerCapture` at press
    and `lostpointercapture`/Escape resolving to `onDragAbandon`. **It never subscribes to
    `click`** — that absence is the fix, so the readme SHALL say why, or the next reader
    "completes" the API by adding it.
  - Targets are opaque to the arbiter (`ActiveGesture<Target>` from the design's Data
    Models): `{ kind, id, … }` threaded through untouched, the way `elementSelectionOf`
    threads ids. Background is a target like any other. `dx`/`dy` stay client pixels; unit
    conversion is the module's.
  - Unit tests in jsdom cover every transition plus the design's Error Scenarios:
    press-then-release selects; press-move-release commits and does not select; a release
    dispatched off the surface still ends the gesture (capture); capture loss abandons; a
    second pointer down while a gesture is active starts nothing; the threshold boundary at
    exactly the constant, both sides. Use `AnsibleCanvas.test.tsx`'s `setPointerCapture`
    stubs as the jsdom precedent.
  - No consumer changes in this task: the arbiter lands dark, proven by its own tests.
  - _Requirements: 2.2, 2.3, 3.1, 3.2, 3.3_
  - _Prompt: Implement the task for spec selection-after-drag, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript/React developer | Task: Build the gesture arbiter in src/client/src/canvas/gesture/ per the approved design — verdicts at gesture end from the gesture's own record, pointer capture, one threshold constant, no click subscription — with jsdom unit tests for every transition and every design Error Scenario | Restrictions: no canvas is converted yet; targets stay opaque; dx/dy stay client pixels; the readme records why click is deliberately absent | _Leverage: selection.ts's header as the mechanics-versus-meaning precedent; interaction.ts as the keyboard half of the same idea; AnsibleCanvas.test.tsx for capture under jsdom | Success: the arbiter's tests pass, at least one was seen to fail during authoring for the right reason, and npm test plus typecheck stay green. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2. C4: the latch reproduction, then the conversion that deletes it
  - Files: `src/diagrams/c4/client/C4Canvas.tsx`, `src/diagrams/c4/client/C4Canvas.test.tsx`
  - **Reproduction first, seen to fail**: press node A, move beyond threshold, fire the
    surface's mouse-leave, release, click node B — assert B is selected. Today the latch
    armed at `C4Canvas.tsx:439` swallows the click.
  - Then convert: node, relationship and background presses onto `usePointerGesture`; the
    drag body becomes `onDragMove`/`onDragEnd` unchanged in substance;
    `endInlineEditBeforeGesture` stays at press. Delete both `JustEndedRef`s, both arming
    sites, both guarded early-returns and the `onMouseLeave` wiring — the design's deletion
    table names every line.
  - Relationships ride the same press path as nodes (Requirement 3.4): unmoved release
    selects, exactly as `onRelationshipClick` does today, minus the raw click. The
    right-click menu paths and double-click behaviour are untouched, per the design's
    "what the arbiter deliberately does not do".
  - Sabotage check: reintroduce the smallest trailing-click shape that compiles and confirm
    exactly the reproduction reddens.
  - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 4.1, 4.2, 5.1, 5.2_
  - _Prompt: Implement the task for spec selection-after-drag, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Write the C4 latch reproduction, see it fail against today's code and record the failure, then convert C4Canvas onto the arbiter deleting the flag machinery per the design's deletion table | Restrictions: reproduction before conversion, never after; no behaviour change outside the defective boundary — thresholds, Escape, context menu and inline-edit-before-gesture as today; net line count goes down | _Leverage: the arbiter from task 1; the design's deletion table for the exact sites | Success: the reproduction failed before and passes after, the sabotage reddens exactly it, no JustEndedRef remains in the file, and the c4 client tests pass. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 3. Mindmap: both latch faces reproduced, then the conversion
  - Files: `src/diagrams/mindmap/client/MindmapCanvas.tsx`, `src/diagrams/mindmap/client/MindmapCanvas.test.tsx`
  - **Two reproductions first, both seen to fail**: (a) drag node A onto parent P — the
    reparent's layout moves A from under the pointer — then click node B; today the trailing
    click misses A, the latch holds, and B's click is swallowed. (b) drag node A and release
    over empty canvas; today the trailing background click deselects A, because
    `onSurfacePointerUp` (`MindmapCanvas.tsx:279`) arms the flag for pan but not for a node
    drag.
  - Then convert: press paths onto the arbiter, `onDragEnd` doing today's reparent
    (including the backend's refusals arriving as ordinary deltas), the drop-target
    highlight riding `onDragMove`. Delete the flag, its two arming sites, both guarded
    early-returns and the leave-handler state flush (`:292`) — capture makes it unnecessary.
  - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 4.1, 4.2, 5.1, 5.2_
  - _Prompt: Implement the task for spec selection-after-drag, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Write both mindmap reproductions, see both fail and record the failures, then convert MindmapCanvas onto the arbiter deleting its flag machinery and leave-handler flush | Restrictions: reproductions before conversion; the reparent's semantics and its backend refusals unchanged; a completed drag over empty canvas neither selects nor deselects | _Leverage: the arbiter from task 1; task 2's conversion as the shape | Success: both reproductions failed before and pass after, the sabotage reddens them, no JustEndedRef remains, mindmap tests pass. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 4. Relations everywhere: raw click becomes a press, with the theft reproduced
  - Files: `src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx` and its test;
    every other canvas whose relations are selected through a raw `onClick` — **enumerate by
    measurement, not from memory**: grep the canvases for relation/connection click handlers
    and record in the implementation log both what was found and what was looked for and not
    found, the way the thirteen-rename survey was re-measured before use
  - **Reproduction first, seen to fail** on dependency-graph: drag node A so the release
    lands over relation R; today R's raw click handler (`DependencyGraphCanvas.tsx:540`)
    fires on the trailing click and steals the selection from A.
  - Then wire each relation as `gesture.press({ kind: "relation", id })` — one-line wirings,
    no guards, no flags. Element paths in these canvases stay untouched (Requirement 4.3):
    their pointer-up model is already correct and its migration belongs to
    `drag-and-drop-centralization`.
  - _Requirements: 1.1, 2.1, 2.2, 3.4, 4.2, 4.3, 5.1, 5.2_
  - _Prompt: Implement the task for spec selection-after-drag, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Reproduce the relation-steals-selection defect on dependency-graph, see it fail and record it, then move every raw relation click in the pointer-up canvases onto gesture.press, enumerating the affected canvases by grep rather than assumption | Restrictions: element selection paths in these canvases untouched; no new guards or flags anywhere; the enumeration and its misses go in the log | _Leverage: the arbiter from task 1 | Success: the reproduction failed before and passes after, every relation-selecting canvas found by the grep is converted or explicitly recorded as out of scope with the reason, and the client suite passes. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 5. Azure-pipeline: the pan flag goes the same way
  - Files: `src/diagrams/azure-pipeline/client/PipelineCanvas.tsx` and its test
  - The same latch shape, pan-flavoured: `panJustEndedRef` (`PipelineCanvas.tsx:84`) armed at
    pan end, consumed only by a background click that may never come. Convert pan and
    background press onto the arbiter; delete the flag, its arming and its guard.
  - Small on purpose — this canvas has no element drag — and kept separate so tasks 2 and 3
    stay one-canvas diffs.
  - _Requirements: 1.2, 1.3, 4.1, 4.2_
  - _Prompt: Implement the task for spec selection-after-drag, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Convert PipelineCanvas's pan and background press onto the arbiter and delete panJustEndedRef with its arming and guard | Restrictions: pan behaviour unchanged - a moved pan neither selects nor deselects; an unmoved background press still deselects | _Leverage: the arbiter from task 1 | Success: no JustEndedRef remains in the file and the pipeline client tests pass. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 6. Gate and merge, and the two completion checks
  - Four gates on the merged tree, exit codes captured before any pipe, then the scratch-
    worktree merge as the preamble describes.
  - **Completion checks, both recorded in the log**: `grep -rn JustEndedRef src/` returns
    nothing (Requirement 4.1) — a one-time verification, deliberately not a standing test,
    because a guard over an identifier name is a guard over prose (design, "Guard Testing").
    And the reproductions from tasks 2–4 are present and green in the landed tree.
  - **Tell `drag-and-drop-centralization`'s owner when this lands** (Requirement 6): its
    migration may now begin, its Requirement 4.2 now preserves the corrected boundary, and
    its gesture derivatives take their verdicts from the arbiter.
  - _Requirements: 4.1, 5.1, 6.1, 6.2_
  - _Prompt: Implement the task for spec selection-after-drag, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Developer | Task: Run the four gates on the merged tree, land the spec, run the two completion checks, and notify drag-and-drop-centralization's owner | Restrictions: judge each gate by an exit code captured before any pipe; read Zero tests ran as a broken build; chain reset, merge, gates and fast-forward in one command; the fast-forward runs from the main checkout | Success: all four gates exit zero on the merged tree, the grep returns nothing, the reproductions are green on develop, and the notification is sent or its impossibility recorded. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
