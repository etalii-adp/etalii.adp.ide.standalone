# Requirements Document

## Introduction

The user's request, verbatim:

> The dependency-graph and dotnet-dependency-graph diagrams show two vertical scrollbars. Investigate, also check other diagrams and fix it. Ideally with a change that promotes centralized code.

**The cause is four pixels, and it is in the shared canvas library rather than in either diagram.**

**The two boxes.** A canvas never scrolls natively: it is an `<svg>` that pans by rewriting its `viewBox`, and the library draws its own scrollbars as elements (`canvas/scroll/CanvasScrollbars.tsx`, two `.canvas-scrollbar` divs). The second, native bar comes from the diagram pane, `.tabbed-pane-content { overflow: auto }` in `index.css`.

**The mechanism, proven by changing it and changing it back in the running app** - measured on `dependency-graph`'s `services.dgr`, on the element `dependency-graph-canvas canvas-host`, in a pane 1440x900 with `innerWidth > 0` asserted first:

| state of the library's svg | pane `clientHeight` | pane `scrollHeight` | native overflow |
| --- | --- | --- | --- |
| **as shipped**: `display: inline`, `vertical-align: baseline` | 503 | **507** | **4px, so a native bar** |
| `display: block` | 503 | 503 | **none** |
| `vertical-align: top` | 503 | 503 | **none** |
| restored to as-shipped | 503 | 507 | 4px again |

**An inline svg sits on a text baseline, so its line box reserves descender space below it.** `.canvas-host` is `overflow: visible`, so those four pixels spill into the pane, which then scrolls - beside the library's own vertical bar. **Two vertical scrollbars.**

**Why this is every canvas and not two, established from the code because sixteen measurements were not available.** Every module renders through `DiagramCanvas`, whose svg carries `canvas-drawing library-canvas-surface`, and **no CSS rule for `.library-canvas-surface` exists anywhere in the client.** The container is styled correctly - `.library-canvas { height: 100%; min-height: 0 }` - and the svg inside it was never styled at all, so it is inline in every canvas by construction. **The defect is in the shared library; the user noticed it on two diagrams.**

**And the obvious explanation was eliminated by measurement rather than skipped.** The first theory was a wrapper that fails to take the pane's height. It is wrong twice over: **`dotnet-dependency-graph` sets `height: 100%` on its wrapper and `dependency-graph` has no wrapper rule at all**, so the two reported diagrams differ in exactly the property the theory needs them to share; and **11 of 16 wrappers set no height** while only two diagrams were reported, so wrapper height cannot explain which diagrams show it either.

**What could not be measured, as a limit rather than a caveat.** The sixteen-diagram browser pass is **not available**: in the project used, only `dependency-graph` renders children in the explorer, thirteen module folders report `aria-expanded="true"` with no child rows, and two (`ansible-structure`, `helm-charts`) legitimately have no file document because their subject is a folder. **The evidence is therefore one perturbed diagram plus the code argument**, and this specification requires a guard that does not depend on a sixteen-row pass.

## Alignment with Product Vision

`tech.md` requires styling to be centralised rather than inline, and the canvas library exists so that sixteen diagram types share one drawing surface. **A defect in that surface appearing in every type is the library working as designed; the fix belongs in the same place.** One rule in one shared stylesheet, touching no module, is what the user asked for by name.

## Requirements

### Requirement 1: One vertical scroll affordance per canvas

**User Story:** As a user looking at any diagram, I want one vertical scrollbar, so that I am not offered two controls for one axis.

#### Acceptance Criteria

1. WHEN any canvas is open at any pane size THEN the diagram pane SHALL NOT scroll natively: its `scrollHeight` SHALL equal its `clientHeight`.
2. The library's own scrollbars SHALL remain the only vertical scroll affordance a canvas offers.
3. WHEN the fix is applied THEN the library's drawing surface SHALL NOT be laid out as an inline box, because an inline box reserves descender space that its own height does not include.
4. The same SHALL hold horizontally: the pane's `scrollWidth` SHALL equal its `clientWidth`. **Only the vertical case was reported and measured; the horizontal case is required because the cause is not axis-specific**, and a fix that removed one and left the other would be half a fix.

### Requirement 2: The fix is one rule in the shared stylesheet

**User Story:** As a maintainer, I want this fixed where the surface is defined, so that no module carries a workaround and no future module can forget one.

#### Acceptance Criteria

1. The fix SHALL be a rule for `.library-canvas-surface` in `src/client/src/canvas/canvas.css`, and SHALL change no module stylesheet and no module component.
2. IF a module stylesheet already works around this THEN that workaround SHALL be removed in the same change, and the implementation log SHALL name it. **None was found while measuring, so the expected count is zero and a non-zero count is a finding.**
3. The rule SHALL carry its reason in a comment: an inline svg's line box reserves descender space, which spills through `overflow: visible` into a pane that scrolls.

### Requirement 3: A guard that does not need a browser, beside a check that only a browser can make

**User Story:** As an agent session, I want this to redden in the suite if it returns, so that a four-pixel regression is not found by a user again.

#### Acceptance Criteria

1. A client test SHALL mount a canvas with the shared stylesheet loaded and SHALL assert that the drawing surface's computed `display` is not `inline`. **It SHALL be seen to fail against the rule removed**, recorded in the implementation log with the message it produced.
2. The guard SHALL NOT depend on opening sixteen diagrams, on the explorer rendering children, or on any example corpus, because **none of those was available while measuring and a guard that needs them would not run today.**
3. The guard SHALL state in itself what it cannot see: **jsdom applies stylesheets by source order and ignores specificity and `!important`** (measured 2026-09-23), so it proves the rule is declared and reached, and **the browser remains the judge of what a real cascade resolves.**
4. A `tests.md` entry SHALL record the browser check - open a canvas, confirm the pane's `scrollHeight` equals its `clientHeight` and that exactly one vertical bar is offered - naming the diagram and the element measured on it.

### Requirement 4: The corpus gap is recorded, not fixed here

**User Story:** As the Scrum master, I want the thing that blocked the measurement written down, so that it is triaged rather than rediscovered.

#### Acceptance Criteria

1. This specification SHALL record that **thirteen module folders rendered no documents** in the project measured, and **SHALL NOT fix it** - it is a separate finding with its own cause, and conflating it with a four-pixel layout defect would delay both.
2. The record SHALL name what is known: the folders report `aria-expanded="true"` with no child rows; `ansible-structure` and `helm-charts` are legitimate exceptions with folder subjects; `dependency-graph` rendered children normally in the same tree.

## Non-Functional Requirements

### Measurement discipline

- **Every row of a survey names the element it was measured on**, because a canvas-by-canvas pass that pressed one painted mark reported a working diagram this week, and a browser loop of mine reported four modules from one already-open tab.
- **`innerWidth > 0` is asserted before any geometry is recorded**: a closed pane reports 0x0 and hands back sixteen clean diagrams.

### Out of scope

The explorer's missing children; the pane's own `overflow: auto`, which is correct for a pane that may hold non-canvas content; the library's scrollbar appearance; horizontal panning behaviour beyond the equality in 1.4.
