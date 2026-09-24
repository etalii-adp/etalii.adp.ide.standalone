# Tasks Document

One worktree for the whole specification (`.claude/worktrees/csbar`, per CLAUDE.md's one-worktree-per-specification rule) and one Developer owning it. **This is a small specification — one declaration, two guards and a recorded browser check** — and it is written out in full anyway, because the parts that are easy to get wrong are the guards rather than the fix.

**The order is load-bearing, and it buys the failing observation for free.** R3.1 requires the guard to be *seen to fail against the rule removed*. Writing the guard **before** the rule means it fails for the right reason on its first run, with nothing to un-do afterwards — where writing the rule first means adding it, deleting it, observing, and putting it back, which is three chances to leave the tree in a state nobody intended. **Task 1 therefore writes a test that must fail, and task 2 makes it pass.**

---

## The coverage diff, run before this document was raised

**11 acceptance criteria in the requirements; 11 claimed by the tasks below; none unclaimed, and nothing claimed that is not a criterion.**

There is no *satisfied ahead* set here and no criterion met by the document's own shape — unlike `backend-centralization`, every criterion in this specification is work somebody has to do. **The diff is run again at task 6**, traced to the files and strings the work produced rather than to a task's promise.

**One task claims no criterion, deliberately: task 5.** It is a CLAUDE.md obligation (a UI change that makes a screenshot misleading means retaking it) rather than something the requirements ask for, and it is written as a judgement with a stated threshold rather than as an assertion, because I could not settle it by looking.

---

## The tasks

- [x] 1. The client guard, written first so that it fails
  - Files: `src/client/src/canvas/library/DiagramCanvas.test.tsx`
  - Beside the existing assertion that the surface carries `canvas-drawing` — the same element, already mounted, so a reader meets the new case where they already look rather than in a file they must find.
  - Mount a canvas **with `canvas.css` loaded** and assert the surface's **computed** `display` is not `inline`. **Computed, never declared**: a presentation attribute loses to any CSS rule, and a declared value says nothing about what the cascade resolved.
  - **It must fail on its first run, before task 2 exists.** Record the message it produced in the implementation log — that recording is the evidence R3.1 asks for, and it is free at this point in the order.
  - **The test states in itself what it cannot see**: jsdom applies stylesheets by source order and ignores specificity and `!important` (measured 2026-09-23). So it proves the rule is declared and reached; **it cannot prove what a real cascade resolves.** A reader who takes it as proof that wardley's higher-specificity rules lose has over-read it.
  - It mounts one canvas and needs no corpus, no explorer children and no sixteen-diagram pass.
  - _Requirements: 3.1, 3.2, 3.3_

- [x] 2. The rule, which makes task 1 pass
  - Files: `src/client/src/canvas/canvas.css`
  - A rule for `.library-canvas-surface` setting `display: block`. **Not `vertical-align: top`**, which removes the same four pixels and leaves the surface an inline box — R1.3 is about the box rather than the pixels, and it is what chooses between two changes a measurement calls equivalent.
  - **A new rule rather than an amendment to `.canvas-drawing`** at `canvas.css:44`, per R2.1, even though that class is applied to nothing else and amending it would be behaviourally identical today.
  - The rule carries its reason in a comment, as R2.3 requires: an inline svg's line box reserves descender space, which spills through `overflow: visible` into a pane that scrolls. **That comment is also what stops the second rule reading as redundant** beside the sizing rule.
  - **No module stylesheet and no module component is touched.** wardley-map needs no change and will see none: its wrapper is `display: flex`, which blockifies the svg already, so the fix is a no-op on that one canvas.
  - _Requirements: 1.3, 2.1, 2.3_

- [x] 3. Count the module workarounds, and count the right thing
  - Files: the implementation log
  - R2.2 expects **zero** module workarounds for this defect and says a non-zero count is a finding. **Zero is what the design measured, and the count that produces it is *workarounds for this defect*, not *rules that hide the symptom*.**
  - Three module stylesheets clip an ancestor of their canvas — `causal-loop`, `mindmap`, `azure-pipeline` — and **none is a workaround**: each states its own reason, and causal-loop's comment says outright that the frame clips and fills the pane's width. **All three stay.** Removing a clip that happens to hide four pixels would change what those frames do, which no criterion asks for.
  - **A fourth module declares `overflow: hidden` and protects nothing**: `timeline`'s is on `.timeline-ruler`, not on an ancestor of its canvas. *Four modules declare `overflow`* and *four are protected* are different sentences, and a Developer who collapses them will hunt a fourth protection that does not exist.
  - The log records the count as zero **with the three-and-one distinction**, so the next reader does not re-derive it.
  - _Requirements: 2.2_

- [x] 4. The `tests.md` entry for the browser check
  - Files: `tests.md`
  - Open a named canvas; assert **`innerWidth > 0` on the row itself**, not once as a precondition — an emulated viewport is cleared when a turn ends, so a multi-row pass silently returns to 0x0 partway through.
  - Record that the pane's `scrollHeight` equals its `clientHeight` **and** its `scrollWidth` equals its `clientWidth`, and that exactly one vertical scroll affordance is offered.
  - **Name the diagram and the element measured on**, per the requirements' measurement discipline: a survey row without its element is how a pass reported a working diagram this week.
  - It belongs in `tests.md` rather than in the suite because only a real cascade and a real layout can answer it.
  - _Requirements: 3.4_

- [x] 5. Judge whether `docs/screenshots/dependency-graph.png` has become misleading
  - Files: `docs/screenshots/dependency-graph.png` (only if the judgement says so), using the existing `docs/screenshots/capture.mjs`
  - **This claims no acceptance criterion.** It is CLAUDE.md's rule that a UI change making a screenshot misleading means retaking it, and that image is a capture of **one of the two diagrams the user reported**.
  - **Stated as a judgement rather than an instruction, because it could not be settled by looking**: the image is 1600px wide and the defect is four pixels, so whether a second vertical bar is visible in it is at or below what that image can decide by eye. **Do not retake on the strength of the argument alone.**
  - **What was looked at, so the judgement is not re-derived from scratch**: the committed image, at its full 1600px width, showing `services.dgr` open on the dependency-graph canvas. There is a vertical scroll affordance at the canvas's right edge. **What could not be resolved is whether a second, four-pixel native bar sits beside it** — at that scale the two would be adjacent and the difference is around the width of the rendering itself.
  - The decidable form: after task 2, capture the same view again with `capture.mjs` and compare the canvas region against the committed image.
  - **Both outcomes are acceptable, and only the unrecorded one is wrong.** *Retake it* and *leave it* are equally good answers provided the log says which was chosen and why. **This is not a disguised instruction to retake**: an implementer who retakes to be safe has converted a judgement into a ritual and taught nobody anything, including themselves.
  - _Requirements: none — a house rule, recorded here so it is neither forgotten nor mistaken for a criterion_

- [x] 6. The browser check, run, and the coverage diff traced to code
  - Files: the implementation log, and `tests.md`'s result line
  - Run the check task 4 wrote, on a local build, and record the outcome with the diagram and element named. **This is what verifies R1.1, R1.2 and R1.4** — the client guard cannot, because jsdom resolves no real cascade and lays out no real pane.
  - **R1.4 is a verification rather than a repair**: an inline box reserves space below its baseline and not beside it, so no horizontal spill was ever observed. Recording both equalities is what makes a future regression in either axis visible.
  - **Re-run the coverage diff here**, traced to files and strings rather than to a task's promise. *Did anybody claim this?* and *does the code show this?* are different questions, and the second has found gaps the first did not.
  - **Before recording any negative, check the console**: a degraded shell renders the last document perfectly while listening to nothing, and a wedged origin answers `curl` in milliseconds while nothing in the tab completes. Either produces a clean-looking row that means nothing happened.
  - _Requirements: 1.1, 1.2, 1.4_
