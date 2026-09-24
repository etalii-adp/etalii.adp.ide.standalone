# Design Document

## Overview

One declaration, in one shared stylesheet, changing no module. The requirements establish that the cause is universal in the canvas library and the symptom is not; this design says which declaration, where it goes, why the alternative that also removes the symptom is refused, and what guards it.

**Nothing here is a second fix.** Fifteen of sixteen canvases share the cause, and after this change none of them has it. The modules currently protected by clipping or by wrapper slack are not touched, because their protection was never written for this defect and removing it would change behaviour no criterion asks to change.

## Steering Document Alignment

### Technical Standards (tech.md)

`tech.md` requires styling to be centralised rather than inline. The fix is a rule in `src/client/src/canvas/canvas.css`, which is where the library's own classes are already stated — the file says so in its own comments, having twice paid for a class name with no rule behind it.

### Project Structure (structure.md)

No new file, no new module touch point. `docs/creating-a-diagram-module.md` names no touch point that moves, so no documentation refresh is owed.

## The declaration, and why the requirement chooses it

**`display: block`.** Both measured candidates remove the spill — `display: block` and `vertical-align: top` each took the pane's `scrollHeight` from 507 to 503, and restoring the shipped state brought the four pixels back — **but only one satisfies R1.3**, which requires that the surface no longer be laid out as an inline box. `vertical-align: top` leaves it inline and merely stops the descender space being reserved *below* it; the box is still a line box and the next layout change can put the space back. **So the criterion decides between two changes that a measurement alone would call equivalent**, which is the reason R1.3 is phrased about the box rather than about the four pixels.

## Where the rule goes, and why not the rule already there

**A new rule for `.library-canvas-surface` in `canvas.css`**, per R2.1.

The svg carries two classes, `canvas-drawing library-canvas-surface`, and **`.canvas-drawing` already has a rule** — `width: 100%; height: 100%` at `canvas.css:44`. Measured: **that class is applied to nothing else in the repository**, so amending it would be behaviourally identical today. It is still the wrong place:

- **The two classes mean different things.** `canvas-drawing` is the shared sizing convention every hand-written canvas uses, and `DiagramCanvas.tsx` says so in a comment at its own call site. `library-canvas-surface` is the library's own surface. A rule about how the library's surface is laid out belongs on the library's class, or the next hand-written canvas inherits a decision made for the library.
- **R2.1 names the class**, and a design that quietly chose the other one would be a design overriding an approved criterion on grounds of convenience.

**What the new rule must not do is read as redundant.** A reader meeting two rules for one element will want to know why, so the rule carries R2.3's required comment — an inline svg's line box reserves descender space, which spills through `overflow: visible` into a pane that scrolls — and that comment is what distinguishes it from the sizing rule beside it.

## Specificity, measured rather than assumed

**One module has rules for this class**: `wardley.css:35` and `:43`, at specificity (0,2,0) against the new rule's (0,1,0). **Neither sets `display`**, so there is no conflict today, and the new rule wins on every canvas including wardley's.

**Two facts a reader needs here.** First, **wardley is already protected structurally** — its wrapper is `display: flex`, which blockifies the svg, so this fix is a no-op on that one canvas and no wardley change is required or expected. Second, **a module rule could override `display` in future**, because a descendant selector on the same class outranks the library's. That is not a reason to reach for `!important`; it is what the guard in R3.1 exists to catch, and the guard asserts the *computed* value rather than the declared one for exactly that reason.

## The module workarounds R2.2 asks about: none, and three things that look like one

**R2.2 expects zero module workarounds for this defect, and zero is what the measurement found.** Three module stylesheets do clip an ancestor of their canvas, and none is a workaround:

| Module | Rule | Why it is not a workaround |
| --- | --- | --- |
| causal-loop | `.causal-loop-frame { overflow: hidden }` wrapping `.causal-loop-canvas-host` | Its own comment states the frame's job: *it clips, and it fills the pane's width*. Written for the frame, not for a scrollbar. |
| mindmap | `.mindmap-canvas { overflow: hidden }` wrapping `.mindmap-canvas-host` | Same shape, and it predates this defect's discovery. |
| azure-pipeline | `.pipeline-canvas { overflow: hidden }` wrapping `.pipeline-canvas-surface` | Same shape. |

**And a fourth module declares `overflow: hidden` while protecting nothing**, which is why *how many modules clip* is the wrong count to take from the requirements: **timeline's is on `.timeline-ruler`**, not on an ancestor of its canvas. The requirements say four modules declare `overflow` at all, which is true and is not the same statement — **three clip an ancestor of the canvas, verified by reading which element each selector is rendered on.** A Developer who reads *four* as *four protected* will look for a fourth protection that is not there.

**All three are left in place.** Removing a clip that happens to hide four pixels would change what those frames do, which no criterion asks for, and the four pixels will be gone anyway. **The distinction matters for the implementation log**: R2.2 says a non-zero count is a finding, and a Developer who counts *rules that hide the symptom* rather than *workarounds for this defect* will report two findings where there are none.

## The horizontal criterion is a verification, not a second fix

R1.4 requires `scrollWidth` to equal `clientWidth`. **An inline box reserves space below its baseline, not beside it**, so the measured spill was vertical and no horizontal spill was ever observed. The criterion is right to exist — the cause is not axis-specific and a fix that removed one axis and left the other would be half a fix — but **the honest statement is that the horizontal case is verified rather than repaired**, and the browser check in R3.4 records both equalities so a future regression in either axis is visible.

## The guards

### The client test (R3.1–R3.3)

**Where**: beside the existing assertion in `DiagramCanvas.test.tsx` that the surface carries `canvas-drawing` — the same element, already mounted, so the new case adds an assertion to a place a reader already looks rather than a new file they must find.

**What**: mount a canvas with `canvas.css` loaded and assert the surface's **computed** `display` is not `inline`. Computed rather than declared, because a presentation attribute loses to any CSS rule and a declared value says nothing about what the cascade resolved.

**Seen to fail**: with the new rule removed, recorded in the implementation log with the message it produced. **A test that passes against the defect is worse than no test**, because it converts *unverified* into *verified* while nothing has changed.

**What it cannot see, stated in the test itself** (R3.3): jsdom applies stylesheets by source order and ignores specificity and `!important`, measured 2026-09-23. So the test proves the rule is declared and reached; **it cannot prove what a real cascade resolves, and the browser remains the judge of that.** A reader who takes this guard as proof that wardley's higher-specificity rules lose has over-read it.

### The browser check (R3.4)

A `tests.md` entry: open a named canvas, assert `innerWidth > 0` first, then that the pane's `scrollHeight` equals its `clientHeight` and its `scrollWidth` equals its `clientWidth`, and that exactly one vertical scroll affordance is offered. **Naming the diagram and the element measured on**, per the requirements' measurement discipline — a survey row without its element is how a pass reported a working diagram this week.

**It belongs in `tests.md` rather than in the suite** because only a real cascade and a real layout can answer it, and the preamble's limits apply: the emulated viewport is cleared when a turn ends, so `innerWidth > 0` is a per-row gate rather than a precondition.

## What this design deliberately does not carry

**The inverse-direction guard — one walk over both lists, finding a rule nothing emits and a class no rule claims — is not in this design.** It was ruled to be mine, and this defect is exactly the case for it: a class the library emitted with no rule behind it, twice now. **But no acceptance criterion here asks for it**, and a design that added it would be claiming work the approved requirements do not cover, which the coverage diff would report as a task claiming something that is not a criterion.

**So it needs its own requirement.** Either an amendment to this specification, or its own item — that is the Scrum master's call to place and the user's to approve, and this section exists so the idea is not lost between the two.

## Requirement Coverage

| Criterion | Where it is met |
| --- | --- |
| R1.1 pane does not scroll natively | the declaration; browser check |
| R1.2 the library's bars are the only affordance | follows from R1.1; browser check counts the affordances |
| R1.3 the surface is not an inline box | `display: block`, chosen because of this criterion |
| R1.4 the same holds horizontally | verified, not repaired; browser check asserts both equalities |
| R2.1 one rule for `.library-canvas-surface` in `canvas.css`, no module touched | the placement section |
| R2.2 a module workaround is removed and named | none exists; two clips are not workarounds and stay |
| R2.3 the rule carries its reason | the comment, whose wording the criterion fixes |
| R3.1 a client test on computed `display`, seen to fail | the client test |
| R3.2 the guard needs no corpus | it mounts one canvas in jsdom |
| R3.3 the guard states what it cannot see | the jsdom note in the test |
| R3.4 a `tests.md` entry | the browser check |

## Sources

- The requirements' measured table: `display: inline` 503/507 with a 4px native bar, `display: block` 503/503, `vertical-align: top` 503/503, restored 507 again.
- `canvas.css:44` (`.canvas-drawing`), `wardley.css:35` and `:43`, `causal-loop.css:10-21`, `DiagramCanvas.tsx:1507-1513`, `DiagramCanvas.test.tsx:504-509`, `index.css:607` (`.tabbed-pane-content { overflow: auto }`).
- The structural count behind *fifteen of sixteen*: `<DiagramCanvas>` parsed at each element's own opening tag, 16 canvases across 13 modules.
