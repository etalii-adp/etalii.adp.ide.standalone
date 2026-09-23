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

**Why the cause is shared by almost every canvas while the symptom is not, which are two different claims.** `<DiagramCanvas>` is rendered at **16 canvases across 13 modules**, counted by parsing each element's own opening tag rather than by the nearest `className` above it - a proximity grep answers a different question and has already produced one wrong survey on this defect.

| | canvases | why |
| --- | --- | --- |
| **share the cause** | **15 of 16** | nothing in the module's CSS changes the svg's box |
| **structurally protected** | **1** (`wardley-map`) | its wrapper is `display: flex`, which **blockifies** the svg, so there is no line box to reserve descender space in |

**So the cause is in the shared library and the incidence is decided by ancestors the library does not own.** Four modules declare `overflow` at all (`timeline`, `mindmap`, `causal-loop`, `azure-pipeline`), so a spill can be clipped rather than removed; a wrapper with slack absorbs the four pixels instead (`c4`, measured: a 442px canvas inside a 503px wrapper); and everywhere else it reaches `.tabbed-pane-content { overflow: auto }`, which is the bar the user saw on the two diagrams they reported. **Only `wardley-map` is protected for a reason. The rest are protected by clipping or by slack, which a layout change removes without touching this defect.**

**What the svg actually declares, stated precisely because an earlier draft of this document overstated it.** The svg carries `canvas-drawing library-canvas-surface`. `.canvas-drawing` **is** styled - `width: 100%; height: 100%` at `src/client/src/canvas/canvas.css:44` - and `.library-canvas-surface` has two rules in `src/diagrams/wardley-map/client/wardley.css:35`. **What is unset is `display`, specifically, and that is all the mechanism needs.** The earlier claim that no rule existed anywhere was false, and it mattered in a second way: it made the fix sound like a new rule when it is one declaration added to a rule already there.

**And the obvious explanation was eliminated by measurement rather than skipped.** The first theory was a wrapper that fails to take the pane's height. It is wrong twice over: **`dotnet-dependency-graph` sets `height: 100%` on its wrapper and `dependency-graph` has no wrapper rule at all**, so the two reported diagrams differ in exactly the property the theory needs them to share; and **11 of 16 wrappers set no height** while only two diagrams were reported, so wrapper height cannot explain which diagrams show it either.

**And the elimination went one property too far, which is why the incidence first came out universal.** The wrapper's *height* was eliminated correctly, on the evidence above; the wrapper's *overflow* was eliminated **along with it**, as though one finding covered both. They are independent properties, and overflow is exactly the one that decides whether the four pixels are seen. **With the wrapper ruled out entirely, only the library was left, and a universal cause reads as a universal symptom** - which measurement then refuted. The lesson is narrow and worth the words: an elimination names one property, and a second property on the same element needs its own.

**What was measured, and a corpus limit this document claimed and then had to withdraw.** The perturbation evidence is one diagram - `dependency-graph` - plus the structural count above. An earlier draft also recorded that the sixteen-diagram pass was **unavailable**, because thirteen module folders appeared to render no documents in the explorer. **That reading was wrong: a later pass found 717 rows and 322 documents in the same tree, with 16 chevrons expanding in 5ms and none stuck.** The requirement built on it has been removed.

**The withdrawal is kept rather than deleted, because a false absence reading is the failure this project keeps paying for** - an explorer that renders nothing looks identical whether the corpus is empty, the pane is 0x0, or the shell has stopped listening. Which of those produced the first reading is not established, and this document does not guess. **What is established is that the corpus was there.**

## Alignment with Product Vision

`tech.md` requires styling to be centralised rather than inline, and the canvas library exists so that sixteen diagram types share one drawing surface. **A defect in that surface reaching fifteen of sixteen canvases is the library working as designed; the fix belongs in the same place.** One rule in one shared stylesheet, touching no module, is what the user asked for by name.

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
2. The guard SHALL NOT depend on opening sixteen diagrams, on the explorer rendering children, or on any example corpus - **not because those were unavailable, which this document withdrew above, but because a guard whose subject is a shared stylesheet rule should not be able to fail for sixteen unrelated reasons.** A guard that needs a corpus reddens when the corpus changes, which is a second thing to diagnose before reaching the four pixels.
3. The guard SHALL state in itself what it cannot see: **jsdom applies stylesheets by source order and ignores specificity and `!important`** (measured 2026-09-23), so it proves the rule is declared and reached, and **the browser remains the judge of what a real cascade resolves.**
4. A `tests.md` entry SHALL record the browser check - open a canvas, confirm the pane's `scrollHeight` equals its `clientHeight` and that exactly one vertical bar is offered - naming the diagram and the element measured on it.

## Non-Functional Requirements

### Measurement discipline

- **Every row of a survey names the element it was measured on**, because a canvas-by-canvas pass that pressed one painted mark reported a working diagram this week, and a browser loop of mine reported four modules from one already-open tab.
- **`innerWidth > 0` is asserted before any geometry is recorded**: a closed pane reports 0x0 and hands back sixteen clean diagrams.

### Out of scope

The pane's own `overflow: auto`, which is correct for a pane that may hold non-canvas content; the library's scrollbar appearance; horizontal panning behaviour beyond the equality in 1.4.
