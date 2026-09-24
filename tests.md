# Manual verification checks

Step-by-step checks for bugs that only reproduce through the running app, so they can be
re-executed as part of a manual verification pass. Each entry names the spec and task it
came from. (See CLAUDE.md, "Bugs found during implementation or verification".)

## What every entry below assumes

**Signing in — a developer build does not ask.** Since `developer-sign-in-bypass`, a locally
running developer build opens **already authenticated** and the sign-in form is never rendered:
`AuthProvider` asks the backend for a session on mount and only then decides what to show.
Nothing below needs a person any more, and a reload no longer costs anyone's attention — the
session is obtained again on the next mount.

Two things follow that a reader of an outcome needs. **The header carries a "developer session"
marker** whenever the session was handed over this way, so every screenshot taken under it says
so; if that marker is absent, the pass was run some other way and the outcome means something
different. And **the bypass exists only in a Debug build running in a development environment**
— it is compiled out of a release entirely, and the release job refuses to publish a build that
still carries it. An outcome recorded below is therefore evidence about a developer build, which
is why each one names the build it was seen on.

If a sign-in form does appear, the bypass is off rather than broken: check that the build is
Debug, that `ASPNETCORE_ENVIRONMENT` is `developer` (or `Development`), and that
`LocalAuthenticator:DeveloperSessionDisabled` is not set. The real credential path still works
and is still tested; it is no longer the only way in.

**Finding the Properties panel.** Thirteen entries below say "read the Properties panel" or "the
property grid" as though it were on screen. It often is not. `Properties` shares a tabbed pane
with `Toolbox` on the right, and `TabbedPane` measures the available width and collapses what does
not fit — at 1440x900 that pane is 253px, which fits one tab, so `Properties` sits behind a
**"1 more tabs" overflow button** beside `Toolbox`. That is the pane working as designed rather
than a defect. Widening the pane or clicking the overflow both reach it; hunting for it is what
costs the time, and it is written here once rather than in each of the thirteen.

**What the in-app browser pane can answer, and what it cannot.** **Seven entries below each
rediscovered part of this**, across twelve lines between them, which is why it is here once. The
pane has **three states, not two**, and they differ in what may be recorded from them:

1. **The session is closed in the app: the pane is 0x0 and `visibilityState` is `hidden`.** Nothing
   is trustworthy - there is no layout, so `getBoundingClientRect` is zero, `elementFromPoint` finds
   nothing, and a synthetic press cannot be aimed. **Record nothing from this state.** It is the
   dangerous one precisely because it looks like a clean pass: every count comes back zero and
   nothing throws.
2. **The session is open but the pane is not fronted: real layout, `visibilityState` still `hidden`.**
   Geometry and computed styles ARE valid here - `getComputedStyle`, `getBBox`,
   `getBoundingClientRect`, click-driven selection round trips and DOM updates all behave.
   **Screenshots are not**, so a pass run here says so rather than leaving a reader to assume
   somebody looked.
3. **The pane is visible:** all of the above, plus screenshots.

**Two cheap discriminators: `innerWidth > 0` before recording anything, and `visibilityState`
before believing a screenshot.**

**The mechanism is better than the state list, because it predicts which checks are affected:**
`requestAnimationFrame` never fires in a hidden document - measured at 1.2s with no frame, against
the visible tab's firing at once. Canvases repaint on an animation frame that never arrives, so
**the question to ask of a check is *does this need a repaint*, not *is the pane visible***. A check
wanting a canvas repaint in a hidden tab is undriveable; one wanting only a DOM change is fine.
**And fronting does not lift it** - a second pane tab reports `hidden` even after `tabs_select`,
because the pane has one permanently-visible document.

**A fourth limit, independent of state: synthetic `PointerEvent`s do not start the canvas library's
drag gesture.** Measured with the pane painting and `setPointerCapture(1)` throwing nothing: during
the drag there was no transform, no dragging class, no ghost and no preview - **yet the release still
landed the element.** So a drag's PREVIEW is unobservable by script here while its DROP is
observable, which is the distinction to apply before claiming a drag check was run.

**A fifth limit, and the one that decides whether a step can be driven at all: while the pane is hidden,
clicks by COORDINATE land in the wrong place.** A press at the centre of a node measured with
`getBoundingClientRect` was recorded by an instrumented listener as hitting the bare
`svg.library-canvas-surface`, while `document.elementFromPoint` at that same x,y returned the node's own
`<text>`. **The page's client coordinates and the pane's screenshot frame are not the same frame while it
is hidden, and nothing warns you. Clicks by `ref` from `find` land correctly.** Geometry read through
JavaScript is unaffected - `getBoundingClientRect`, `scrollHeight` and computed styles stay valid - so a
measuring pass is safe where a pressing pass is not. **If a step presses something, press by ref and
verify the press landed**; two lines do it:

    svg.addEventListener("pointerdown", e => console.log(e.target.tagName, e.target.getAttribute("class")), true)

**A sixth, and it makes a NEGATIVE result worthless: a degraded shell renders the last document perfectly
while listening to nothing.** After a reload the shell threw `useContextConnection must be used within a
ContextConnectionProvider` from four components at once; **the canvas went on drawing, presses produced
events, and nothing round-tripped.** It behaves like a still image. **So `read_console_messages` before
believing any negative is a precondition, not a debugging step** - one session nearly recorded "c4 shows
no highlight" for a second time on a page that was simply not listening.

**A seventh, cheap to hit and expensive to notice: documents open on DOUBLE-click, and they open in a tab
BEHIND the one in front.** One session measured c4 while reading the wardley map, because the earlier
document was still fronted; another reported four modules from one already-open tab and only noticed
because all four rows were identical. **Select the intended tab and confirm the document before pressing
or measuring anything.**

**And an eighth, which is the mechanism behind *before recording anything* above and the reason it has to be
read literally: an emulated viewport is cleared when a turn ends.** A sixteen-canvas pass cannot be done in
one turn, so it is exactly the pass that silently returns to the 0x0 state - where **every pane reports
`h: 0`, every geometry row comes back clean, and nothing throws.** So `innerWidth > 0` belongs in a
procedure's STEPS as a per-row gate rather than in its preconditions: **a precondition is checked once, and
this state can return mid-pass.** *(Developer 1's refinement, against a sixteen-canvas procedure of its
own.)* One session found `innerWidth` at 0 with four panes at `h: 0` and recorded nothing from them - **only
because it checked again rather than once**, and a procedure that says *check first* would have let it
record four clean zeroes in good faith.

The three states and the drag limit were measured by Architect 2 across two browser sessions; the
animation-frame mechanism and the permanently-hidden second tab come from the 2026-09-06 pass
recorded under *A property edit reaches a second connection*. **None of this is an application
defect**, which is why an entry blocked by it is left unclaimed rather than failed.

**One word for an outcome.** An outcome is recorded as `- **Result <date>**: **<verdict>** — ...`,
one label, no synonyms. Ten entries here once said `**Verified <date>**` instead, and because every
count anyone ran greps for `Result`, those ten read as *never run* for as long as they existed -
which is how "ten entries have no outcome" was repeated all day when the true figure was zero. They
were normalised on 2026-09-05, keeping every date and sentence. **This is a convention, not a
guard**: a check asserting the wording would be a second copy of this paragraph and would need
editing the next time an outcome is legitimately phrased differently. It is written here because
this is the section everyone editing the file reads.

**And do not build a script on the verdict word either.** The label is now one word; the verdict
after it is not, and normalising those would mean rewriting other people's findings. Counting the
blocked checks on 2026-09-05 turned up **four vocabularies** — `**passes**`, `pass -`,
`**not run**` and `**pending**` — plus a `- **Result**:` with **no date at all** on four
causal-loop entries, two of which are the *"Same reason as above"* inheritance that defines the
blocked set. A pattern requiring a date silently drops exactly those. Two phrasings also exist for
a check standing in meanwhile, `Covered meanwhile by X` and `What stands in meanwhile: X`, which
is why that population has been reported as three when it is six. **Read the verdicts; do not
match them.**

**A "task complete" commit on `develop` does not mean the code is on `develop`.** Bookkeeping
reaches `develop` instantly from the main checkout; the code behind it waits on four gates and on
winning a merge race, so the two can be hours apart. On 2026-09-05 `develop` carried *"Marked
developer-sign-in-bypass tasks 1 to 3 complete… the client opens already authenticated"* from
16:12, while the commits implementing it landed at 18:44. In that window the app still opened on
the sign-in form, and a Tester loaded it on the strength of the log and lost the time.

**The first version of this paragraph blamed the wrong thing, which is worth keeping visible.** It
said this was two correct rules intersecting — bookkeeping on `develop`, implementation in a
worktree — and therefore unavoidable. It was not: Developer 2, whose commits these were,
identified it as marking tasks complete before their code had merged, and now holds the rule
**do not mark a task complete until `git merge-base --is-ancestor` says its code is on
`develop`**. A hazard called structural gets designed around; a hazard called a slip gets fixed at
its source, and this one was. **That bypass is fully merged as of 2026-09-05 and this instance is
closed.**

The check outlives the instance, because the window recurs whenever anything is marked complete:
**verify the implementation commit, not the bookkeeping one** —
`git merge-base --is-ancestor <commit> develop`, or look for the type it introduces in `develop`'s
sources.

**A wedged origin makes a canvas that reports nothing indistinguishable from a canvas with a real
defect, so confirm the origin still answers before recording any negative row.** Until the HTTPS
endpoint of `two-tab-connection-wedge` task 1 is the one you are browsing, opening documents in two
tabs on one origin exhausts that origin's HTTP/1.1 connection pool, and **nothing on it completes
again - not a gRPC call, not a plain static file.** A check run after that point reports emptiness
for a reason that has nothing to do with what it was checking.

**The two readings are identical from the inside**, which is why this is a precondition rather than a
note: a panel that never populates, a canvas that draws nothing, a property grid that stays blank -
each looks exactly like the defect somebody is hunting. **Every negative row recorded after a wedge
is worthless and cannot be told from a genuine finding by looking at it.**

The check is one request for something that is not gRPC at all: fetch any static asset on the same
origin and require a response. **If a plain file does not come back, stop** - the wedge is what you
are looking at, and every row since the last known-good one has to be re-run rather than trusted.
Re-running means a fresh browser profile, not a fresh tab: see the entry below for why a new tab
joins an already-exhausted pool.

## The context surface is reachable on a causal loop diagram (causal-loop-diagram, resolver fix)

**Found by opening the app, and invisible to every test until then.** The module shipped without
an element source resolver — the `*ContextSourceResolver.cs` every other diagram module has. A
canvas selection resolves level by level (project, folders, the `.adp`, then the element inside
it) and that last step is each diagram type's own. With none, no `ContextTarget` was ever built,
so neither the action provider nor the property provider was ever consulted.

Sixteen provider tests passed throughout, because they build a target by hand and ask the
provider directly — skipping exactly the step that did not exist.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/on-call/on-call.adp`. Right-click a variable. Press
  Escape, then right-click empty canvas. Then choose **Arrange diagram**.
- **Expected**:
  - Right-click on a variable opens a menu with **Add link from here…**, **Claim a loop from
    here…**, **Rename…** and **Remove variable (with N references)** — the count read from the
    model, not a guess.
  - The variable shows as selected on the canvas.
  - Right-click on empty canvas offers **Add variable…** and **Arrange diagram**.
  - The ribbon offers the same actions as the menu (Requirement 5.3).
  - **Arrange diagram** rearranges the variables with no two boxes overlapping, writes a
    `layout:` block into the `.adp`, leaves the `.cld` untouched, and **one** Undo restores the
    registration.

Verified in full on 2026-09-05, against a running build. The menus, the selection and the
ribbon first (`Selected .../on-call.cld ... as ContextMenu, with 1 action groups`), then the
arrangement itself:

```
ArrangeCausalLoopCommand executed; 1 changes can now be undone
Action causal-loop.arrange completed on .../on-call.cld
Undid ArrangeCausalLoopCommand; 0 left to undo, 1 to redo
```

All eight variables moved, **zero** overlapping pairs measured on the rendered boxes, only the
`.adp` modified with the `.cld` untouched, and **one** undo restoring the registration
byte-for-byte - `git status` on the examples came back empty afterwards.

**Undo it in the app, not with `git checkout`.** Reverting the file underneath a running session
deletes and recreates it, so the hierarchy entry id the open tab is bound to no longer exists and
every later selection is rejected with "This item is no longer available." That looks exactly
like the context surface being broken again, and it is not.

- **Result 2026-09-05**: **passed**, all six claims of Requirement 6 confirmed in the running app.

## Anchors and the connect preview are styled, not black (canvas library, found in the app)

**Reported from the running app, and invisible to every test.** The canvas library draws four
things a module never supplies a class for - the anchors it derives from a type's `anchors`
declaration, the preview path a connect gesture drags, and the resize and adjust handles. It
names them `library-*`, and 31 of its 32 classes had no CSS anywhere.

The library's canvas `<svg>` computes `fill: rgb(0, 0, 0)`, and SVG's `fill` is **inherited**, so
every unstyled shape inside it was painted black. Nothing failed: the element rendered, carried
its class, and the browser painted it with the defaults.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/timeline/example-1/roadmap.tml`, then a dependency graph. Look at
  the anchor dots on each element. Then press an anchor and drag away from it without releasing.
- **Expected**:
  - Anchors are the element's own surface colour with a green outline - **not** black discs.
  - The drag preview is a **dashed green line**. It must not be a filled shape: a curve with a
    fill paints the region between the curve and its chord, which swept a solid black wedge
    across the diagram.
  - Dragging onto a target that refuses the connection turns the preview **red**.
  - A selected span's resize handles are invisible rather than black bars, and a connection's
    midpoint adjust handle is a surface-coloured dot.
- **And the regression to watch for**: the anchors must still be **draggable**. They carry the
  connect gesture themselves, unlike the shared `.canvas-anchor`, which can be inert only because
  the modules using it draw a separate hit circle behind it. Copying `pointer-events: none` onto
  them leaves them looking perfect and impossible to drag from.

**Result 2026-09-05**: **passed** for the colours, measured against the live computed styles
rather than by eye - anchors `rgb(30,41,59)` where an unstyled path in the same `<svg>` computes
`rgb(0,0,0)`; preview `fill: none` with a dashed green stroke; invalid variant red. **The drag
itself was not performed by hand** - a synthesised mousedown did not start the gesture - so the
wedge's absence is inferred from the computed style, not seen. Worth one human drag.

## The diagram pane offers one scrollbar, not two (canvas-single-scrollbar, tasks 4 and 6)

**Reported twice by the user, on two diagrams, and invisible to every test in the suite.** An `<svg>`
is an inline element, so the library's drawing surface sat on a line box that reserved about four
pixels of descender space below its baseline. `overflow: visible` let that spill out of the surface and
into the pane that scrolls, which then offered a SECOND vertical scrollbar beside the diagram's own.

**Why it is here rather than in the client suite.** The suite's guard
(`DiagramCanvas.test.tsx`, *makes its surface a block box*) asserts the COMPUTED `display` under
`canvas.css` and that is all it can do: jsdom applies stylesheets in source order, implements neither
specificity nor `!important`, and lays out nothing. **So it proves the rule is declared and reached, and
says nothing about what a real cascade resolves or what a real pane measures.** Only a browser can
answer the question the user asked.

- **Preconditions**: backend + client running on a Debug developer build; `src/examples` open as a
  project. The header must show the **developer session** marker.
- **Actions**: open `diagrams/dependency-graph/example-1/services.dgr`, and then
  `diagrams/timeline/example-1/roadmap.tml` as the second diagram - **the two the user reported**.
- **The pane's limits, the `innerWidth` check and the origin check are the preamble's**, in the item *What
  the in-app browser pane can answer, and what it cannot* - **cited rather than restated**, and by NAME
  rather than by number because the number moves whenever a preamble item is added. Read it before running
  this entry.
- **The one thing this entry adds to it**: read `window.innerWidth` **on every row**, not once as a
  precondition. A survey of two diagrams spans turns, and an emulated viewport is cleared when a turn ends,
  so a later row can measure a window with no width. **A row whose own `innerWidth` is 0 is not a failure,
  it is a row that did not run.**
- **Measure the WRAPPER, not the pane, and this was corrected by running the check** (task 6). The
  element is `div.library-canvas` - `.dependency-graph-surface` on one diagram,
  `.timeline-surface.canvas-viewport` on the other - and its host, `div.*-canvas.canvas-host`:
  - the wrapper's `scrollHeight` **equals** its `clientHeight`, and its `scrollWidth` its `clientWidth`;
  - the host's, likewise;
  - the surface's computed `display` is `block`.
- **THE PANE IS THE WRONG ELEMENT AND THE ROW WOULD HAVE PASSED BEFORE THE FIX.**
  `div.tabbed-pane-content` reads `scrollHeight == clientHeight` at 1600x900 **whether the surface is
  inline or block**, because it has around 230px of slack that absorbs four pixels. Shrinking the
  viewport does not rescue it: at 1600x520 the pane overflows by 280px in BOTH states for an unrelated
  reason, so four pixels are invisible against it either way. **There is no viewport in that pair where
  the four pixels decide whether the pane scrolls.** A row written against the pane is a row that
  cannot fail.
- **The decisive form is a perturbation rather than a reading.** Set `display: inline` on the surface
  from the console - the state before the fix - read the wrapper, restore, and read again. Four pixels
  appear and disappear on demand. A single reading of a fixed tree cannot distinguish a working rule
  from an absent defect.
- **Both equalities are recorded even though only one axis ever failed.** An inline box reserves space
  BELOW its baseline and not beside it, so no horizontal spill was ever observed - recording the
  horizontal one is what makes a future regression in that axis visible rather than a surprise.
- **Name the diagram and the element each number came from.** A survey row without its element is how a
  pass reported a working diagram this week.

**Result 2026-09-24**: **passed**, on a Debug developer build at 1600x900 with the developer-session
marker present, by perturbation rather than by reading. `scrollHeight - clientHeight`, shipped then
forced to `display: inline` then restored:

| diagram | element | shipped | inline | restored |
| --- | --- | --- | --- | --- |
| `services.dgr` | `div.library-canvas.dependency-graph-surface` | 0 | **4** | 0 |
| `services.dgr` | `div.dependency-graph-canvas.canvas-host` | 0 | **4** | 0 |
| `roadmap.tml` | `div.library-canvas.timeline-surface.canvas-viewport` | 0 | **4** | 0 |

Horizontal was 0 everywhere in every state, which is what R1.4 asks be recorded rather than assumed:
an inline box reserves space below its baseline and not beside it, so no horizontal spill was ever
observed and recording it is what makes a future regression in that axis visible.

**Two honesty notes on this result.** The `roadmap.tml` numbers were taken TWICE: the first reading was
in a window in which the backend then died with an internal CLR error, so it was discarded and retaken
on a healthy process - *check the origin still answers* cuts the same way for a positive as for a
negative. And `window.innerWidth` was read on every measurement, not once: it came back **0** after a
reload, because an emulated viewport is cleared when a turn ends - the exact failure this entry warns
about, met while running the entry.

## A feedback loop is drawn as a loop (causal-loop-diagram, arcs fix)

**A defect found by looking at the running app.** The diagram type is named after loops and drew
none. Both links of a two-variable loop took the shared tree connector, which anchors on the sides
facing each other, so `a -> b` and `b -> a` produced the same path and rendered as **one line with
an arrowhead at each end**. Longer cycles read as a concertina rather than a ring.

The fix follows the notation's own convention — and Vensim's, which gives each arrow a single
curvature handle: one control point, offset perpendicular to the chord, always to the same side of
**travel**. Reversing a link reverses travel, which flips the offset, which is what encloses the
loop.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/on-call/on-call.adp`, then
  `diagrams/causal-loop/reference/reference.adp`.
- **Expected**:
  - Two variables joined both ways draw an **ellipse** between them, not a single line. This is
    the case that was broken.
  - A longer cycle bows outward consistently and reads as a **ring**.
  - Each link is a single arc, not an S-curve.
  - A loop's identifier sits inside a small **circular arrow** at the loop's centre, turning
    clockwise for a reinforcing loop and anticlockwise for a balancing one.
  - A delayed link's two strokes cross the curve **at right angles to it**, wherever on the arc
    they fall — not drawn vertically against a near-vertical link, where they used to vanish into
    the line.
  - The polarity mark sits beside the arc near the arrowhead, not underneath it.

**Why the unit tests did not catch the original**: every canvas test asserted that link elements
existed and carried their classes and marks. Nothing asserted anything about the **shape**, so two
links drawing the identical path was invisible. The tests added with this fix compare the two
paths and the sign of each curve's control point, and were seen to fail against the old geometry.

- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session), worktree `claude/run` on develop)**: **passed.** Ran in the app now that a developer build opens already authenticated. `on-call.cld` and `reference.cld` both open. Every causal link is a single quadratic arc (one control point) — not a straight line, not an S-curve — and all ten of on-call's control-point offsets share one sign, so the arcs bow consistently to one side of travel and read as a ring. In `reference`, the mutual Population↔Births pair draws two arcs bowing to **opposite** sides — an ellipse, the case that was broken. Each loop identifier sits in a circular-arrow badge: R1 sweeps clockwise (SVG sweep-flag 1), B2 anticlockwise (0), and R3's badge — labelled reinforcing but computed balancing — is drawn anticlockwise with a `causal-loop-balancing` glyph. Polarity marks (+/−) sit beside each arc, and the delayed link's two strokes cross the arc at 90° (measured).

## A loop labelled R is arithmetically balancing, and the tool says so without changing the file (causal-loop-diagram, task 5.3)

The claim this diagram type exists to make: an `R`/`B` label is **checkable**, and a disagreement
is reported rather than corrected. The unit tests prove the arithmetic and the finding; what they
cannot prove is that a reader meets the finding, in the problems panel and in the property grid,
without the document changing under them.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/reference/reference.adp`. Read the problems panel.
  Select the loop labelled `R3` on the canvas and read the Properties panel (see the preamble on
  finding it). Then close the tab and reopen it.
- **Expected**:
  - The problems panel carries a warning naming `R3`, both polarities, and the negative count —
    something of the shape *"Loop R3 is labelled reinforcing but its links make it balancing: it
    runs through 3 negative links…"*.
  - The property grid shows **Computed polarity: balancing** and, because they differ, a second
    row **Stated polarity: reinforcing** whose read-only reason says neither is corrected for you.
  - `reference.cld` on disk is **byte-for-byte unchanged** — check with `git status`. Reporting is
    not correcting, and this is where that would break first.
  - Reopening shows the same finding: it is derived from the document, not remembered.
- **Also worth seeing while here**: loop `B4` reads **undecidable** rather than reinforcing or
  balancing, because the link into it states no polarity. Unknown is not none.

- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session), worktree `claude/run` on develop)**: **passed.** After **Validate**, the problems panel carries, verbatim: *"Loop R3 is labelled reinforcing but its links make it balancing: it runs through 3 negative links, and a loop reinforces on an even count and balances on an odd one…"* — R3, both polarities, the negative count. Selecting R3 on the canvas fills the property grid with **Computed polarity: balancing** (*"Counted from the polarity of the links around the cycle"*) and **Stated polarity: reinforcing** (*"'R3' claims this, and the arrows say balancing. Neither is corrected for you…"*), plus its cycle `population → crowding → sanitation → deaths`. `reference.cld` stayed byte-for-byte unchanged throughout (`git status` clean) — reporting is not correcting — and the finding is re-derived on Validate rather than stored. **B4** reads undecidable: *"…no stated polarity, so whether it reinforces or balances cannot be counted."* One incidental finding: the R3/B4 diagnostics point at `reference.adp:59`/`:64`, but the loops live in `reference.cld` and `reference.adp` is an 8-line registration/layout file — activating the problem opens it as text with *"Line 59 is not in this file any more."* A stale file:line on the loop diagnostics, unrelated to sign-in; flagged for a separate pass.
  person, or a session permitted to enter the placeholder credential.
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate against the implementation**, checked without running the app. Every named document, variable, loop, property row, action label and reason string exists and matches. `reference.cld` carries `loop R3` and `loop B4`; `Computed polarity` and `Stated polarity` are in `CausalLoopContextPropertyProvider.cs`.

## A delayed link draws its strokes, and the grid agrees with the drawing (causal-loop-diagram, task 5.3)

A delay is drawn as short strokes across the arrow. That is the whole of its visual existence, and
nothing in the backend suite can see whether it was painted.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/reference/reference.adp`. Find the link from
  `crowding` to `sanitation`. Select it and read the Properties panel. Then right-click it.
- **Expected**:
  - The link is drawn with the conventional strokes across it, and its polarity mark reads `−` at
    the arrowhead end. The other links carry no strokes.
  - The grid shows **Delayed: true** as a toggle, **Polarity: negative** as a list, and a
    **Weight** row that is empty rather than `0` — this link has no weight, and an unweighted link
    is not a link of weight zero.
  - The context menu offers **Not delayed** (the opposite of what the link states), **Same
    direction (+)** as available, and **Opposite direction (−)** as *unavailable* with the reason
    *"This link already states −."*
- **Then**: toggle the delay off from the menu and confirm the strokes disappear; one Undo brings
  them back and the `.cld` returns byte-for-byte.

- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session), worktree `claude/run` on develop)**: **passed.** The `crowding → sanitation` link is the only delayed one: two strokes across it, its polarity mark reads `−`, no other link carries strokes, and the strokes cross the arc at 90°. Its grid shows **Delayed: true**, **Polarity: negative**, and a **Weight** row that is empty rather than `0`. The context menu offers **Not delayed**, **Same direction (+)** as available, and **Opposite direction (−)** as unavailable with the reason *"This link already states −."* Choosing **Not delayed** removed exactly the `delayed` keyword from that one line (polarity preserved) and the strokes vanished; **one** Undo returned `reference.cld` byte-for-byte (`git status` clean) and a fresh reopen drew the strokes again.
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate against the implementation**, checked without running the app. Every named document, variable, loop, property row, action label and reason string exists and matches. `link crowding -> sanitation - delayed` is in `reference.cld` and carries no weight, while its sibling `population -> crowding` carries `weight=0.8` - so the empty-not-zero Weight row is genuinely testable. `"This link already states −."` is verbatim at `CausalLoopContextActionProvider.cs:338`.

## The self-organizing layout rearranges a diagram, and one undo puts it back exactly (causal-loop-diagram, task 5.3)

Requirement 6 in one gesture. The layout is deterministic, it is invoked rather than automatic, it
writes authored positions rather than touching the body, and it is **one** undo away rather than
one per variable — that last point is the one a test in the backend can assert but only a person
can feel.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/on-call/on-call.adp`. Drag two or three variables into a
  deliberate mess. Right-click the canvas **background** — with nothing selected — and choose
  **Arrange diagram**. Then press Undo **once**.
- **Expected**:
  - Arrange is offered on the background and **not** on a selected variable: it is diagram-wide.
  - The diagram rearranges. No two variables overlap.
  - `on-call.cld` is **unchanged**; `on-call.adp` gained a `layout:` block. An arrangement is an
    opinion about where things are drawn, not a change to what the document says.
  - **One** Undo restores the registration exactly, including any positions that were authored
    before the arrange. Not one undo per variable.
- **And the determinism**: with the diagram arranged, note two or three positions, restart the
  backend, reopen and arrange again from the same starting registration. The positions are
  identical. If they are not, Requirement 6.3 has broken in a way the two-process unit test did
  not reach.
- **A refusal is a pass, not a failure**: if a diagram is refused with a sentence naming its size,
  that is the specified behaviour (Requirement 6.7) and should be recorded as such rather than
  retried until it succeeds.

- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session), worktree `claude/run` on develop)**: **passed.** Right-clicking a variable offers Add link / Claim a loop / Rename / Remove — **not** Arrange; right-clicking the background offers **Add variable…** and **Arrange diagram**. After dragging a variable into a mess (which wrote a position to `on-call.adp` alone, `on-call.cld` untouched), **Arrange diagram** re-laid all eight variables with **zero** overlapping pairs and wrote a `layout:` block to `on-call.adp` while leaving `on-call.cld` byte-identical. **One** Undo moved all eight back together — a single step, not one per variable — and undoing the drag too returned the worktree byte-for-byte clean. The determinism-across-restart sub-check is left to the two-process backend unit test rather than re-run here.
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate against the implementation**, checked without running the app. Every named document, variable, loop, property row, action label and reason string exists and matches. `Arrange diagram` is at `CausalLoopContextActionProvider.cs:104` and `on-call` is present. Requirement 6.7 is conditional - a refusal only if determinism and overlap-freedom cannot both hold - so no refusal path in code is not a gap, and the entry is right to call a refusal a pass.

## Drop-target highlight actually paints (mindmap-diagram, bezier-connector pass)

The unit test can only assert the `mindmap-node-drop-target` class - jsdom does not load
`index.css`, so a later equal-specificity rule (`.mindmap-node rect`) silently overriding the
highlight's stroke is invisible to it. That is exactly how this bug shipped once: the class was
applied and the test passed while the node looked unchanged.

- **Preconditions**: backend + client running; a project with a mindmap of at least three
  nodes open in a diagram tab.
- **Actions**: press the mouse on a non-root node, drag it more than a few pixels, and hold
  the pointer over another node without releasing.
- **Expected**: the hovered node's border changes to the accent color (`--color-primary`,
  limegreen), thicker (2.5px) and dashed - visibly different from both the normal border and
  the focused node's solid accent border. Releasing the button clears it again.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Verified in the real browser, which is what the check needs — jsdom cannot see the paint. With the `mindmap-node-drop-target` class on a node, its rect computes to stroke `rgb(50,205,50)` (limegreen, the `--color-primary` token), width **2.5px**, dash-array **6 3** — versus a normal node's slate `rgb(51,65,85)`, 1.5px, solid. So the highlight paints and no equal-specificity rule overrides it (the exact defect this guards). The pointer plumbing that applies the class on a live drag could not be driven through React's synthetic-event capture and is covered instead by `MindmapCanvas.test.tsx`'s `dragOver` tests; the paint — the part only a browser can answer — is confirmed.
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate**, checked without running the app. `.mindmap-node-drop-target rect` is at `mindmap.css:79` with `stroke: var(--color-primary)`, `stroke-width: 2.5` and `stroke-dasharray: 6 3`; `--color-primary` is `limegreen` in both themes (`index.css:8`, `:27`). The entry's contrast claim is exact: focused (`:71`) is the same colour and width but **solid**, so the dasharray is the only difference. The overriding rule it warns about, `.mindmap-node rect`, sits at `:54` — *before* the highlight, so that hazard is currently not present; the entry describes how the bug shipped, not a live defect.

## Collapse in the node's context menu offers Expand afterwards (mindmap-diagram, bezier-connector pass)

Guarded by `DiagramElementActionFlowTests.ACompletedAction_RepushesTheSelectionsActions_SoACollapseOffersExpand`;
kept here because the stale label was found through the menu and is quickest to spot there.

- **Preconditions**: a mindmap with a branch node (has children) open in a diagram tab.
- **Actions**: right-click the branch node, choose **Collapse**; right-click the same node again.
- **Expected**: the branch's descendants disappear on the first click, and the re-opened menu
  reads **Expand** (not Collapse). Choosing Expand restores the branch.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** On a 37-node mindmap, right-clicking a branch node ("Layout") offered **Collapse**; choosing it hid that branch's descendants (37 → 27 nodes). Right-clicking the same node again the menu read **Expand**, not Collapse, and choosing it restored the branch (27 → 37). The mindmap files stayed git-clean (collapse/expand net-zero).
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate**, checked without running the app. The named guard `ACompletedAction_RepushesTheSelectionsActions_SoACollapseOffersExpand` exists at `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DiagramElementActionFlow.Tests.cs:264`, and the `Collapse`/`Expand` labels are in `MindmapContextActionProvider.cs:95`.

## C4 relationships are elevated to the level the view shows (c4-diagrams, manual pass)

Covered by `C4ElementMapperTests.AContextView_ElevatesAContainersRelationship_ToTheSystemThatContainsIt`,
kept here because the symptom is only obvious when you look at a rendered diagram: the context
view drew the systems it depends on and *no lines to them*, which reads as "no dependency".

- **Preconditions**: a project with a `.dsl` whose containers talk to an external software
  system, plus a `c4/context` `.adp` naming that model and view.
- **Actions**: open the context view.
- **Expected**: the system in scope has a labelled arrow to each external system its containers
  talk to, and no arrow to itself. Two containers talking to one external system draw one line,
  not two.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Opened the `c4/context` view (`courier.adp`). Mapping every rendered relationship's endpoints back to its node: the system in scope, **Courier Tracking**, carries one labelled arrow to **each** external system its containers talk to — Routing Optimiser (*Requests routes*), E-mail System (*Sends mail*), Mapping Provider (*Geocodes addresses*) — with **no** arrow to itself, and exactly one line per external (the dedup holds). The "systems drawn but no lines to them" symptom is absent.

## A C4 boundary contains only what is inside it (c4-diagrams, manual pass)

Covered by `C4LayoutTests.WhatIsNotInsideTheSystem_IsNotDrawnInsideItsBoundary`, kept here for
the same reason: the containers looked right, and only the boundary's geometry gave it away.

- **Preconditions**: a `.dsl` with a system containing two or more containers, plus a person and
  an external software system that talk to them; a `c4/container` `.adp` naming that view.
- **Actions**: open the container view.
- **Expected**: the system in scope is drawn as the dashed boundary and **not** also as a box;
  every container sits inside it; the person and the external system sit outside it; and nothing
  overlaps anything else.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Opened the `c4/container` view (`courier.containers.adp`). Courier Tracking is drawn as a **dashed** boundary (stroke-dasharray 8 6) and **not** also as a box. All six containers (Web Application, Single-Page Application, Courier App, API Application, Event Broker, Delivery Database) sit inside the boundary's box; all three people and all three external systems sit outside it; and no two nodes overlap.

## A C4 document ADP wrote opens in Structurizr Lite (c4-diagrams, task 39)

The reason for choosing the Structurizr DSL over a format of ADP's own was interoperability, and
that claim is only worth something checked against the real tool.

**The parsing half of this is now automated** and no longer needs a manual pass. `C4InteropTests`
hands ADP's own output to the real Structurizr CLI - the `structurizr-dsl` library Lite itself
parses with - covering the new-document template for all six working types, a document ADP
created and then edited (rename, description, added view) and then undid, and every fixture in
the corpus. It skips unless a CLI is provided, so run it as:

```powershell
$env:ADP_STRUCTURIZR_CLI = "<structurizr-cli>lib"; dotnet test --solution EtAlii.Adp.slnx
```

That check found two defects in ADP's own templates on its first run: a component view scoped as
`system.container` (identifiers are flat unless a document says `!identifiers hierarchical`, so
the dotted form named nothing), and a `deploymentEnvironment` with no nodes in it (an environment
is a property of the nodes deployed into it, so an empty one does not exist and the view bound to
nothing). Both are fixed and guarded.

An earlier version of this entry said the check could not run here because there was neither
Docker nor a JRE. A JDK has since been installed, and the CLI comes from Maven Central, which was
reachable all along - the entry was wrong, not the environment.

**The rendering half is now automated too**, and this entry no longer asks anyone to open a
browser to answer it. `C4InteropTests.EveryExport_IsStillWhatStructurizrDraws` exports every
fixture through the real CLI to Mermaid and compares the result to the diagrams committed in
`Fixtures/exports`; `C4ExportTests` then reads those committed diagrams with no JDK at all and
asserts that every element ADP believes is on a view was actually drawn on it. That is the
question a person used to answer by looking at Lite, asked without the browser, the container
or the person.

It is a real check rather than an exit code: a view that renders empty exports perfectly
happily, and ADP shipped exactly that once - the `c4/deployment` template with an empty
`deploymentEnvironment`, which bound to nothing and drew nothing while every command
succeeded. Replacing a committed diagram with an empty frame fails both checks, which was
verified rather than assumed.

**What is still manual** is what no text format can answer: whether the picture is any good to
look at. Three things, and only these:

- **Styling and themes.** That `styles` blocks and `theme` directives ADP round-trips actually
  resolve to the colours and shapes intended, rather than merely surviving as text. ADP carries
  these through untouched and has no model of what they mean.
- **Layout quality.** Structurizr's own auto-layout on a document ADP wrote: whether the result
  is readable, not merely non-overlapping. `C4LayoutCrowdingTests` pins that boxes do not sit
  on top of each other; nothing can pin that a diagram is pleasant to read.
- **The sidecar staying out of the way.** That ADP's `.layout.json` beside the `.dsl` draws no
  complaint from Lite, since it is ADP's own file and not part of the workspace.

- **Preconditions**: Docker (or a JRE and the Structurizr Lite jar). A project containing a C4
  model ADP created and then edited - rename an element, set a technology, add a second view -
  so the file under test is one ADP wrote, not one it only read. Give it a `styles` block.
- **Actions**: run Structurizr Lite against the folder holding the `.dsl`
  (`docker run -it --rm -p 8080:8080 -v /path/to/folder:/usr/local/structurizr structurizr/lite`)
  and open `http://localhost:8080`.
- **Expected**: the styles resolve, the auto-layout is readable, and the `.layout.json` sidecar
  causes no complaint. That every view is present and every element is on it is no longer part
  of this pass - the export checks own that, and they run on every `dotnet test`.
- **Result 2026-09-05**: **not blocked on sign-in — re-classified, not run here.** This entry's Actions run Structurizr Lite (Docker/JRE, `localhost:8080`) and its residual manual part is explicitly a subjective aesthetic judgment — styles resolve, the auto-layout is *readable*, the sidecar draws no complaint — against an external tool. None of that uses ADP's sign-in, so the developer-sign-in bypass does not unblock it; the earlier "needs a person to sign in" was a uniform-sweep mis-attribution. The objective halves it once covered are automated: `C4ExportTests` asserts every element ADP believes is on a view is drawn, and passes on every `dotnet test`; `C4InteropTests` parses ADP's output through the real Structurizr CLI and **skips** where that CLI is absent — all 43 of its cases were skipped, not passed, in this branch's full-suite gate, each saying so and naming `ADP_STRUCTURIZR_CLI`. An earlier revision of this line said both passed; the suite's own output says otherwise, so the interop half is covered only where the CLI is provisioned. What remains — the aesthetic judgment in Lite — is left for a person with Structurizr Lite; this specification does not claim it.

## The property grid's keyboard cadence, in a real browser (property-grid, task 6)

The commit cadence is pinned by unit tests, but no manual pass has ever seen it in a real
browser: the in-app browser pane could not composite frames during the task 6 pass, so key
events never reached the page (typing worked; `Enter`, `Ctrl+Enter` and `Escape` did not).
The blur path was verified live instead. Run this on a machine where the browser pane
displays, or in an ordinary browser against the dev servers.

- **Preconditions**: a project holding a mindmap with at least one node; the Properties panel
  visible; a node selected so its Text, Notes and Link rows show.
- **Actions and expected results**:
  1. Click into **Text**, type, and press **Enter**. → The value commits once: the canvas
     updates, and exactly one write reaches the backend (`Set mindmap.text` in the log).
  2. Click into **Notes**, type, press plain **Enter**. → A newline is added and *nothing*
     commits. Then press **Ctrl+Enter**. → It commits once.
  3. Click into **Text**, type, press **Escape**. → The field returns to the value the backend
     last described and nothing is written.
  4. Click into **Text**, change nothing, click elsewhere. → Nothing is written, and Undo does
     not become available for it.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **partially run — the bypass unblocked the panel; Enter/Ctrl+Enter still need an ordinary browser.** With a mindmap node selected, the Properties panel shows the editable Text, Notes and Link rows, and the developer build reaches them with no sign-in. Live: typing reaches the field, **Escape** returns Text to the backend's value and writes nothing (step 3), and a commit produced **exactly one** `Set mindmap.text` write with the canvas updating and one Undo restoring it — the commit-once-and-reach-the-backend heart of step 1. What the in-app browser pane still would not deliver is **Enter** and **Ctrl+Enter** (repeated Enter left the canvas unchanged and wrote nothing while typing and Escape worked) — the exact non-compositing-pane limitation this entry names. So the Enter/Ctrl+Enter cadence needs an ordinary browser against the dev servers; it is not an app defect, and the cadence stays pinned by unit tests.

## A property edit reaches a second connection (property-grid, task 6)

Requirement 5.2 says a committed change reaches every connection viewing the document, and the
grid follows the push rather than its own copy. Nothing automated covers two connections, and
the task 6 pass could not drive a second browser tab's canvas selection through the
non-compositing pane.

- **Preconditions**: the same project open in two browser tabs, the same diagram open in both,
  and the same node selected in both, with the Properties panel visible in each.
- **Actions**: in tab A, edit the node's **Text** and commit (Enter or by clicking away).
- **Expected**: tab B's canvas shows the new text without any interaction, and tab B's
  Properties panel shows the new value in its Text row - it re-describes from the push. Then
  press **Undo** in tab A: both tabs return to the old value, canvas and grid alike.
- **Result 2026-09-06 (developer build (Debug, `developer` env, bypass session))**: **still not run — attempted with two real tabs, and the reason it cannot be driven here is now measured rather than asserted.** Two tabs were opened on the same scratch project with the same `mindmap.mm` and its 37 nodes drawn in both. **Two separate obstacles, both in the harness:** **(a)** the second tab's document reports `visibilityState: "hidden"` even after it is fronted (the first tab reports `visible`), and no click on its canvas produces a selection — the same click, on the same node, at the same coordinates, selects in the visible tab and does nothing in the hidden one. So the *same node selected in both* precondition cannot be reached, and tab B's grid can never be brought to the node. **(b)** the edit itself will not commit through the pane: the Text field does take the new value (typed characters land and the field reads `Transforms pushed`), but **Tab** reverts it to the backend's value and clicking another canvas node reverts it too, while **Enter** is not delivered at all — the same key-delivery limitation recorded on the property-grid cadence entry above. Neither is an application finding: both are properties of the non-compositing pane, and both were checked in the direction that would have disproved them. The check wants an ordinary browser with two windows and is left unclaimed rather than faked.
- **Confirmed 2026-09-06 (Tester 2), and the mechanism is now measured rather than named.** Both obstacles reproduce independently of the run above, by a different method, so this is confirmed twice:
  - **The hidden second tab is a genuine, permanent limit, and fronting does not lift it.** A second pane tab reports `visibilityState: "hidden"`, `hidden: true`, `hasFocus: false` — and **still reports exactly that after `tabs_select` fronts it**, while the first tab goes on reporting `visible`. The pane has one permanently-visible document; every other tab is permanently hidden. That rules out the obvious workaround.
  - **But “hidden” is not itself the blocker, and the real one predicts which checks are affected.** The hidden tab *renders*: it drew the full project grid, 97 project elements, and carried the developer-session marker. What fails is that **`requestAnimationFrame` never fires there** — it did not fire in 1.2s, where the visible tab's fires at once. Browsers suspend rAF in hidden documents. So a check needing a **canvas repaint** in the second tab is genuinely undriveable, because canvases repaint on an animation frame that never arrives; a check needing only a **DOM** change there may still be driveable, since initial render and DOM updates plainly work. That is the distinction worth inheriting rather than “the tab is hidden”.
  - **The property grid does not commit through the pane, and it fails silently.** Typing into the Wardley grid's Name row and pressing Enter leaves the field showing the new text with no error — and `tea.owm` byte-identical, same `git hash-object`, no working-tree change. **Blur commits no better**: clicking away also left the file unchanged, so this is not an Enter-delivery problem alone.
  - **No commit is even attempted.** The network log for the edit carries `ContextService/Select` and `ContextService/DescribeProperties` and **no property-commit call at all**. So the value is taken into local state and the RPC is never emitted — *never attempted* rather than *attempted and rejected*, which is what makes the silence complete.

## The Ansible diagram opens from Add and draws its folder (ansible-structure-diagram, task 26)

The task 26 pass verified the backend half live - the type was discovered, both fixture folders
registered, and the problems panel showed all five rules with each problem's own file and line -
but could not drive the explorer's context menu or the canvas, because the Browser pane was not
displayed and so did not composite frames. Synthetic `contextmenu` and `dblclick` events do not
take. These are the checks that needs a real pair of eyes.

- **Preconditions**: the app running; a project open whose folder contains an Ansible project
  laid out the recommended way (the module's `Fixtures/infrastructure` tree is one - copy it
  somewhere outside the repository first, since nothing may write into the committed fixtures).
- **Actions and expected results**:
  1. Right-click the `infrastructure` folder in the explorer. → An **Add…** submenu offers
     **Ansible project structure (read-only)** among the diagram types.
  2. Choose it. → Exactly one file appears in the folder, `infrastructure.adp`, whose first line
     is `ansible/structure`. Nothing else in the tree changes - check `git status` if the folder
     is under version control.
  3. Double-click that `.adp`. → The diagram opens: `site.yml` leftmost, `webservers.yml` and
     `dbservers.yml` to its right, the three roles right of those, `tls.yml` right of `nginx`,
     and the two inventories with their variable folders in a band **below** the flow.
  4. Look at the edge styles. → `roles:` and `import_playbook` draw solid; the `include_tasks`
     from `nginx` to `tls.yml` draws dashed; the two `dependencies` edges into `common` draw in
     a third style. Each carries its directive as a label, and the `postgres` edge also shows
     its `when:` as written.
  5. Double-click the `nginx` role. → The explorer reveals `roles/nginx`, expanding to it.
  6. Press **Enter** with a node focused. → The same reveal happens from the keyboard.
  7. Select the `nginx` role and look at **Properties**. → Every row is greyed with a reason
     beside it, and no row has an editable field. The **Depends on** row's reason names
     `roles/nginx/meta/main.yml`, not the role folder.
  8. Look at the **Toolbox**. → It says the type offers no elements, and nothing can be dragged
     onto the canvas.
  9. Delete `roles/common/` in a text editor or file manager, leaving the app open. → The
     diagram loses the `common` node without being reopened, and the problems panel gains
     `ansible.role-missing` entries pointing at `webservers.yml` and `dbservers.yml`.
 10. Put `roles/common/` back. → Both the diagram and the panel return to their earlier state.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **largely passed — the visual and interaction core verified live; three sub-steps noted.** Opened `example 1/infrastructure` (the registered fixture tree). **Layout** (3): by measured positions, `site.yml` is leftmost, `webservers.yml`/`dbservers.yml` to its right, the three roles right of those, `tls.yml` right of `nginx`, and the inventories with their `group_vars`/`host_vars` in a band below the flow. **Edge styles** (4): three distinct dash patterns — solid for `roles:`/`import_playbook`, one dash for the `include_tasks` `nginx → tls`, a third for the two `dependencies` into `common` — each labelled with its directive, and the `postgres` edge shows its `when:` (`roles: when postgres_enabled | default(true)`). **Reveal** (5): double-clicking the `nginx` role selects `roles/nginx` in the explorer. **Properties** (7): every row greyed and non-editable with a reason, and the **Depends on** (and Meta) reason names `roles/nginx/meta/main.yml`. **Toolbox** (8): *“This diagram type offers no toolbox elements.”* **Live edit** (9–10): moving `roles/common` aside dropped the `common` node from the diagram with no reopen, and restoring it brought the node back — worktree byte-clean after. Noted, not app defects: the keyboard reveal (6) is not deliverable through the in-app pane (as with the property-grid entry); the `ansible.role-missing` diagnostics on removal (9) did not populate in this freshly-opened project (the diagram updated live but validation was not re-run there); and the Add-flow creation of `infrastructure.adp` (1–2) was not re-tested here because this folder is already registered (folder-add-registration covers it).

## An Ansible diagram in a subfolder reveals the right file (ansible-structure-diagram, task 26)

Guards the bug the integration flow found: the rule set works in the diagram folder's terms and
core in the project's, so a diagram that is not at the project root once attributed every problem
to a path that did not exist. Automated now, but the panel's reveal is the part a person sees.

- **Preconditions**: a project whose root contains the module's `Fixtures/broken` tree in a
  **subfolder**, registered with a `.adp` inside that subfolder.
- **Actions**: open the Errors and Warnings panel and double-click the
  `The role 'absent-role' has no folder under roles/.` row.
- **Expected**: the explorer reveals `<subfolder>/playbooks/deploy.yml` - the playbook that
  named the role - and not the `.adp`, and not a path that fails to resolve.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Registered the module's `Fixtures/broken` tree as a **subfolder** diagram (`diagrams/ansible-structure/broken-demo/broken/`, `.adp` = `ansible/structure`). After Validate the panel carries *“The role 'absent-role' has no folder under roles/.”* pointing at `diagrams/ansible-structure/broken-demo/broken/playbooks/deploy.yml:6` — a subfolder-relative path that **resolves**, which is the bug this guards (a non-root diagram once named a path that did not exist). Double-clicking it revealed `deploy.yml` — selected in the explorer and opened in the editor — the playbook that named the role, not the `.adp` and not a failing path. Scaffold removed afterwards; worktree clean.

## `dotnet test` discovers tests in a long worktree path (build tooling)

Windows' 260-character `MAX_PATH` silently breaks the backend build in deep checkouts. MSBuild's
`Exists()` returns false - without any error - for a path over 260 characters, so the SDK's
`_CreateAppHost` condition `Exists('@(IntermediateAssembly)')` evaluates false and `apphost.exe`
is never produced. Because every xUnit v3 test project is an executable whose apphost *is* the
test host, the run surfaces as `dotnet test` reporting `Zero tests ran` for project after
project, which reads like an empty suite rather than a broken build.

`src/Directory.Build.targets` guards this with error `ADP0001`, but the guard only proves it
fails loudly; this check proves it does not fail at all. The repository is inside the limit from
the main checkout - a worktree under `.claude/worktrees/<name>/` adds ~36 characters plus the
worktree name, and the longest project path needs the machine's long-path support.

- **Preconditions**: Windows; `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled`
  is `1` (set it as admin; existing processes must be restarted to pick it up). A git worktree
  whose directory name is at least 35 characters, e.g.
  `git worktree add .claude/worktrees/a-deliberately-long-worktree-name develop`.
- **Actions**: from that worktree's `src/backend/`, run `dotnet test --solution EtAlii.Adp.slnx`.
- **Expected**: the run completes with a non-zero test total (1338 at the time of writing), exit
  code 0, and no `ADP0001`, `ADP0002` or `MSB3030` error. A summary of `Zero tests ran` with
  `total: 0`, or `error: 54`, means long-path support is not in effect for the process that ran
  the build - it is not a test failure.
- **Note**: a genuinely empty run does exit non-zero (5 for zero tests, 8 for a filter matching
  nothing), so any CI step must check the exit code rather than grepping the output for
  `failed`. Grepping alone reads a zero-test run as a pass.
- **Result 2026-09-03**: **passes** —
  verified in a 39-character worktree (`a-deliberately-long-worktree-name-x`,
  develop at 228721e7): full `dotnet test --solution` run, exit 0 with tests discovered and
  executed, and no `ADP0001`, `ADP0002` or `MSB3030` anywhere in the output.

## A real pipeline opens, reads and still runs (azure-pipeline-diagram, task 29)

The round trip is this module's headline promise and the one thing no unit test can fully settle:
that a pipeline ADP has opened and edited is still a pipeline Azure DevOps will run. The fixtures
prove byte-identical round trips over a corpus; this proves it over a pipeline somebody's releases
actually depend on.

- **Preconditions**: backend + client running; a project whose folder contains a real
  `azure-pipelines.yml` from a repository you have, ideally one with several stages, a `dependsOn`
  or two and at least one template reference. The folder is a git checkout, so `git diff` works.
- **Actions**:
  1. In the explorer, select the `.yml` and choose **Add as diagram…**, then pick **Azure DevOps
     pipeline**. An `.adp` appears beside it.
  2. Open the `.adp`.
  3. Read the canvas against the file: every stage present, arrows matching each `dependsOn`, and
     an arrow between consecutive stages that declare none.
  4. Right-click a stage with jobs and choose **Show jobs**; then **Hide jobs** again.
  5. Select a stage. In the Property Grid, change **Display name**, then press Enter.
  6. Run `git diff` in the project folder.
  7. Press Ctrl+Z, and run `git diff` again.
- **Expected**:
  - Step 3: the drawing says what the file says. An arrow nobody wrote is drawn differently from
    one that is written down.
  - Step 4: the stage's jobs appear inside it and disappear again. Nothing is written to the file
    at any point — `git diff` after this step alone is empty.
  - Step 6: exactly one changed line, the `displayName` you set. No re-indentation, no reordered
    keys, no quoting changes, no lost comments, no changed line endings.
  - Step 7: `git diff` is empty. Byte for byte, not merely equivalent.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Opened `multi-stage.adp`. All five stages are present and every arrow matches the file's `dependsOn`: Build → Test, Test → DeployStaging, Test → DeployProduction, and DeployStaging/DeployProduction → Notify (this pipeline declares `dependsOn` on every non-first stage, so the implicit consecutive-stage arrow is not exercised here — that styling is unit-tested). Right-clicking Test → **Show jobs** brought Unit and Integration inside it and **Hide jobs** removed them, with `git diff` empty after — no write. Editing a stage's **Display name** and committing produced a `git diff` of **exactly one line** (the `displayName`), with the `# Fan-in:` comment, indentation, key order, quoting and line endings all untouched; one **Undo** returned `git status` to empty, byte for byte. (The commit was made by blurring the field rather than pressing Enter, since the in-app pane does not deliver Enter; the write and the byte-level round trip are identical.)

## An edited pipeline still validates against Azure DevOps (azure-pipeline-diagram, task 29)

A file that round-trips byte-identically is safe by construction; a file this module *edited* is
only safe if the lines it wrote are lines Azure accepts. Nothing in ADP knows Azure's schema, so
this check is the only thing that closes that gap.

- **Preconditions**: as above, plus either the Azure Pipelines VS Code extension (which validates
  YAML against the published schema) or an Azure DevOps project you can queue a run in.
- **Actions**:
  1. On the open pipeline, add a stage from the Toolbox, add a job to it, and add a step to that
     job.
  2. Select the new stage and set **Depends on** to an earlier stage from the list.
  3. Save nothing by hand — the edits write themselves.
  4. Open the resulting `.yml` in the editor with the schema extension, or queue it in Azure
     DevOps.
- **Expected**: no schema errors, and the pipeline queues. Specifically the added stage has a job
  and the job has a step — an empty stage or job is a schema error, and this module writes the
  smallest runnable block for exactly that reason.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not fully runnable here — the validator is external, and it is not a sign-in blocker.** The developer build unblocks the on-canvas edits, but the check's expectation — *no schema errors, and the pipeline queues* — is answered only by the Azure Pipelines VS Code schema extension or by queuing a run in an Azure DevOps project, neither of which exists in this environment and neither of which sign-in gates. The objective ADP-side invariant this rests on — that adding a stage writes the smallest runnable block, a stage with a job and the job with a step, never an empty stage or job — is covered by the azure-pipeline writer unit tests. The schema/queue confirmation is left for a person with the extension or an Azure DevOps project; the earlier sign-in stamp was a partial mis-attribution.

## A pipeline shows what it cannot do rather than doing it wrongly (azure-pipeline-diagram, task 29)

The editable set is deliberately narrow, and the value of that only shows up when the diagram
refuses something. Worth a look because a refusal that is silent reads as a bug.

- **Preconditions**: a pipeline open, containing a `template:` reference that resolves inside the
  project (so its jobs appear on the canvas).
- **Actions**: right-click a job that came from the template; then select it and look at the
  Property Grid.
- **Expected**: the context menu offers nothing at all for that job. The Property Grid shows its
  properties with a reason beside each naming the template file the value lives in — not a greyed
  box with no explanation.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Opened `templates.adp`, whose `Build` stage draws its jobs from the local template `templates/build-jobs.yml`. **Show jobs** surfaced the job *“Compile from the template”*, annotated *“From diagrams/azure-pipeline/example 1/templates/build-jobs.yml: edit it there.”* Right-clicking that job the context menu offered **nothing at all**. Selecting it, the Property Grid showed its Name, Display name, Kind and Depends-on rows, each non-editable with the reason *“This comes from diagrams/azure-pipeline/example 1/templates/build-jobs.yml…”* — naming the template file, not a bare greyed box. (The repository-resource template `deploy-stages.yml@shared` also renders as a dashed indeterminate box saying how many stages run is decided when the pipeline runs — the same refuse-rather-than-guess principle.)

## A map authored in onlinewardleymaps.com opens looking like the same map (wardley-map, task 26)

The corpus test proves ADP's parser and the reference parser agree about what the text *means*;
nothing automated can say the picture agrees. The failure this guards against is a map that
parses perfectly and is drawn wrong - an axis inverted, a band in the wrong place - which every
unit test in the module would still pass.

- **Preconditions**: backend + client running; a project folder on disk; a browser open at
  [onlinewardleymaps.com](https://onlinewardleymaps.com/).
- **Actions**: in that editor, load the built-in tea shop example (or paste
  `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/Fixtures/tea-shop.owm`).
  Save the same text as `tea.owm` in the project folder, beside a `tea.adp` whose only line is
  `wardley/map`. Open `tea.adp` in ADP and put the two windows side by side.
- **Expected**: the same components in the same relative places. The anchor sits at the top,
  Cup of Tea below it, Kettle and Power towards the bottom left; every link joins the same pair.
  A component that is high in one and low in the other means the visibility axis is inverted;
  one that is left in one and right in the other means maturity is.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed (the drawn-right check; the side-by-side is external).** Opened `tea.adp` and checked the rendered positions against the `.owm`'s `[visibility, maturity]` numbers: the anchors sit at the top, Cup of Tea below them, Kettle and Power toward the bottom, maturity increases left→right (Kettle in the Custom-Built band, Water in Commodity) and visibility bottom→top. Neither axis is inverted and no band is misplaced — which is the failure this guards. The onlinewardleymaps.com side-by-side itself is an external tool and is not needed to answer the axis-inversion question, which the coordinates settle.

## The four evolution stage labels are legible at the default zoom (wardley-map, task 26)

`WardleyEvolutionTests` pins where the three boundaries are and the canvas test asserts the
labels exist in the DOM. Neither can say whether a reader can read them - and the labels carry
their parentheticals ("Product (+rental)", "Commodity (+utility)") precisely because the short
forms are a different claim, which only helps if the long forms fit.

- **Preconditions**: an `.owm` open in a diagram tab, at the zoom the tab opens with.
- **Actions**: look along the bottom of the map without zooming or panning.
- **Expected**: four labels - Genesis, Custom Built, Product (+rental), Commodity (+utility) -
  each fully readable, none clipped at the edge of the canvas, none overlapping its neighbour,
  and each sitting under the band it names.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** With `tea.owm` open at the tab's default zoom, the four band labels render along the bottom with their full parentheticals — **Genesis**, **Custom Built**, **Product (+rental)**, **Commodity (+utility)** — each under its band. Measured: no label overlaps its neighbour (each one's right edge is left of the next one's start) and none is clipped (all fall within the canvas, the rightmost ending well inside the right edge).

## Dragging a component and reopening the file elsewhere shows it moved (wardley-map, task 26)

The round-trip and command tests assert ADP writes what it means. This asserts the other
ecosystem agrees, which is the whole point of Requirement 3: a map ADP has touched must still
be a map onlinewardleymaps.com can open, with the drag visible in it.

- **Preconditions**: `tea.adp` / `tea.owm` open in ADP, as above.
- **Actions**: drag `Kettle` clearly to the right (more evolved) and drop it. Open `tea.owm` in a
  text editor, copy its whole text, and paste it into onlinewardleymaps.com.
- **Expected**: the file's `component Kettle [...]` line carries a larger second number than
  before and the same first number; the rest of the file - comments, blank lines, the order of
  the statements, the line endings - is untouched; and the map in the browser draws Kettle in
  its new place with everything else where it was.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed for the move, with two byte-level findings; the browser render is external.** Dragging `Kettle` clearly to the right wrote `tea.owm` as `component Kettle [0.43, 0.35]` → `[0.43, 0.5046]` — a larger second number (more evolved), the same first number, and the Kettle line otherwise identical, with every other component, comment, blank line and statement order untouched. Two deviations from *“the rest of the file is untouched”*, both worth a fix: **(a)** the write **stripped the file's leading UTF-8 BOM** (`-﻿title` / `+title`), and Undo reverted the position but not the BOM (it is a write-side normalization); **(b)** the drag left an untracked `tea.identities.json` sidecar beside the `.owm`. Opening the result in onlinewardleymaps.com to confirm the drawn move is an external step, not run here.

## An element selected on the canvas fills the property grid (wardley-map, task 26)

`WardleyContextPropertyProviderTests` proves the rows are contributed and `WardleyMapFlowTests`
proves they reach a caller. Neither can say the panel renders them usefully: a read-only reason
is a `display: block` span with no truncation, so one that is too long grows its row rather than
clipping, and that is only visible on screen.

- **Preconditions**: `tea.adp` open in a diagram tab, with the Property Grid panel visible.
- **Actions**: click `Kettle` on the canvas and read the panel. Then click the Evolution stage
  row and try to type into it.
- **Expected**: the panel's heading is `Kettle`; the rows are grouped under Identity, Position
  and Strategy; Visibility and Maturity show the file's own numbers; Evolution stage shows
  `Custom Built` and cannot be typed into, with a sentence beside it saying to change Maturity
  instead - one or two lines, not a paragraph that pushes the rest of the panel down.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **FAILED — a real defect found by running the app.** Clicking `Kettle` on the canvas (tried repeatedly, including a precise click on the zoomed-in dot) does **not** fill the Property Grid: the backend logs *Selected …/tea.adp … as Activate* and *Described 0 properties for …/tea.adp* — the innermost selection resolves to the `.adp` **file**, never the element, so the heading stays the file and Kettle's Identity/Position/Strategy rows never appear. Root cause, confirmed in source: **`WardleyCanvas.tsx` reports no element context-selection.** The working modules do — e.g. causal-loop wires `onSelect → select(elementSelectionOf(entryId, path, id, gesture))` to each element's `onClick` and imports `useContextSelection`/`useElementContextMenu`; `WardleyCanvas` has none of these, its element `<g>` carries only an `onMouseDown` drag handler, and a comment in it admits the `.adp` entry *“a selection reports as its outer level … task 19's context resolver needs and nothing here does.”* The backend `WardleyContextSourceResolver` and `WardleyContextPropertyProvider` exist and are unit-tested, but the client never hands them an element. This is the same class of bug as the already-fixed causal-loop *“context surface is reachable”* entry, and it wants the same fix — wire element-selection reporting into `WardleyCanvas`, with a guard. Flagged to the Scrum master.

## The ruler stays fixed to the view while its labels slide with the content (timeline-diagram, task 31)

- **Preconditions**: a project holding a `.tml` with several elements spanning a few months
  (for example three elements over January-June); the timeline open on the canvas.
- **Actions**: note the ruler strip's vertical position at the bottom of the view and the
  horizontal position of one month label. Drag the canvas background right by roughly 80 pixels
  (a pan), including some vertical movement.
- **Expected**: the ruler strip itself does not move at all - it stays at the bottom of the view
  whatever the pan's vertical component - while the month label slides horizontally by exactly
  the pan distance, staying over the time it names. A label panned out of the view disappears
  rather than piling up at the edge.
- **Result 2026-09-01**: **passes** —
  verified against the running app: ruler top identical before and after a pan
  with a 100px vertical component; "Mar 2026" slid exactly +80px for an 80px pan; "2026"
  disappeared once panned off-view.

- **Result 2026-09-05**: **text reviewed against the implementation, no drift.** Not re-executed
  - the client dev server is down and the pass waits on `developer-sign-in-bypass`.
  Reviewed in a sweep of the seven timeline entries, whose 2026-09-01 outcomes were recorded
  under the older `Verified` label and normalised on 2026-09-05 - see the preamble.

  `TimelineRuler.tsx` has not changed since `a68d0701` (2026-08-31), before the verification. The
  clause that could have drifted is *"a label panned out of the view disappears"*: the timeline
  now filters its **content** to the reported viewport (`337938aa`, view-delta-adoption task 5),
  so it was worth checking whether labels are filtered too. They are not - ruler ticks are
  computed from the view's own `startSeconds` and `secondsPerPixel`, never from delivered
  elements, so the ruler describes time the reader can see whether or not any element there has
  arrived yet. The entry still describes the application.

## The ruler's labels change their unit with the zoom (timeline-diagram, task 31)

- **Preconditions**: as above.
- **Actions**: zoom in (ribbon Zoom In or mouse wheel) until the visible span is a few weeks;
  then zoom out far past the starting view.
- **Expected**: the labels change unit rather than crowding or vanishing - months at the
  starting span, day labels ("Mar 12", "Mar 19") once weeks are visible, then years and
  multi-year steps ("2020", "2022", "2024") when zoomed far out. At no zoom level do labels
  overlap or disappear entirely.
- **Result 2026-09-01**: **passes** —
  verified against the running app across that whole range.

- **Result 2026-09-05**: **text reviewed against the implementation, no drift.** Not re-executed.
  Reviewed in a sweep of the seven timeline entries, whose 2026-09-01 outcomes were recorded
  under the older `Verified` label and normalised on 2026-09-05 - see the preamble.

  `timelineTicks.ts` unchanged since `a68d0701` (2026-08-31). Same reasoning as the entry above:
  unit selection is a function of the visible span alone, so view filtering cannot empty the
  ruler. *"At no zoom level do labels overlap or disappear entirely"* is a claim about the tick
  generator, which is untouched.

## Connections follow a drag while it is in progress, and Escape abandons it (timeline-diagram, task 31)

- **Preconditions**: as above, with at least one connection between two elements.
- **Actions**: press on a connected element and move the pointer without releasing; watch the
  connection's curve and the hint above the element; press Escape; then check the `.tml` file.
- **Expected**: the curve is redrawn continuously as the element moves - not only on release -
  and the hint shows the times and row the element would land on (for example
  "2026-01-31 · row 1"). Escape returns the element and its curves to where they started, and
  the file on disk is byte-for-byte untouched.
- **Result 2026-09-01**: **passes** —
  verified against the running app: the connection path changed mid-drag, the
  hint read "2026-01-31 · row 1", Escape restored the original path, and the file still read
  `begin: 2026-01-05`.

- **Result 2026-09-05**: **text reviewed against the implementation, no drift.** Not re-executed.
  Reviewed in a sweep of the seven timeline entries, whose 2026-09-01 outcomes were recorded
  under the older `Verified` label and normalised on 2026-09-05 - see the preamble.

  `TimelineCanvas.tsx` **did** change after the verification - twice - so this one needed reading
  rather than assuming. `3bc98214` migrated it onto the shared scroll view and `58934ae1`
  (2026-09-05) added inline renaming. Neither touched the drag: `dragRef` still carries the
  in-flight gesture, the preview still overrides positions while it runs, the hint is still
  rendered, and Escape is still handled with Requirement 6.6 named at the call site. The entry
  still describes the application.

  **One thing the entry does not cover, a gap rather than drift**: `58934ae1` added `F2` to this
  canvas's structural shortcuts, so an element's label can now be renamed in place. No entry here
  mentions it. It arrived with four client tests each seen to fail against its own defect, so it
  is not unguarded - but a reader using this file as the inventory of timeline gestures will not
  learn that F2 exists.

## A toolbox drop lands an Element where it was dropped (timeline-diagram, tview pass)

- **Preconditions**: a `.tml` timeline open on the canvas; the Toolbox showing Element and Moment.
- **Actions**: drag Element from the Toolbox and drop it on empty canvas at a chosen time and
  row; repeat over an existing element.
- **Expected**: a "New element" appears at the dropped time and row immediately - no dialog -
  and one undo removes it. Dropping never answers "That action is not available for this item"
  (the placement target must discover the add actions, or executing by id resolves nothing).
- **Result 2026-09-01**: **passes** —
  verified against the running app after fixing exactly that: a placement target
  that discovered no actions made every drop a no-op with that message.

- **Result 2026-09-05**: **text reviewed against the implementation, no drift.** Not re-executed.
  Reviewed in a sweep of the seven timeline entries, whose 2026-09-01 outcomes were recorded
  under the older `Verified` label and normalised on 2026-09-05 - see the preamble.

  Checked specifically because `58934ae1` altered the timeline's context actions the same day. It
  did not touch this path, and said so in terms: rename and relabel gained a target `ElementId`
  while *"give-an-end and add-element deliberately do not - one asks for a date and the other for
  text that does not exist yet."* So a drop still places a "New element" with no dialog. The
  entry's warning about a placement target that discovers no actions still names a real trap.

## The context menu removes elements and relations (timeline-diagram, tview pass)

- **Preconditions**: as above, with at least one relation.
- **Actions**: right-click an element with no relations and choose Remove; right-click a
  relation and choose Remove relation; right-click an element with relations and confirm the
  dialog that names the relation count.
- **Expected**: each removal happens and is one undo away. A relation-free Remove must act
  immediately - an execute that answers Completed without dispatching has done nothing, because
  the commit leg only runs after a dialog.
- **Result 2026-09-01**: **passes** —
  verified against the running app after fixing exactly that: menu-Remove on a
  relation-free element silently did nothing until the execute leg dispatched the command.

- **Result 2026-09-05**: **text reviewed against the implementation, no drift.** Not re-executed.
  Reviewed in a sweep of the seven timeline entries, whose 2026-09-01 outcomes were recorded
  under the older `Verified` label and normalised on 2026-09-05 - see the preamble.

  `TimelineContextActionProvider.cs` changed on 2026-09-05 (`58934ae1`), by four lines, all of
  them adding `target.ElementId` to rename and relabel. Removal is untouched, so the entry's
  sharpest clause - *"an execute that answers Completed without dispatching has done nothing,
  because the commit leg only runs after a dialog"* - still describes the live hazard.

## A relation dragged onto empty space creates the element it reaches (timeline-diagram, tview pass)

- **Preconditions**: as above; an element selected so its anchors show.
- **Actions**: do this twice, once from each side anchor - **the two anchors are not symmetric**
  and the direction is the thing under test. Drag from the **END** anchor and release over empty
  canvas; then, on a fresh element, drag from the **BEGIN** anchor and release over empty canvas.
- **Expected**: each time a "New element" appears at the release point, joined by one relation -
  one history entry, one undo removing both. Releasing back on the source cancels quietly.
  **The direction differs by anchor, and both are correct:**
  - From the **END** anchor, the relation runs **from the dragged element to the new one**.
  - From the **BEGIN** anchor it arrives **reversed**: the new element becomes the relation's
    **source**, pointing into the dragged element's start (`1c92704c`). Reading this as a wrong
    direction is the mistake this wording exists to prevent - it is the feature.
- **Result 2026-09-01**: **passes** —
  verified against the running app: elements 4→5 and relations 2→3 from one
  gesture.

- **Result 2026-09-05**: **drifted, mildly: the entry describes one of two anchors as though it
  were both.** Not re-executed.
  Reviewed in a sweep of the seven timeline entries, whose 2026-09-01 outcomes were recorded
  under the older `Verified` label and normalised on 2026-09-05 - see the preamble.

  The entry says *"drag from a side anchor and release over empty canvas"* and expects *"a
  relation from the source to it"*. Since `1c92704c` that holds for one side only: a relation
  dragged from the **BEGIN** anchor *"now arrives reversed - the landing (element or fresh
  placement) becomes the relation's source pointing into the dragged element's start"*. From the
  begin anchor the new element is therefore the **source**, not the target, and the check as
  written would read a correct reversal as a wrong direction.

  `1c92704c` is dated **2026-09-01**, the same day as the verification, so whether this is drift
  or an imprecision present at the time cannot be settled from the dates - and it does not
  matter, because the amendment is the same either way. **Suggested amendment**: name which
  anchor is dragged and state both directions. The 4-to-5 element and 2-to-3 relation counts in
  the recorded result hold for either direction, which is why the original pass did not catch
  it.

## Right-drag pans; Tab and Enter add; scrollbars pan (timeline-diagram, tview pass)

- **Preconditions**: as above. The scrollbars are the shared `CanvasScrollbars` since
  `3bc98214`; `TimelineScrollbars` was removed that day and no longer exists to look for.
- **Actions**: right-press empty canvas and drag (no browser menu may appear); select an
  element and press Tab, then Enter; drag each scrollbar thumb, including all the way into the
  margin past the content.
- **Expected**: the right-drag pans exactly as a left-drag; Tab adds an element two days after
  the selected one on its row, Enter adds one below on the next row, neither asking anything;
  the thumbs pan the view.
  **What the thumb's size means, since view-delta-adoption task 5 (`337938aa`):** the extent it
  describes is **what has been delivered so far, plus a margin** - half the span horizontally,
  two rows vertically - not the whole document. `scrollAxesOf` measures `model.elements`, which
  is the subset the session has sent for the reported viewport. So **panning to the end of the
  thumb and finding yet more timeline appear is correct**, not a defect: the drag reports a new
  view, the deltas arrive, and the extent grows. Do not use the thumb's size to judge how much
  timeline exists. This is the opposite of the `Fit to View` defect recorded below, where the
  delivered set collapsed inward instead - the two look alike from the outside.
- **Result 2026-09-01**: **passes** —
  verified against the running app (Tab and right-drag live; Enter and the
  thumbs through the same handlers in the test suite).

- **Result 2026-09-05**: **drifted twice, and the second one changes what the check means.** Not
  re-executed.
  Reviewed in a sweep of the seven timeline entries, whose 2026-09-01 outcomes were recorded
  under the older `Verified` label and normalised on 2026-09-05 - see the preamble.

  **First: the component it was verified against no longer exists.** On 2026-09-03 `3bc98214`
  migrated the timeline onto the shared scroll view - `TimelineScrollbars` was **removed** and
  `CanvasScrollbars` renders in its place. The recorded pass says the thumbs were covered
  *"through the same handlers in the test suite"*; those handlers are now shared with three other
  canvases. The behaviour is pinned by `scrollGeometry.test.ts`, which the migration says
  reproduces the old parameterisation term for term, so this is drift in what the entry *points
  at* rather than in what it claims.

  **Second, and more important: "the content's extent" no longer means what it meant.** When this
  was verified the whole timeline arrived at open, so the content was the document and the thumbs
  described it. Since `337938aa` (view-delta-adoption task 5) the session filters to the reported
  viewport, and `scrollAxesOf` measures `model.elements` - **the delivered subset**. The thumb now
  describes what has arrived, not what exists, so its size cannot be used to judge how much
  timeline there is.

  **It is not a hard stop, and I checked that rather than assuming it.** Both axes keep a margin
  past the content - horizontally half the span, vertically a fixed two rows - so there is always
  somewhere to drag to; dragging there reports a new view, the deltas arrive, and the extent
  grows. The loop is self-extending, which is the opposite of the `Fit to View` defect recorded
  below, where the delivered set collapsed inward to a fixed point. Worth saying plainly because
  the two look alike from the outside.

  **Suggested amendment**: replace *"within the content's extent"* with a sentence saying the
  extent covers what has been delivered plus a margin, and that panning into the margin brings
  more. As written, a tester who pans to the end of the thumb and finds more timeline appearing
  cannot tell whether that is the design or a bug.

## A dependency graph shows no ruler, no dates and no moments (dependency-graph, task 4.1)

- **Preconditions**: `src/examples/diagrams/dependency-graph/example-1/` opened as a project,
  with `services.dgr` on the canvas.
- **Actions**: look at the whole canvas; open the Toolbox; select a node and read the property
  grid; select an edge and read it too.
- **Expected**: no ruler strip along any edge, no date anywhere on the canvas or in either grid,
  and no moment marker. The Toolbox offers **Node** and nothing else. A node's grid rows are
  Label, X and Row - begin, end and duration are **absent**, not blank. An edge's rows are its
  label plus its two ends, read-only.
- **Why manual**: the absences are the requirement (Requirement 3.3 calls a ruler, a date or a
  moment appearing here a defect), and an absence in a running UI is what a test suite is least
  able to see.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Opened `services.adp`. There is no ruler strip along any edge, no date anywhere on the canvas, and no date or moment row in the property grid of a selected node (which shows Label and a Placement of X and Row). The Toolbox offers a single **Node** entry and nothing time-shaped. (A node click does fill the grid here — heading *Order service* — so the module reports element selection, unlike wardley.)

## Every edge visibly points at its dependency (dependency-graph, task 4.1)

- **Preconditions**: as above.
- **Actions**: follow the edge from Checkout web to API gateway, and the one from Order service
  to Orders database; then select a node, drag from its **left** anchor onto another node, and
  look at the new edge.
- **Expected**: each edge carries an arrowhead at the end it depends on - the arrow on the first
  points at API gateway, not at Checkout web. The left-anchor drag produces an edge pointing at
  the node the drag *started* from, because what depends on a node arrives at it.
- **Why manual**: an arrowhead drawn at the wrong end still renders a plausible graph, so the
  failure is silent to anything that only checks that an edge exists.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Every one of the seven relation lines carries an arrowhead at its end via a shared `marker-end` (`#dependency-graph-arrowhead`), and each path runs from the dependent node to the one it depends on, so the arrowhead lands on the dependency — Order service → Payment, Inventory, Orders-database and RabbitMQ; Payment → Payments-database; Inventory → Orders-database; Notification → RabbitMQ.

## Edges follow a drag while it is in progress, and Escape abandons it (dependency-graph, task 4.1)

- **Preconditions**: as above, with at least one edge between two nodes.
- **Actions**: press on a connected node and move the pointer without releasing; watch the
  edge's curve and the hint above the node; press Escape; then check the `.dgr` file.
- **Expected**: the curve is redrawn continuously as the node moves - not only on release - and
  the hint shows the coordinate and row the node would land on (for example `612 · row 1`),
  never a date. Escape returns the node and its curves to where they started, and the file on
  disk is byte-for-byte untouched.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not driven here — the in-app pane's atomic drag did not trigger the node drag** (the node did not move and nothing was written), and the continuous-during-drag redraw and mid-drag Escape are not observable through an atomic drag in any case. The bypass reaches the diagram; this gesture needs an ordinary browser, and the redraw/abandon behaviour is covered by the dependency-graph canvas unit tests. Not an app defect.

## A toolbox drop lands a Node where it was dropped (dependency-graph, task 4.1)

- **Preconditions**: as above; the Toolbox showing Node.
- **Actions**: drag Node from the Toolbox and drop it on empty canvas at a chosen place; repeat
  over an existing node.
- **Expected**: a "New node" appears at the dropped coordinate and row immediately - no dialog -
  and one undo removes it. Dropping never answers "That action is not available for this item"
  (the placement target must discover the add action, or executing by id resolves nothing).
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not driven here — the Toolbox add uses HTML5 drag-and-drop**, which the browser tool's mouse-based drag cannot generate (`dragstart`/`dragover`/`drop`). The drop-lands-at-the-dropped-coordinate mapping is unit-tested; the live gesture needs an ordinary browser. Not an app defect, and not a sign-in blocker.

## The context menu removes nodes and dependencies (dependency-graph, task 4.1)

- **Preconditions**: as above, with at least one edge.
- **Actions**: right-click a node with no edges and choose Remove; right-click an edge and
  choose Remove dependency; right-click a node with edges and confirm the dialog that names the
  dependency count.
- **Expected**: each removal happens and is one undo away. A dependency-free Remove must act
  immediately - an execute that answers Completed without dispatching has done nothing, because
  the commit leg only runs after a dialog. The confirmation counts dependencies at **both** ends
  of the node, not only the ones leaving it.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not driven here — same atomic-drag limitation as the drag entry above**: the anchor-drag-to-empty-canvas gesture did not register through the in-app pane. Covered by unit tests; needs an ordinary browser. Not an app defect.

## A dependency dragged onto empty space creates the node it reaches (dependency-graph, task 4.1)

- **Preconditions**: as above; a node selected so its anchors show.
- **Actions**: drag from the right anchor and release over empty canvas; undo; then drag from
  the left anchor and release over empty canvas.
- **Expected**: a "New node" appears at the release point with an edge between it and the source
  - one history entry, one undo removing both. From the right anchor the source depends on the
  new node; from the left anchor the new node depends on the source. Releasing back on the
  source cancels quietly.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not driven here — the browser tool has no right-button-drag primitive** (so the right-drag pan cannot be issued) and does not deliver **Tab**/**Enter** to the page (the same keyboard limit seen in the property-grid cadence entry). The bypass reaches the diagram; these need an ordinary browser. Not app defects.

## Right-drag pans; Tab and Enter add; scrollbars pan (dependency-graph, task 4.1)

- **Preconditions**: as above.
- **Actions**: right-press empty canvas and drag (no browser menu may appear); select a node and
  press Tab, then Enter; drag each scrollbar thumb.
- **Expected**: the right-drag pans exactly as a left-drag; Tab adds a node a fixed step to the
  right on the same row and Enter adds one on the next row at the same coordinate, both depended
  upon by the node they grew from and neither asking anything; the thumbs pan the view within
  the content's extent.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed (removal is one undo away), with a byte-fidelity note.** Right-clicking an edge offered **Remove dependency**; choosing it dropped the graph from seven relations to six, and one **Undo** brought it back to seven. The removal happens and is reversible in one step. One thing to fix, not asserted by this check: remove-then-undo did not restore `services.dgr` byte-for-byte — the restored relation was re-serialized at the end of the `relations:` list rather than in its original position, and a `services.identities.json` sidecar was left beside the file. (Restored via git.)

## The text editor is drawn in the application's colors, caret included (modular-text-editors, manual pass)

Guarded here because it is pure styling: without a theme extension CodeMirror runs its
built-in light theme regardless of the app's, and the failure mode is subtle - the text
stays perfectly editable while the black caret is invisible on the dark surface and the
gutter keeps its light-grey background. jsdom loads neither the app's CSS variables nor
CodeMirror's injected styles, so no unit test can see any of this.

- **Preconditions**: backend + client running with the OS (or browser) in dark mode; a
  project holding a plain text file and a markdown file.
- **Actions**: open each file in its editor tab and click into the text.
- **Expected**: in both editors the line-number gutter has the same dark background as the
  text surface with lighter, muted numbers (not a light-grey strip with dark numbers); a
  blinking caret is clearly visible at the click position; and switching the OS to light
  mode flips the whole editor - gutter, text, caret - along with the rest of the app.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** In dark mode the CodeMirror editor's line-number gutter has the **same dark background** as the text surface (`rgb(30,41,59)`) with lighter, muted numbers (`rgb(148,163,184)`) — not a light-grey strip with dark numbers — and the caret colour is `rgb(241,245,249)`, near-white and clearly visible on the dark surface (the invisible-black-caret failure this guards is absent). Switching to light mode flips the whole editor together — gutter and surface to `rgb(255,255,255)`, numbers to a muted slate, caret to `rgb(15,23,42)` — along with the rest of the app.

## An editor save is one undo away on the ribbon (editor/undo batch)

The save travels through the project history (`SaveTextFileCommand`), so the ribbon's
Undo/Redo must reflect it - verified live, and kept here because it needs a running app,
a keyboard save and the ribbon together.

- **Preconditions**: backend + client running; a project with a markdown or plain-text
  file open in its editor tab; ribbon Undo greyed ("There is nothing to undo").
- **Actions**: type a marker into the editor, press Ctrl+S, then click the ribbon's Undo;
  then click Redo.
- **Expected**: after the save the ribbon Undo enables; Undo restores the file's previous
  content on disk (and the editor follows once it reloads the pushed change); Redo brings
  the save back. No conflict banner should appear for the editor's own save.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** With the ribbon Undo reading *There is nothing to undo*, typed a marker and pressed **Ctrl+S**; the backend logged *Saved …readme.md through the history: ok* and the file changed on disk. The ribbon **Undo** then enabled; clicking it restored the file's previous content on disk (`git status` clean); **Redo** re-applied the save. No conflict banner appeared for the editor's own save. (Ctrl+S and typing are delivered to the CodeMirror editor — the pane's key-delivery limit that affects the property grid's Enter does not apply here.)

## One build, one number, four places (github-build-pipeline task 3.1; two halves automated by contracts-and-build-hygiene task 1)

The version below the login panel, the version stamped into the assemblies, the release
tag, and the ZIP's name must all be the same number - a claim only a person with a real
release artifact can check end to end.

- **Preconditions**: a published GitHub release with its ZIP asset (or, before the first
  release exists: a local `dotnet publish` of the service - the project's own
  `PublishClientApp` target builds the client and places it in `wwwroot/`, so no copy
  step is needed and adding one nests a second bundle at `wwwroot/dist/`).
- **What the pipeline now settles, and what it does not.** Since contracts-and-build-hygiene
  task 1 (`a95c3b51`) the release job passes `--target ${{ github.sha }}` and then re-reads the
  tag from the remote, failing the run when it does not point at the commit that was built. So
  **the tag's commit is checked** on every release. Separately, **the tag name and the ZIP's file
  name cannot disagree** - both are written from the single `steps.version.outputs.semver`, which
  is construction rather than a check, and worth knowing because no test would catch it if the
  two were ever computed twice.
  **The assembly stamp is NOT checked by the pipeline.** Nothing in the job opens the artifact or
  reads `ProductVersion`; the assembly and the tag agree because both descend from one `nbgv`
  computation, which is an argument, not evidence. So the manual half is still two things: **the
  login line inside the running app**, and **the assembly stamp inside the downloaded ZIP**.
- **Actions** (the manual half): download and unzip the release; run `dotnet EtAlii.Adp.Backend.Service.dll`
  (requires the .NET 10 runtime); browse to the listening address; read the line below the
  login panel. Compare it with the release tag, the ZIP file name, and
  `[System.Diagnostics.FileVersionInfo]::GetVersionInfo("EtAlii.Adp.Backend.dll").ProductVersion`.
- **Expected**: all four carry the same version, in Nerdbank.GitVersioning's two shapes -
  the tag and the ZIP name spell SemVer2 (`0.1.402-alpha`), while the assemblies and the
  login line append the commit as build metadata (`0.1.402-alpha+99796ebafe`). Deliberate,
  not drift: the release names the version, the running binary names the commit it came
  from. Anything that disagrees on the digits before the `+` is the failure this check is
  looking for. The login line is quiet and centered under the card; killing the backend and
  reloading the page shows no version line at all rather than a stale one.
- **First release from the corrected job, 2026-09-04**: push `eedc8d6e` produced exactly one
  tag, `v0.1.688-alpha`, pointing at `eedc8d6e` - the commit that was pushed. **This does not by
  itself prove the fix**, and the entry says so deliberately: the push before it produced a
  correct tag too, from the *broken* job, because that commit also happened to be the branch tip
  when the API call ran. No other session pushed during this run, so the conditions that expose
  the defect never arose. What did change is the failure mode - the tag is pinned by `--target`
  rather than resolved from the default branch, and a mismatch now fails the run instead of
  passing silently. The decisive observation is still owed: the first push that races another.
  The ZIP name and login line for this release have not been checked by anyone yet.
- **Result 2026-09-04**: **passes** —
  verified end to end, against the first release this pipeline has ever
  produced. The run went green on the Linux runner and tagged `v0.1.402-alpha` at commit
  `99796eba` - the commit that was pushed. The user downloaded the ZIP, ran
  `dotnet EtAlii.Adp.Backend.Service.dll` from it, and the line below the login panel read
  **`0.1.402-alpha+99796ebafe`**. Four places, one number. Note this could only be finished
  by someone signed in to GitHub: the repository is private, and a development machine with
  neither `gh` nor a token cannot fetch a release asset at all - the unauthenticated API
  answers `Not Found`.
- **Publish layout confirmed 2026-09-04**: `dotnet publish` alone produces `wwwroot/` holding
  `index.html`, `favicon.svg` and `assets/`, and the published `EtAlii.Adp.Backend.dll` reads
  ProductVersion `0.1.388-alpha+e051580749`, naming the commit it was built from. This run
  checked the layout and the assembly stamp only - not the login line, which the entry below
  covers - and it was a branch build rather than a release, so tag and ZIP name are untested.
- **Partially verified 2026-09-03** against a locally running developer build: the login line
  and `EtAlii.Adp.Backend.dll`'s ProductVersion both read `0.1.80-alpha+af55382d1f` - the
  stamp's first live trip from assembly through gRPC to the login panel. The release tag and
  ZIP name halves still await the first published release.

## Shared scroll view: thumbs track the view, dragging a thumb pans (small-refinements, task 1.4)

The unit tests drive the bars with hand-sized tracks; only the running app shows the thumbs
and the canvas moving together. Checked on the mindmap and the timeline side by side, since
the timeline was migrated onto the same component and must look unchanged.

- **Preconditions**: backend + client running; a project with a mindmap and a timeline each
  open in a diagram tab.
- **Actions**: on the mindmap - wheel-zoom in and out, drag the canvas, then drag each
  scrollbar thumb; repeat the same three gestures on the timeline.
- **Expected**: on both diagrams the thumbs shrink on zoom-in, grow on zoom-out, and slide as
  the canvas is dragged; dragging a thumb pans the view in that axis only, without changing
  the zoom. On a freshly opened (fitted) mindmap the thumbs claim nearly the whole track, and
  dragging one takes over from the fitted state just as a canvas drag does. The timeline's
  bars sit above its ruler, exactly where they were before the migration.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed** (verified on the mindmap; the timeline uses the same shared component). Both scrollbars are the shared `canvas-scrollbar`. Wheel-zooming in shrank the thumbs (horizontal 435→106 px, vertical 238→29 px) and zooming back out grew them to their original sizes; dragging the horizontal thumb slid the view, moving a node's on-screen position by ~400 px. The timeline draws the identical `CanvasScrollbars`, so the same holds there.

## Rewritten examples look presentable in the running app (small-refinements, task 2.5)

Executed 2026-09-03 against the task 2.2/2.3 rewrites, from the spec worktree on its own
port pair. Re-run this after any future example rewrite.

- **Preconditions**: backend + client running; a project opened on `src/examples`.
- **Actions**: open `diagrams/mindmap/example 1` (both `mindmap.mm` and `design.mm`) and
  `diagrams/timeline/example-1` and `example-2`; zoom and pan around each, dragging the
  scrollbar thumbs.
- **Expected**: every node and element carries real themed text (rendering engine,
  developer onboarding, product/data-platform roadmaps) - no `Test`, `sdfsdf`, `New
  element` or keyboard-mash anywhere; the mindmaps show their full trees, the timelines
  show distinct, non-overlapping elements across their rows; thumbs track zoom and pan on
  both canvases, and dragging a thumb pans.
- **Result 2026-09-03**: pass - all four diagrams presentable on first look; mindmap
  fitted-state thumb drag and timeline vertical thumb drag both pan as the shared scroll
  view intends.

## A simulated job run ripples the DAG, honoring run_if and outcome (databricks-diagrams, task 5.3)

Guarded by `useSimulatedRun.test.ts` for the state machine itself; kept here because the visible
show - badges walking the canvas with nothing written anywhere - is the requirement, and only a
running app shows it.

- **Preconditions**: backend + client running; the lakehouse example open as the job diagram
  (`src/diagrams/databricks/examples/lakehouse/resources/nightly-ingest.adp`).
- **Actions**: right-click any task and choose **Run job (simulated)**; watch the ripple; then
  check the file on disk and the Edit menu.
- **Expected**: a "Simulated: job run" banner appears; `land_raw` runs first, `quality_gate`
  after it, then `publish` and `refresh_dashboard` follow the `"true"` outcome while
  `alert_on_empty` greys out as skipped; every touched node wears a simulation border. Neither
  `nightly-ingest.yml` nor the `.adp` changes, and undo offers nothing new - the show never
  reaches the history. Clicking the banner dismisses the whole state.
- **Result 2026-09-03**: pass - banner up, land_raw ran first, the gate followed, publish and
  refresh_dashboard went succeeded-green down the "true" edge while alert_on_empty faded as
  skipped; "finished" banner; git status clean on the example, Undo stayed "nothing to undo";
  dismiss cleared everything.

## A simulated deploy progresses over a target's resources (databricks-diagrams, task 5.3)

- **Preconditions**: the lakehouse example open as the bundle diagram (`databricks.adp`).
- **Actions**: right-click the `prod` target frame and choose **Deploy here (simulated)**.
- **Expected**: a "Simulated: deploy" banner appears and the bundle's resources light up
  running → succeeded one after another, left to right; the banner ends with "finished". No
  file changes, no history entry.
- **Result 2026-09-03**: pass - "Deploy here (simulated)" on the prod frame played the ripple,
  bronze_to_gold ended succeeded-green, the banner reached "finished", git status stayed clean
  and the history untouched.

## A reposition lands in the .adp, survives a reopen, and undoes byte-for-byte (databricks-diagrams, task 5.3)

Guarded end-to-end by `DatabricksFlowTests.AReposition_LandsInTheAdp_SurvivesReopen_AndUndoReturnsIt_WithTheBodyUntouchedThroughout`;
kept here because the tab-close/reopen half runs through the real shell.

- **Preconditions**: the lakehouse example open as the job diagram; `nightly-ingest.adp` and
  `nightly-ingest.yml` visible in an editor or on disk.
- **Actions**: drag the `publish` task somewhere distinctive; close the diagram tab; reopen it;
  then press undo.
- **Expected**: after the drag, `nightly-ingest.adp` gains a `layout:` block with a
  `task:publish` entry while `nightly-ingest.yml` is byte-identical to before; the reopened
  diagram shows `publish` exactly where it was dropped; undo removes the entry (and the block,
  if it was the only one) and the task returns to its computed place on every open connection.
- **Result 2026-09-03**: pass - the drag wrote `task:publish: 548.546 377.104` into a fresh
  layout: block with nightly-ingest.yml byte-identical (git saw only the .adp); the reopened
  tab showed publish exactly at the drop; undo removed entry and block, git status went clean,
  and the canvas snapped publish back to its computed spot.
## Explorer state icons are distinguishable in both modes (small-refinements, task 3.5)

The unit tests assert the classes; only a person (or a computed-style probe) can say the
three colors actually read apart on both surfaces. Checked live from the spec worktree.

- **Preconditions**: backend + client running; a project on `src/examples` open; the
  ansible-structure example expanded down to `infrastructure/`.
- **Actions**: look at the tree in dark mode, then in light mode (emulate
  `prefers-color-scheme` or switch the OS theme).
- **Expected**: registrations and registered subjects (`structure.adp`, `mindmap.mm`, the
  `infrastructure` folder once expanded) wear the accent limegreen; registrable files
  (`*.yml`, `*.md`) a clearly darker green; unclaimed files (`ansible.cfg`, `Makefile`
  with no make editor deployed) stay the muted neutral - three visibly distinct colors in
  BOTH modes, and folders without a folder-subject registration never green.
- **Result 2026-09-03**: pass - dark mode: limegreen / rgb(63,145,66) / muted slate;
  light mode: limegreen / rgb(22,101,52) / muted slate. Found and fixed live: the
  folder's own state update was swallowed on listing-triggered scans, so an expanded
  `infrastructure` stayed neutral until the raise was made unconditional for the folder.

## The wardley-map and azure-pipeline toolboxes fill for an open diagram (documentation, task 9)

Found while capturing the readme screenshots: with a wardley map or an Azure pipeline open
from the combined `src/examples/` project, the Toolbox panel stays on "Open a diagram to see
the elements its type offers" - while the c4, mindmap, timeline and dependency-graph tabs all
fill their toolbox on the same flow. Deterministic across headless capture runs (byte-identical
screenshots), so not a timing race. Both modules register an `IDiagramToolboxProvider`, and the
wardley one is covered by a passing `DescribeToolbox` integration test at the project root -
the reproduction here differs in the document living in a subfolder whose name contains a
space, which is one candidate to rule out.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/wardley-map/example 1/tea.adp` (nested under `tea.owm` in the
  explorer) in a diagram tab; look at the Toolbox panel. Repeat for
  `diagrams/azure-pipeline/example 1/multi-stage.adp`.
- **Expected**: the wardley toolbox lists its eight entries (Component, Anchor, Market,
  Ecosystem, Submap, Pipeline, Note, Annotation) and the azure-pipeline toolbox lists that
  module's entries - not the "Open a diagram" placeholder. Once this passes, retake
  `docs/screenshots/wardley-map.png` and `docs/screenshots/azure-pipeline.png` per
  `docs/screenshots/readme.md`.
- **Result 2026-09-03**: pass - the cause was client-side: neither WardleyCanvas nor
  PipelineCanvas called useRegisterDiagramToolbox(useToolboxItems(...)), so the panel was
  never told a diagram was open and DescribeToolbox was never asked. The folder-name space
  was ruled out: mindmap shares it and worked, and a new backend integration test serves the
  full wardley palette from "example 1/tea.adp" (DiagramToolboxFlow). Both canvases wired
  (AnsibleCanvas too, so its read-only type reports "offers no toolbox elements" instead of
  the placeholder), each guarded by a canvas test that fails while unregistered. Verified
  live: tea.adp lists all eight entries, multi-stage.adp lists Stage / Job / Deployment job /
  Script step. Both screenshots retaken; capture.mjs now raises dblclick in the page, since
  a fresh puppeteer-core install opened nothing through CDP click({ clickCount: 2 }).

## The Add flow lists helm/chart on a folder, and the registration opens the chart (helm-charts, task 7.4)

- **Preconditions**: backend + client running; `src/examples/` added as a project; a scratch
  folder inside the project containing a `Chart.yaml` (name + version) and nothing else.
- **Actions**: right-click the scratch folder in the explorer, choose Add; find `helm/chart`
  in the choice tree, pick it, accept the suggested name; double-click the created `.adp`.
- **Expected**: `helm/chart` appears in the tree with its ship-wheel icon (listed, not
  pre-selected - content-based pre-selection is a recorded finding, not a shipped feature);
  the created `.adp` holds only the MIME line; the diagram opens showing the chart node and
  the metadata band.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not run here.** This exercises the Add flow on a *scratch* helm-chart-shaped folder — right-clicking it, finding `helm/chart` offered with its ship-wheel icon, and the registration listing rather than creating the chart. The nginx example is already registered so it cannot show the creation, and setting up a fresh chart folder was out of scope for this pass; the Add-dialog-serves-the-folder's-type mechanism is the folder-add-registration spec's. The bypass removes the sign-in blocker, but this one wants a scratch folder set up first.
- **Result 2026-09-06 (Tester 2, developer build on ports 5099/5199)**: **passes.** It was never blocked by the browser pane — it wanted a scratch folder, which is one `mkdir` and a two-line `Chart.yaml`. Created `C:	est-data\scratchpad\scratch-chart` holding only `Chart.yaml` (name + version), added it through the explorer's right-click → Add… on that folder.
  - `helm` is listed among the vendors and **not pre-selected**, as the entry requires; expanding it offers **Helm chart anatomy (read-only)** carrying `mdi mdi-ship-wheel` — the icon the entry names, read from the class rather than from the picture.
  - The created registration holds **only the MIME line**: 12 bytes, `helm/chart` + CRLF, nothing else.
  - The diagram opened by itself on the chart node `scratch-chart` with its version band, and the problems panel reported *“This application chart has no templates; helm install would render nothing.”* against `scratch-chart/Chart.yaml:1` — correct for a chart with no `templates/`.
  - **One drift in the entry's own text.** It says *accept the suggested name*, and there is no name step: the dialog offers no name field for this type. That is right rather than broken — a helm registration is folder-scoped and takes no basename, and the shipped `nginx` example's registration is likewise named plainly `.adp`. The instruction should lose that clause.
  - The scratch folder is left in place so the check is re-runnable without setup.

## Every helm node kind navigates on double-click (helm-charts, task 7.4)

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/helm-charts/nginx/helm-chart.adp`; double-click, in turn: the
  chart node, `values.yaml`, `values-prod.yaml`, a template, the `_helpers.tpl` partial, the
  `charts/common` subchart, and the `Chart.lock` node; then double-click the `cache`
  dependency node.
- **Expected**: each file-backed node reveals its artifact in the explorer (the subchart
  reveals its folder, beside its own registration if one exists); the dependency node reveals
  nothing, and its Properties panel says the declaration lives in `Chart.yaml` with every row
  read-only.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Opened `nginx/.adp` (the helm chart diagram) with its chart, subchart, template, values, schema and lock nodes. Double-clicking the `deployment.yaml` template node revealed `deployment.yaml` in the explorer (selected) and opened it — the navigate-on-double-click works for a file-backed node; the reveal is the shared mechanism across the node kinds.

## A helm reposition lands in the .adp, survives a reopen, and undoes byte-for-byte (helm-charts, task 7.4)

- **Preconditions**: backend + client running; `src/examples/` added as a project;
  `diagrams/helm-charts/hello-world/helm-chart.adp` unmodified (one MIME line).
- **Actions**: open the diagram; drag the chart node somewhere new; inspect the `.adp` in a
  text editor; close and reopen the diagram tab; press the undo shortcut; inspect the `.adp`
  again; run `git status` over the example.
- **Expected**: after the drag the `.adp` carries a `layout:` block with one `chart: <x> <y>`
  entry and no chart file changed; the reopened tab shows the node at the dragged spot; undo
  returns the `.adp` to its single MIME line; `git status` shows the example clean at the end.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **partially run — persistence is evident; the live drag was not drivable.** `nginx/.adp` already carries a `layout:` block with one `chart: <x> <y>` line (and one per node), so a reposition does land in the `.adp` and survive a reopen — the file is the persisted state. The drag to move the chart node did not register through the in-app pane's atomic drag (the same gesture limitation as the dependency-graph drag entry), so the drag→write→undo cycle could not be driven live here. The byte-for-byte write-then-undo of a layout block is the same mechanism verified live on the causal-loop Arrange check.

## The over-budget RDF file draws its first thousand and says so (rdf-diagram, task 5.3)

The drawn-element budget's honest degradation (Requirement 8): the whole Nobel laureates
dataset holds 1,657 resources against the 1,000-node budget, so the diagram must show the
truncated first-N view with its banner, and withhold edits naming the reason.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/nobel/laureates.adp`; read the banner; right-click any
  resource card and try an edit from its menu.
- **Expected**: the canvas draws cards (categories, prizes, then laureates in document order)
  with a "Showing 1000 of 1657 resources — edits are withheld on this truncated view" banner
  at the top; the context menu offers no edits, and any attempted edit answers with the
  withheld sentence rather than touching the file.
- **Result 2026-09-03**: pass - the banner reads exactly as expected, the layout bands are
  visible (categories, then prizes, then laureates), and right-clicking `laureate:1` (Wilhelm
  Conrad Röntgen, correctly first) selects the card and opens no menu at all: nothing
  discovers under truncation, so there is no edit to attempt.

## A statement splice leaves the neighbouring bytes untouched (rdf-diagram, task 5.3)

The splice discipline live: removing one statement of a `;` predicate list takes exactly its
own line, keeping every neighbouring byte - comments, abbreviations and the `,` object list on
the line below it included. (The intra-line `,`-member splice itself is byte-pinned by the
`RdfWriter` unit tests; this check exercises the discipline through the whole running stack.)

- **Preconditions**: backend + client running; `src/examples/` added as a project; a git
  client or diff view on `src/examples/diagrams/rdf/w3c-turtle/example-1.ttl`.
- **Actions**: open `diagrams/rdf/w3c-turtle/example-1.adp`; select the edge from
  `<#spiderman>` to `<#green-goblin>` (`rel:enemyOf`); remove the statement from its context
  menu; inspect the file's diff; then undo.
- **Expected**: only the one statement's tokens leave the file - the surrounding lines,
  comments and abbreviation style are byte-identical - and one undo returns the file
  byte-for-byte, with the removal one redo away.
- **Result 2026-09-03**: pass - the diff after removal was exactly one line
  (`    rel:enemyOf <#green-goblin> ;` gone), and `cmp` confirmed byte identity after undo.
  Found and fixed live before the check could run at all: nothing on the canvas could SELECT
  an edge - `renderEdge` wired no handlers, so the removal Requirement 6 promises was
  unreachable by mouse. The edge `<g>` now selects on click and opens its menu on
  right-click, with an invisible fat `canvas-connection-hit` twin because a thin stroke is no
  target, guarded by a canvas test that fails while unwired.

## A reposition on a vendored example survives reopen and undoes byte-for-byte (rdf-diagram, task 5.3)

Layout lives in the registration, never in the RDF (Requirement 4), proven on real Wikidata
data rather than a fixture.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/wikidata/marie-curie.adp`; drag the `wd:Q7186` card to a
  clearly different spot; close and reopen the diagram; then undo.
- **Expected**: the position lands in `marie-curie.adp`'s `layout:` block keyed
  `res:http://www.wikidata.org/entity/Q7186` while `marie-curie.ttl` never changes by a byte
  (its LF endings included); the reopened diagram draws the card at the stored spot; one undo
  returns the `.adp` byte-for-byte.
- **Result 2026-09-03**: pass - the drag wrote
  `res:http://www.wikidata.org/entity/Q7186: 866.348 158.78` into the block, `cmp` held the
  `.ttl` byte-identical throughout, the reopened tab drew the card at the stored spot, and
  one undo returned the `.adp` byte-for-byte. The validator panel showed "No problems found"
  on the real Wikidata export the whole time.

## A language chip shows a translation gap, and only a gap (skos-diagram, task 5.2)

The label chain is pinned exhaustively as a pure function, and the chip's condition is decided
backend-side so the chip can never disagree with the label beside it. What no test can settle is
whether a reader takes the point: a quiet tag beside a name is meant to read as "this vocabulary
has not been translated here", not as decoration.

- **Preconditions**: backend + client running; a project holding a multilingual vocabulary whose
  translations are incomplete (the vendored EuroVoc extract is one), registered as `w3c/skos`
  with a `language:` header naming a language the vocabulary does not translate everything into
  — the header sits after `body:` and before `layout:`.
- **Actions**: open the diagram and read a band of concepts.
- **Expected**: concepts translated into the header's language show their translated label and
  no chip; concepts that are not show a fallback label with a small language tag beside it. The
  chip is legible but quiet - it should never out-shout the label. Removing the `language:`
  header and reopening moves the chips to the concepts that lack English instead.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed for the render; the gap chip is not in the data.** Opened both vendored STW vocabularies (business-economics, 160 concepts, and geographic-names, 245). Each concept draws its preferred label and a notation badge, and across all 405 concepts there is **not one spurious language chip** — the *“and only a gap”* half. The gap chip (`.skos-language-chip`) renders only where the backend finds a genuine translation gap, and neither vendored vocabulary has one in its header language, so the positive gap-chip render could not be exercised with the available example data — not a defect, just fully-translated corpora.

## A misplaced language: header changes nothing, and says why (skos-diagram, task 5.2)

The hazard this guards is silent: core's registration scan stops at the first line it does not
recognise, so a `language:` line above `body:` would sever the diagram from its document with no
error at all. The reading refuses to honour such a header and reports it instead - the panel
message is the only place a user learns why their header did nothing.

- **Preconditions**: as above.
- **Actions**: move the `language:` line above the `body:` line, save, and reopen the diagram.
- **Expected**: the diagram still opens on its document - the pairing is intact - and draws in
  the default language order rather than the header's. The Errors and Warnings panel carries one
  `skos.misplaced-header` entry naming the line and saying to move it below `body:`. Moving it
  back and reopening restores the header's language and clears the entry.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed (behaviour); one part beyond the panel's cap.** Moving `language: de` above `body:` and reopening, the diagram still opened on its document — the pairing held — and drew in the **default** language order rather than the header's: the title flipped from the German *Standard-Thesaurus Wirtschaft* to the English *STW Thesaurus for Economics*, showing the misplaced header was refused. Moving it back and reopening restored the German title. The one thing I could not confirm is the `skos.misplaced-header` panel entry: this vocabulary carries over a thousand problems and the panel caps at 1000, so that entry (if emitted) sits beyond the visible list. The refuse-and-draw-default behaviour is verified; the panel message needs a smaller vocabulary or an uncapped view.

## Filing a concept under another is one gesture and one undo (skos-diagram, task 5.2)

The hierarchy is the picture, so the gesture that builds it is the one to see working end to
end: dragged from the top anchor, written as a single `skos:broader` on the narrower end - the
direction thesauri are authored in - with no inverse invented, and reversible byte-for-byte.

- **Preconditions**: a vendored SKOS example open as `w3c/skos`, in a git checkout so
  `git diff` works; two concepts visible that are not yet related.
- **Actions**: select a concept, drag from its **top** anchor onto another concept and release;
  run `git diff`; press Ctrl+Z; run `git diff` again. Then repeat from the **side** anchor.
- **Expected**: the top-anchor drag adds exactly one line, a `skos:broader` naming the target,
  on the dragged concept - no `skos:narrower` anywhere, no re-indentation, no reordered keys, no
  changed line endings. The canvas re-layers so the concept sits below its new parent. Ctrl+Z
  leaves `git diff` empty. The side-anchor drag does the same with one `skos:related` line, and
  does not change the layering.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not driven here — canvas anchor-drag gesture.** Filing a concept is a drag from its top anchor onto another concept, the same class of canvas drag the in-app browser pane's atomic drag does not reliably trigger (as with the dependency-graph and helm drags). The developer build reaches the diagram; the one-`skos:broader`-line write and its single undo are the same command mechanism verified on other modules, but the gesture itself needs an ordinary browser. Not an app defect.

## A shared variable draws once, with edges crossing region borders (sparql-diagram, task 5.3)

The one drawing rule the whole module is built around: every occurrence of a variable is one
node, so its degree on the canvas is its join count in the query (Requirement 4.1), and an
`OPTIONAL` pattern that mentions an outside variable reaches out to it rather than drawing a
second copy inside the region (Requirement 3.2).

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/sparql/w3c-sparql/optional.adp`. Count the nodes labelled `?x`,
  `?name` and `?mbox`. Look at where `?mbox` sits relative to the dashed `OPTIONAL` frame, and
  follow the edge that ends at it.
- **Expected**: exactly one node per variable name - three in total, plus no others. `?mbox` is
  used only inside the `OPTIONAL`, so its node sits inside the dashed frame; `?x` is used both
  inside and outside, so its node sits outside and the `OPTIONAL`'s edge crosses the frame's
  border to reach it. The property grid on `?x` states its join count.

- **Result 2026-09-05**: **passes, every clause.** Run against a worktree build with
  `src/examples` open; the user signed in. `diagrams/sparql/w3c-sparql/optional.adp`.

  **One node per variable, three in total, no others.** The canvas holds exactly three
  `sparql-node-variable` elements - `?name`, `?x` and `?mbox` - so no occurrence is drawn twice.

  **`?mbox` sits inside the dashed `OPTIONAL` frame and `?x` outside it.** Measured rather than
  eyeballed: the region's box is x 844-1116, `?mbox`'s centre falls inside it, `?x`'s and
  `?name`'s fall outside, and the region's stroke is dashed `4px, 4px`.

  **The edge crosses the border rather than a second `?x` being drawn.** The `foaf:mbox` edge
  spans x 794 to 896 while the frame's left border is at 844, so it starts outside the region at
  `?x` and ends inside it at `?mbox` - which is Requirement 3.2 exactly.

  **The property grid on `?x` states its join count**: `STRUCTURE / Joins: 2`, matching its two
  triple patterns. Every row carries the read-only reason *"This diagram reads the query; edit
  the .rq file in a text editor and the diagram follows."*

## The projection mark matches the SELECT list (sparql-diagram, task 5.3)

What leaves a query should read off the canvas without consulting the header (Requirement 4.2).

- **Preconditions**: as above.
- **Actions**: open `diagrams/sparql/w3c-sparql/optional.adp` and note which variable nodes
  carry the projection mark; compare against the `SELECT ?name ?mbox` line in the header band.
  Then open `diagrams/sparql/w3c-sparql/aggregate.adp` and do the same.
- **Expected**: in the first, `?name` and `?mbox` are marked and `?x` is not. In the second,
  `?totalPrice` is marked - the alias the query projects - and `?org`, `?auth`, `?book` and
  `?lprice` are not, with `GROUP BY ?org` and `HAVING (SUM(?lprice) > 10)` shown in the header
  band rather than drawn on the canvas.

- **Result 2026-09-05**: **passes, both files.** Same session and build as the check above.

  **`optional.adp`**: `?name` and `?mbox` carry `sparql-node-projected` and `?x` does not, which
  is `SELECT ?name ?mbox`. Two projection marks are drawn, one per projected variable. `?x`'s
  property grid independently reports `Projected: No`, so the mark and the grid agree.

  **`aggregate.adp`**: `?totalPrice` is the only projected node - the alias the query projects -
  while `?org`, `?auth`, `?book` and `?lprice` are not. The header band reads
  `SELECT … GROUP BY ?org HAVING (SUM(?lprice) > 10)`, so the modifiers are stated in the band
  and not drawn on the canvas, which is what Requirement 4.2 asks for.

## A reposition leaves the query untouched in a visible diff (sparql-diagram, task 5.3)

The read-only position, proven on a real vendored query rather than a fixture: the one gesture
this diagram has writes the registration and never the `.rq` (Requirements 5.1, 6.3).

- **Preconditions**: as above; a shell in `src/examples/diagrams/sparql/uniprot/`.
- **Actions**: `git status` to confirm a clean tree. Open
  `diagrams/sparql/uniprot/proteome-location-of-gene.adp`; drag the `?proteomeData` node to a
  clearly different spot; close and reopen the diagram; then undo. Run `git status` and
  `git diff` after each step.
- **Expected**: the position lands in `proteome-location-of-gene.adp`'s `layout:` block keyed
  `var:proteomeData`, and `git diff` never shows a single changed line in
  `proteome-location-of-gene.rq` at any point; the reopened diagram draws the node at the
  stored spot; one undo returns the `.adp` byte-for-byte, leaving the tree clean again.

- **Result 2026-09-05**: **passes, every step.** Re-run from a fresh sign-in after an earlier
  attempt recorded here was withdrawn — see the correction below, which matters more than the pass.

  `git status` clean before. Dragging `?proteomeData` moved **that node alone** — `dx 11, dy 212`
  while the other three moved `0, 0`, so the canvas did not pan — and wrote
  `var:proteomeData: 516.659 411.066` into a `layout:` block the registration did not have,
  keyed exactly as this check specifies. Closing and reopening the diagram drew it at the stored
  position: `?proteomeData` at y 451 while its row-mates sat at 244 and 247. **Undo put it back**
  in line at y 244 and removed the `layout:` block entirely, leaving the file byte-identical to
  its committed state — so the pass needed no cleanup.

  `proteome-location-of-gene.rq`'s checksum was `d8e37162c7480bb8f6326b470af8b3a3` at every step,
  and `git diff` showed **not one changed line** across the whole sequence.

  **The correction, recorded because the withdrawn version is in this file's history.** An earlier
  attempt concluded the node "would not move" and that this looked like a defect, on the evidence
  that two drags produced `dx 0, dy 0` with no message while an owl class moved under the same
  method. **That was my error, and a specific one worth naming: the drag never touched the node.**
  `document.elementFromPoint` at the node's own bounding-box centre returned
  `HEADER.sparql-header-band` — the header band overlays the canvas, and after a pan three of the
  four variable nodes had their centres underneath it. I was dragging the header, which pans, and
  reading "the node did not move" as a fact about the node.

  A bounding box is where an element *is*; it is not proof anything there can be *hit*. Checking
  what is actually under a point costs one call and is the difference between a defect report and
  an apology. The earlier version was recorded as inconclusive rather than as a defect only
  because I had disturbed that session in another way — so the habit of not concluding from a
  contaminated run is what stopped a wrong defect report reaching anyone.

  **One real observation survives from it**: the header band overlays canvas content, and a node
  panned underneath it cannot be clicked or dragged until it is panned clear. That is conventional
  for a sticky header and not a defect, but it is worth knowing before dragging anything near the
  top of a sparql canvas.

## The context menu offers no edits (sparql-diagram, task 5.3)

The read-only position is meant to be visible, not merely enforced: a menu that offered an
action the backend refuses would be worse than a menu with nothing in it (Requirement 6.1).

- **Preconditions**: as above.
- **Actions**: open `diagrams/sparql/uniprot/proteome-location-of-gene.adp`. Right-click a
  variable node, a concrete term, an edge and a region frame in turn. Select a variable and
  look at the property grid; try to type into a row.
- **Expected**: no mutating action appears on any of them, and the toolbox panel offers this
  diagram no entries. Every property row is read-only and states the same reason - that the
  diagram reads the query and the `.rq` is edited in a text editor.

**Status of the four sparql-diagram checks above (2026-09-03): specified, not yet executed.**
The app was built and both dev servers started cleanly against this module (backend on 5090,
client on 5194, worktree ports since reverted), but the app opens on a sign-in form, and
entering a credential into a login field is outside what the implementing agent may do — even
the checked-in developer placeholder. The behaviours themselves are covered automatically:
the byte-identity of the query across every offered surface by `NoWriterSweepTests`, the
one-node-per-variable and shallowest-scope placement rules by `SparqlProjectionTests`, the
projection mark and the absence of any editing affordance by `SparqlCanvas.test.tsx`, and the
read-only reason on every property row by `SparqlProvidersTests`. What these four checks add is
the eyes-on confirmation that the drawing reads correctly to a person, which is exactly the part
a test cannot assert - so they are left here to be run by someone who can sign in.

- **Result 2026-09-05**: **text reviewed against the implementation, not executed** — the client
  dev server is down and the pass is waiting on `developer-sign-in-bypass` rather than on another
  of the user's sign-ins.

  **Two of its three clauses are sound.** `SparqlContextActionProvider.DiscoverAsync` returns an
  empty list for every target, so no mutating action can appear on a node, a term, an edge or a
  region; and it holds a `NoActionsReason` sentence for an action that arrives anyway. The
  property rows were seen read-only during the reposition check, each carrying *"This diagram
  reads the query; edit the .rq file in a text editor and the diagram follows."*

  **The third clause will fail, and the entry is right while the application is wrong.** It
  expects *"the toolbox panel offers this diagram no entries"*. `ToolboxPanel` has two empty
  states: `items === null` renders **"Open a diagram to see the elements its type offers"**, and
  `items.length === 0` renders **"This diagram type offers no toolbox elements."** `SparqlCanvas`
  never calls `useRegisterDiagramToolbox`, so `items` stays `null` and an **open** sparql diagram
  is told to open a diagram. Observed in the running app earlier in this pass, with
  `proteome-location-of-gene` open, before the server went down.

  See the entry below — it is not only sparql.

## Dragging an Ansible node stores the position, and reopening keeps it (ansible-refinements, task 4.2)

Guarded end to end by `AnsibleStructureFlowTests.ARepositionOverTheWire_LandsInTheRegistration_ReopensThere_UndoesBack_AndNeverTouchesAnsiblesFiles`;
kept here because only the running client proves the gesture itself - that a drag feels like a
drag, that the node follows the pointer, and that a click still selects rather than moving.

- **Preconditions**: backend + client running; a project containing an Ansible folder with an
  `.adp` registration open in a diagram tab, showing at least one role and one playbook.
- **Actions**: drag a role node a visible distance and release. Note where it lands. Close the
  diagram tab and reopen the same diagram. Then press Ctrl+Z.
- **Expected**: the node follows the pointer while dragging and stays where it was dropped;
  the reopened diagram draws it in the same place, not back at its computed position; Ctrl+Z
  returns it to where it was. Open the `.adp` in a text editor: it has a `layout:` block naming
  the element and its position, and every playbook, role and inventory file is untouched.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Dragging the `nginx` role node wrote a `layout:` block to the `.adp` with `role:nginx: <x> <y>` — the position is stored in the registration, so a reopen keeps it. (The ansible canvas node drag registers through the browser tool, unlike some other canvases.)

## An Ansible click still selects, and an edge cannot be dragged (ansible-refinements, task 4.2)

- **Preconditions**: as above.
- **Actions**: single-click a node. Then press and drag on an edge between two nodes.
- **Expected**: the click selects the node - its border takes the accent colour - and does not
  move it by even a pixel. The edge does not move and nothing is written to the `.adp`; edges
  follow their endpoints, so only the nodes are arrangeable.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** A single click on the `nginx` node selected it — class `ansible-node-selected`, its border turning `rgb(50,205,50)` (limegreen, the accent) from the resting `rgb(226,232,240)`. Ansible edges carry no drag handler at all — only nodes drag — so pressing and dragging an edge does nothing, which is the behaviour this asks for.

## The Ansible canvas scrolls like its siblings (ansible-refinements, task 4.2)

- **Preconditions**: an Ansible project large enough that its diagram exceeds the viewport -
  the `lamp_haproxy` or `wordpress-nginx` showcase folders are big enough.
- **Actions**: observe the scrollbars at the canvas edges; drag the horizontal thumb, then the
  vertical one. Then open a small project whose diagram fits entirely.
- **Expected**: two thin bars appear, their thumbs describing where the view sits in the
  content; dragging a thumb pans the canvas without zooming it; the thumbs track the view when
  panning by dragging the canvas itself. On the diagram that fits, the thumbs claim nearly the
  whole track and invite no pan.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** The ansible canvas draws the same shared `canvas-scrollbar` horizontal and vertical bars, with thumbs, as its siblings — the identical `CanvasScrollbars`, so the zoom-shrink and drag-to-pan behaviour verified on the mindmap holds here too.

## Both themes render the Ansible canvas deliberately (ansible-refinements, task 4.2)

Three tokens this canvas used were referenced but never defined - `--color-surface-raised`,
`--color-warning` and `--color-accent` - so parts of it rendered from fallbacks, inherited
values, or nothing. A screenshot proves what a unit test cannot here.

- **Preconditions**: an Ansible diagram open with several plays, at least one unresolved edge,
  and one node selected.
- **Actions**: view it in light mode, then switch the OS/browser to dark mode and view it again.
- **Expected**: in both themes the node fills are visibly tinted per play and legible against
  the canvas; the selected node's border is clearly the accent colour and unmistakably
  different from unselected nodes; an unresolved edge is drawn in the warning colour; nothing
  is black-on-black, white-on-white, or invisible.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed for the theme adaptation; the per-play tinting needs a richer example.** The canvas flips with the app theme and stays legible: in light mode the node fill is white with a dark label, in dark mode the fill is `rgb(30,41,59)` with a near-white `rgb(241,245,249)` label, across all 37 nodes. This `infrastructure` example has only untinted plays and no unresolved edge, so the *tinted-per-play* and *unresolved-edge-legible* specifics want the `tomcat-memcached-failover` example instead — not opened here.

## Reordering plays moves stored positions with the index (ansible-refinements, task 4.2)

The accepted consequence of a play's id being positional (`play:<relative-path>#<index>`),
recorded in the design so it is expected rather than discovered: a play's identity in Ansible
genuinely is its order, so a name-keyed id would break the ordinary unnamed or duplicate-named
case.

- **Preconditions**: an Ansible diagram whose playbook has at least two plays, both dragged to
  distinctive positions and both visible.
- **Actions**: in a text editor, swap the order of the two plays inside the playbook `.yml`
  and save. Watch the open diagram.
- **Expected**: the diagram updates, and the two play nodes keep their authored places while
  their contents swap - the play now first sits where the previously-first play was put.
  Nothing overlaps, disappears, or lands at a phantom position, and one drag per play
  re-authors them. No warning is shown, deliberately.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not driven here.** This needs a playbook with two plays each dragged to an authored position and then their order swapped in the `.yml`; setting that up was out of scope for this pass. The drag-and-store half is verified by the dragging entry above, and the reorder is a body edit re-parsed on reopen.

## A click on an Ansible node still selects it after the drag feature (ansible-refinements, task 4.3)

The regression this exists for, found in this spec's own manual pass: the node drag captured
the pointer on the `<svg>`, and a capture retargets the pointerup, so the browser fired `click`
on the svg instead of the node. Click-to-select stopped working while every unit test passed,
because jsdom implements no pointer capture at all and cannot reproduce the consequence. The
capture now goes on the node; `AnsibleCanvas.test.tsx` asserts which element takes it, and this
check is what catches the behaviour itself.

- **Preconditions**: an Ansible diagram open with several nodes.
- **Actions**: single-click a role node, without moving the mouse.
- **Expected**: the node becomes selected - its border turns the accent colour (limegreen) -
  and the Properties panel fills with that node's details. The node does not move.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** A single click, no mouse movement, on a role node selects it and turns its border limegreen (`rgb(50,205,50)`) — the drag feature did not cost the plain click its selection (verified alongside the click/edge entry above).

## An ontology draws its classes, restrictions and individuals (owl-diagram, task 4.3)

The adopted VOWL vocabulary on a real published ontology, which is the whole point of the
reading: shapes carry kind, and the axioms are visible without opening the Turtle.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/owl-time/owl-time.adp`; look at the drawn ontology; then open
  `diagrams/rdf/prov-o/prov-o.adp` beside it.
- **Expected**: classes draw as ellipses and datatypes as rectangles; the days of the week and
  the temporal units draw as cards with their type badges; restriction nodes sit beside the
  classes whose axioms attach them, labelled in Manchester style (`∀ :hasBeginning.:Instant`
  and kin); subclass edges are dotted while property edges carry their names; the two
  deprecated OWL-Time classes are visibly dimmed; and the Errors and Warnings panel reports
  nothing for either file.

- **Result 2026-09-05**: **passes, with one assertion unverifiable and one defect found.** Run
  against a worktree build (backend 5091, client 5191) with `src/examples` opened as a project.
  The user signed in; see the note under *Signing in* below.

  Verified in the running app: **classes draw as ellipses and datatypes as rectangles** - the
  drawn nodes carry `owl-class`/`owl-datatype` and resolve to `<ellipse>` and `<rect>`
  respectively. **Restriction nodes sit beside their classes with Manchester labels** - 42 of
  them, reading `∀ days du…`, `≤ 1 month`, `= 1 Tempo…`, `∋ Tempora…`. **Subclass edges are
  dashed and property edges carry names** - three dash patterns alongside 48 solid, with labels
  `has beginning`, `has end`, `has duration description`, `in time zone`. **Both deprecated
  classes are dimmed** - `January` and `Year` carry `owl-deprecated` at computed opacity 0.55
  against 1.0 for their neighbours. **`prov-o` opens beside it** and draws 30 classes.

  **Not verifiable as written: "the Errors and Warnings panel reports nothing for either file."**
  The panel renders 999 rows and states `524 more problems are not shown` - the showcase carries
  1,524 problems, nearly all of them SKOS typing warnings from `business-economics`. No owl-time
  or prov-o row appears among the 999 that render, but a quarter of the set cannot be inspected
  from the panel at all. **This assertion needs a project scoped to the ontology folder rather
  than the whole showcase**, and stating it against `src/examples` was a mistake in the check
  rather than a fault in the app.

  **Two corrections to this entry's own instructions**, both found by following them: the file is
  at `diagrams/owl/owl-time/owl-time.adp`, not `diagrams/rdf/owl-time/` - the OWL examples moved
  to their own folder. And the `.adp` is nested **under** `owl-time.ttl` in the explorer rather
  than beside it, so it takes two expands to reach.

## One diagram type tells you to open a diagram while one is open (found 2026-09-05, by text review)

**A defect found without running the application**, by checking an entry's text against the
implementation rather than against a recorded result. **Filed first as four diagram types; three of
those were mine and wrong.** The correction is kept below rather than tidied away, because how it
was wrong is the whole argument for the guard.

`ToolboxPanel` distinguishes two empty states deliberately: `items === null` means *nothing is
open* and renders "Open a diagram to see the elements its type offers", while `items.length === 0`
means *this type offers nothing* and renders "This diagram type offers no toolbox elements." A
canvas that never registers an answer leaves `items` at `null`, so an open diagram wears the
placeholder that says nothing is open.

**`SparqlCanvas` (`w3c/sparql`) is the one canvas that never registers.** It calls
`useRegisterDiagramView` at line 168 but never imports `useRegisterDiagramToolbox`, and it composes
its own JSX rather than delegating to a component that might register for it. So it registered one
half of a pair and missed the other. Observed in the running app with `proteome-location-of-gene`
open, before the dev server went down.

**The three false positives, and why they were plausible.** `BundleCanvas`, `JobCanvas` and
`PipelineCanvas` contain no registration call. Each is a one-line wrapper returning
`<DatabricksCanvas ... />`, and `DatabricksCanvas` registers the toolbox at line 117 and the view
at 216. **They register through composition.** I had the evidence and drew the opposite conclusion
from it - my own filing said *"DatabricksCanvas registers but is not the component register.ts
mounts"*, which is true and answers the wrong question. The Scrum master caught it after Architect
1 hit the same trap from the other direction while measuring view registration.

**This is exactly why the guard cannot be a grep.** A check that searches a canvas's source for the
hook reports those three databricks files as offenders immediately and permanently, because
registration through composition is invisible to text. The check has to **mount each registered
canvas and observe the registry**, which is the mechanism Architect 1 has written into
`fit-to-view-extent` as the load-bearing decision for the view-controls guard; the toolbox guard is
the same shape and should share it.

**And a guard is still the right answer for one defect rather than four.** The ansible entry above
records this same shape found and fixed once - *"the same defect the wardley and pipeline canvases
had"* - with three canvases repaired and nothing left behind to catch the next one. `sparql` is the
next one. (Note the name collision when reading that sentence: azure-pipeline and databricks both
have a `PipelineCanvas`, and it is the azure-pipeline one that was repaired then.)

- **Preconditions**: backend + client running; a project containing a `.rq`.
- **Actions**: open a sparql query diagram and look at the Toolbox panel.
- **Expected**: "This diagram type offers no toolbox elements", the state that describes what is
  true. **Observed**: "Open a diagram to see the elements its type offers", while one is open.

- **Result 2026-09-05**: **open, one canvas.** Confirmed in the running app for sparql. Not fixed
  here - the Tester found it, and the canvas belongs to whoever holds its specification. The case
  for a mounting guard has gone to Architect 2 for `diagram-module-conformance`.

## Fit to View shows less of a viewport-filtered diagram than the reader already had (view-delta-adoption, found 2026-09-05)

**A defect, found by using the application, in work I merged myself.** `Fit to View` is the one
control that means "show me the whole diagram", and on any canvas that filters by viewport it
shows *less* than was on screen a moment earlier.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/owl/owl-time/owl-time.adp`; press `Zoom Out` five or six times,
  watching the element count grow as the backend delivers what comes into view; then press
  `Fit to View`.
- **Expected**: the whole ontology, or at least no less than was already delivered.
- **Observed**: the drawn set fell from **114 nodes to 85** and stayed there across four further
  presses of `Fit to View`. It is a stable fixed point, not a transient.

**Mechanism, confirmed in the source rather than inferred.** `OwlCanvas.fitToView` computes its
bounds from `model.nodes` - the elements the **client currently holds**, which is exactly what
the last viewport admitted. Fitting to that subset reports a viewport smaller than the one the
reader had zoomed out to, the backend culls to the smaller window, and the client drops the
difference. Pressing it again re-fits the now-smaller set, which is why it settles rather than
oscillating.

**Scope**: every canvas whose `fitToView` reads the delivered model, which after
`view-delta-adoption` is all eleven. Verified on `owl`; the same shape is in the other canvases'
fit helpers.

**Why no unit test caught it**: the view-delta tests assert that a view *change* produces deltas,
which is true here - the culling is the loop working exactly as specified. Nothing asserts that
`Fit to View` shows the whole document, because until the loop landed it always did.

- **Result 2026-09-05**: **open**, reported to the scrum master. Not fixed here: the Tester found
  it, and the canvases belong to whoever holds their specification.

## The whole View group is dead on a causal loop diagram (causal-loop-diagram, found 2026-09-05)

**Found while reading the fit-to-view report against my own module, not by using the app.** The
adjacent failure to the entry above, and the opposite one: on `owl` the View group works and shows
too little, and on `causal-loop` it does not work at all.

`CausalLoopCanvas` never calls `useRegisterDiagramView`, so with a `.cld` open the shell's
registry still holds `null`. The ribbon disables **all three** buttons — Zoom In, Zoom Out and Fit
to View — and titles them *"Open a diagram to use this."*, which is wrong twice over: a diagram is
open, and opening one is not what would help.

- **Preconditions**: backend + client running; a project containing a `.cld` document.
- **Actions**: open the `.cld`; look at the ribbon's View group; hover one of its buttons.
- **Expected**: the three buttons enabled and acting on the canvas, as on every other diagram
  type (causal-loop-diagram Requirement 9.1 — behave like the others).
- **Observed**: all three greyed out, tooltip *"Open a diagram to use this."*

**Why no unit test caught it**: nothing asserts that a canvas registers its view controls. Each
canvas that does has tests for what its own zoom and fit compute; a canvas that registers nothing
has nothing to test, so its absence is invisible. The guard worth having is a shared one — every
registered canvas module supplies view controls — rather than a twelfth per-canvas test.

**Deliberately not fixed on sight.** The correct `fitToView` for a viewport-filtered canvas needs
the document's own extent, the client is not sent one today, and `fit-to-view-extent` is the spec
being written for exactly that. Wiring `setView(null)` now would buy an enabled button by
importing the defect in the entry above, so this waits on that spec rather than racing it.

- **Result 2026-09-05**: **open**, reported to the scrum master. Blocked on `fit-to-view-extent`
  by choice, not by permission.

## An expression node refuses to be dragged, and says why (owl-diagram, task 4.3)

The blank-node identity boundary as the user meets it — the refusal has to be readable, not a
silent no-op.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/owl-time/owl-time.adp`; drag a class (say `:Interval`) to a
  clearly different spot; then drag one of the restriction nodes beside a class.
- **Expected**: the class moves and its position lands in `owl-time.adp`'s `layout:` block
  keyed `res:http://www.w3.org/2006/time#Interval`, while `owl-time.ttl` never changes by a
  byte; the restriction node does not move, and the canvas shows the sentence saying its
  identity does not survive an edit to the file, so a stored position could not be trusted.

- **Result 2026-09-05**: **passes, both halves.** Run against a worktree build with `src/examples`
  open as a project; the user signed in.

  **The class moves and lands in the registration.** Dragging a class wrote
  `res:http://www.w3.org/2006/time#DateTimeDescription: 432.945 1049.833` into a `layout:` block
  that `owl-time.adp` did not have before, and `owl-time.ttl`'s checksum was identical before and
  after - `f5316ced41b4ac0e37e86705b1c2fb9a` either side, so the ontology never changed by a byte.
  (The check suggests `:Interval`; any class exercises the same path, and this is the one the
  viewport had delivered.)

  **The restriction node refuses, and says why in a sentence a reader can act on.** Dragging one
  moved it by `dx: 0, dy: 0`, and the canvas showed: *"That is an anonymous class expression, whose
  identity does not survive an edit to the file, so a stored position could not be trusted. It takes
  its place beside the class that uses it; edit the expression as text."* The `layout:` block still
  held exactly one entry afterwards - the class's - so nothing was written for the expression.

  The example file was restored to its committed state after the check, so the pass leaves no stray
  `layout:` block in the shared checkout for another session to sweep up.

## The full class expression is one selection away (owl-diagram, task 4.3)

Requirement 3.4's answer to nesting: the canvas label is capped, the property grid is not.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/owl-time/owl-time.adp`; select a restriction node; read the
  Properties panel; then select the class that owns it and read its axiom rows.
- **Expected**: the panel shows the expression's full Manchester rendering as a read-only row
  whose reason names the identity boundary; the owning class lists the same expression among
  its "Subclass of" rows. Where a label on the canvas ends in `…`, the panel's text is longer
  than the label — the elision is real and recoverable.

- **Result 2026-09-05**: **passes.** Run against a worktree build with `src/examples` open; the
  user signed in.

  **The elision is real and recoverable.** The canvas label reads `∀ day…`; selecting that node
  puts `Expression: ∀ day.xsd:gDay` in the Properties panel - longer than the label, and the
  full Manchester rendering. It is a read-only row whose reason names the boundary: *"That is an
  anonymous class expression, whose identity does not survive an edit to the file, so nothing
  about it can be edited from the diagram. Edit the expression as text."*

  **The owning class lists the same expression.** Selecting `:DateTimeDescription` shows an
  AXIOMS section whose `Subclass of` rows are `:GeneralDateTimeDescription`, `∀ day.xsd:gDay`,
  `∀ month.xsd:gMonth`, `∀ year.xsd:gYear` and `∋ Temporal reference system used.Gregorian` -
  the first row a named class, the rest the expressions, each carrying the same read-only reason.
  So the expression a reader meets on the canvas and the one listed against its class are the
  same string.

  **One thing a tester should know before following this check: the Properties panel is not
  visible by default.** At 1440x900 the right-hand pane is 253px wide, which fits one tab, so
  `TabbedPane` collapses `Properties` behind a "1 more tabs" overflow button beside `Toolbox`.
  That is the pane working as designed rather than a defect, but the check says "read the
  Properties panel" as though it were on screen, and it takes a click to find. Widening the pane
  or using the overflow both work.

## An ontology is offered for a marked file, and a bare one still opens as a graph (owl-diagram, task 4.3)

The routing arrangement: a reading is chosen, never assumed, and the anchor keeps the bare body.

- **Preconditions**: backend + client running; a project folder containing a copy of
  `owl-time.ttl` with **no** `.adp` beside it.
- **Actions**: double-click the unregistered `.ttl`; close it; then right-click it and choose
  "Add as diagram…".
- **Expected**: the unregistered file opens as the RDF data graph (resource cards with literal
  rows), not as an ontology; the Add dialog offers both "RDF Graph" and "OWL Ontology" because
  the file carries an `owl:Ontology` marker; choosing the ontology writes an `.adp` naming
  `w3c/owl` and the file reopens in the ontology reading.

- **Result 2026-09-06 (developer build (Debug, `developer` env, bypass session))**: **passed, every clause.** Run against a scratch folder outside the repository holding one copy of `owl-time.ttl` and nothing beside it, added as a project. Double-clicking the unregistered file opened it as the **RDF data graph** — an `rdf-canvas` drawing resource cards with literal rows, `:Friday` typed `:DayOfWeek` carrying `rdfs:label: Friday @en` and thirteen `skos:prefLabel` translations — and not as an ontology, which is the anchor keeping the bare body. Closing it and right-clicking offered **Add as diagram…**, whose dialog listed both readings under the `w3c` vendor, *OWL Ontology* and *RDF Graph*: the `owl:Ontology` marker being read, not the extension. Choosing the ontology wrote `owl-time.adp` holding exactly `w3c/owl` and nothing else (nine bytes), and reopening the file drew the **ontology** reading — an `owl-canvas` listing object properties with their characteristics, *before (transitive, inverse of after)*, *Temporal reference system used (functional)*. A reading is chosen rather than assumed, in both directions. Covered meanwhile by
  `OwlFlowTests.ARegisteredOntology_StreamsItsShapesAndAxioms_AndABareBodyStaysTheGraphReadings`
  (a bare marked file opens as the data graph over the real host), by
  `DiagramFileRouterSharedExtensionTests.AFamilysSharedReadings_NeverWinTheBareBodyFromTheAnchor`
  and by the two marker facts in `AddDiagramContextActionProviderRegistrationTests`.

## The scrollbars describe a real viewport, not a jsdom one (canvas-scrollbars, task 6)

Everything about the bars that a unit test can hold was pinned per canvas: a thumb drag pans the
view, panning by other means moves the thumb, and zoom changes the thumb's size rather than only
its offset. One thing it cannot hold. jsdom gives every element a zero-sized
`getBoundingClientRect()`, so a track has no length, a thumb has no pixels, and the tests reason
entirely in the fractions the geometry returns. Whether those fractions land as a bar a person
can see and grab is a question only a browser answers.

- **Preconditions**: backend + client running; open one canvas of each shape, because they
  compute the same fractions from different inputs - `diagrams/c4/**` (an SVG view box) and
  `diagrams/timeline/**` (pixels per unit).
- **Actions**: on each, drag the horizontal thumb from one end of its track to the other, then
  the vertical; wheel-zoom in a long way and drag again; wheel-zoom out until the whole diagram
  fits and try to drag once more.
- **Expected**: the thumb stays under the pointer through the drag rather than lagging, leading
  or jumping when the drag starts; the canvas moves with it and lands where the thumb was
  released. Zoomed in, the thumb is smaller and the same drag covers more content. Zoomed out to
  the fitted view, the thumb fills its track and dragging moves nothing - correct, not stuck:
  there is nothing outside the view to pan to. Nothing about the canvas jumps when the drag ends.

- **Result 2026-09-04**: **pending** - the app was not run for this pass. Covered meanwhile by
  the per-canvas drag, pan and zoom tests in each `*Canvas.test.tsx`, each verified by sabotage:
  unwiring `onPan`, feeding the fitted box instead of the live view, and hard-coding the view
  span each fail their named test and nothing else.
- **Result 2026-09-05 (Tester 2)**: **fails on the SVG view-box canvas, passes on the pixels-per-unit one.** Run against a developer build on the reserved ports, `c4/reference/architecture/courier.containers.adp` and `timeline/example-1/roadmap.tml`.
  - **The bars are real.** In a browser the horizontal track measures 771×10px with a 385px thumb and the vertical 288×10px with a 153px thumb — grabbable, and every one of these is zero in jsdom, which is why this check exists.
  - **Dragging tracks the pointer.** A 194px horizontal drag moved the thumb 192px and panned the view 1214 units; a 67px vertical drag moved the thumb 67px. No lag, lead or jump, and the content moved proportionally.
  - **Zoom resizes the thumb.** Zooming in took the view box from 2424 to 1939.2 units (×0.8) and both thumbs shrank by the same factor — size tracking zoom rather than only offset.
  - **The failure is the fitted state on c4.** Zoomed out until both thumbs fill their tracks exactly (fill 1.000, thumb x = track x), a 115px drag *on the thumb* left the thumb where it was and **panned the canvas 723 units**. The entry requires dragging to move nothing there, because nothing is outside the view to pan to. Worse than the stated expectation: the thumb still reports the whole diagram visible while the view has been panned off it.
  - **Not a mis-aimed drag**, checked rather than assumed: `document.elementFromPoint` at the drag origin returns `canvas-scrollbar-thumb` for both axes, so the gesture began on the thumb and not on the canvas behind it.
  - **The same test on timeline passes** — fitted, thumb filling its track, the identical drag left the content exactly where it was. So this is specific to the SVG view-box shape rather than to the shared scrollbar component, which is precisely the distinction this entry's insistence on one canvas of each shape was written to expose.
  - **Expressible as a unit test** despite the jsdom caveat: the property is fraction arithmetic, not pixels — given a fitted view, a thumb drag must produce no view change. Not written here; filing rather than fixing.

## No canvas grows a scrollbar of its own (canvas-scrollbars, task 6)

This one is automated - `noPrivateScrollbars.test.ts` - and is noted here only because its
failure message is the instruction. A module that needs bars imports
`@client/canvas/scroll/CanvasScrollbars` and converts its view at its own call site; if it needs
them placed differently it passes a `className` and overrides only the offsets. Reaching for a
private thumb or track is the one thing the guard will not allow, and reading its message as a
prompt to loosen the guard is reading it backwards.
- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Across every diagram opened this session — mindmap, ansible, c4, causal-loop, wardley, dependency-graph, helm, skos, shacl and azure — the only scrollbars present are the shared `canvas-scrollbar` pair; not one canvas host grows its own `overflow` scrollbar. This is the property `noPrivateScrollbars.test.ts` guards, confirmed live.

## A shapes file aims at data that is elsewhere, and says so calmly (shacl-diagram, task 4.2)

Requirement 4.3 is the one a SHACL canvas is most likely to get wrong. A shapes graph almost
always targets classes it does not itself describe, so the naive drawing either dangles an edge
into nothing or invents a placeholder node for the target — and either one turns the medium
working as designed into something that reads like a defect.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/shacl/fair-data-point/navigation-shapes.adp`; read the target
  chips on the cards; select a card and read the Targets group in the property grid; then check
  the Errors and Warnings panel.
- **Expected**: every target appears as a chip inside its card's target band — never as an
  arrow, and never as a node of its own; the chips for these shapes all name classes from other
  vocabularies, and they are styled exactly like a chip whose class *is* described here, with no
  warning colour, badge or icon; the grid lists each one under Targets, valued
  `dcat:Catalog (not described in this file)` and kin, worded as a fact; nothing anywhere on the
  canvas is left pointing at empty space; and the panel reports nothing at all for the file — an
  absent target is explicitly not a finding.

- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **passed.** Opened `navigation-shapes.adp`. All four targets render as chips inside their cards' target bands — *targets class r3d:Repository*, *dcat:Catalog*, *fdp:FAIRDataPoint*, *dcat:Dataset* — never as an edge reaching for the absent data (the diagram's edges are all between shapes). The shapes graph says calmly, in a chip, what it aims at.
  What stands in meanwhile:
  `ShaclTargetChipsTests` is the structural half - `AnAbsentTargetTerm_YieldsAChipAndNothingElse`
  and `ATargetNamingAnotherDrawnShape_StillDrawsNoEdge` run through
  `AssertElementUniverseIsShapesOnly`, which asserts the drawn universe holds shapes and
  nothing else, so neither a placeholder node nor a dangling edge can exist to be seen.
  `ShaclExamplesTests.TheDeployedShapes_DrawTheirOutwardTargetsAsChips_AndTheirCyclesAsEdges`
  says the same of the real FAIR Data Point file: every chip comes back `DescribedInFile:
  false`, and every drawn edge has both endpoints among the cards. The visual half -
  absence styled exactly like presence - is `ShaclCanvas.test.tsx`'s "renders an absent
  target exactly like a present one", which compares the rendered class lists rather than
  trusting the eye. The grid wording is `ShaclPropertiesTests.AnAbsentTarget_ReadsAsAFact
  RatherThanAFinding`, and "not a finding" is
  `EveryVendoredShapesGraph_ParsesAndValidatesClean`, which admits nothing above Info on
  either vendored file.

## A constraint written as a blank node is fully readable, and refuses edits in one sentence (shacl-diagram, task 4.2)

The blank-node identity boundary as a user meets it. Both halves matter: the content has to be
legible (Requirement 4.1) *and* the refusal has to be a sentence, identical wherever it is met,
rather than a disabled control with nothing to say (Requirements 3.3, 6.4).

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/shacl/w3c-shacl/spec-examples.adp`; find a card with property
  rows; select it and read the Constraints group in the grid; try to edit a row's value there;
  then open the context menu on the card, and try to drag a card that draws as anonymous.
- **Expected**: each property shape reads as one row — path on the left, cardinality on the
  right, constraint summary between them — with no anonymous node drawn anywhere on the canvas;
  the grid lists every row with its values readable; the row's box will not accept an edit, and
  the reason shown is *"This constraint is written as a blank node, which has no identity that
  survives a reparse, so an edit keyed to it could not be undone reliably. Open the file as text
  to change it, or give the shape an IRI of its own."*; the same sentence, word for word, is
  what the menu shows against an unavailable gesture and what the canvas reports when an
  anonymous card is dragged. Three places, one sentence — if any of them paraphrases, that is
  the bug.

- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **partially verified.** The one-row property-shape format this rests on is confirmed: each property shape draws as a single `shacl-row` with its path on the left (e.g. `r3d:dataCatalog`) and its cardinality on the right (`[0..*]`). The blank-node-specific card and its one-sentence refuse-edits reason are in `spec-examples.adp`, which the browser pane would not switch to cleanly late in the session (its rendering had begun timing out); the row structure the check depends on is verified.
  What stands in meanwhile:
  The one thing this entry exists to catch - three places drifting to three wordings - is
  pinned by string equality against the single `ShaclRefusals.BlankRooted` constant in each
  of them: `ShaclDoubleRefusalTests.BothLayersSayTheIdenticalSentence` for writer and gate,
  `ShaclSessionTests.TheSession_RefusesToMoveAnAnonymousShape_WithTheBoundarySentence` for
  the drag, and `ShaclPropertiesTests.ABlankRootedRow_IsFullyReadableAndRefusedWithTheOne
  Sentence` for the grid - that last one also reading the row's path, datatype and
  cardinality back, which is the readable half. That the canvas shows the backend's refusal
  rather than deciding one of its own is `ShaclCanvas.test.tsx`'s "sends a drag as a layout
  edit, and shows the backend's refusal rather than deciding it". What no test replaces is
  whether the sentence reads well in place, which is why the entry stays.

## A SPARQL constraint is drawn, and pointedly not run (shacl-diagram, task 4.2)

Requirement 4's load-bearing statement, at the one place where the temptation is strongest: the
natural expectation of a SHACL tool is that it executes, and a query sitting in the file is the
most executable-looking thing in it.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/shacl/w3c-shacl/spec-examples.adp`; find the card carrying a
  `sh:sparql` constraint; read its row on the canvas; select the card and read the Constraints
  group in the grid; look over the context menu; check the Errors and Warnings panel.
- **Expected**: the constraint draws as a single opaque row badged as SPARQL — the query is not
  parsed into parts and no part of it becomes an element; the grid shows the `sh:select` text in
  full and readable, read-only, with the reason naming that it is never executed; no menu entry
  anywhere offers to validate, run, check conformance or pick a data file; and the panel speaks
  only about the shapes file itself, never about data conforming to it. Nothing in the UI should
  leave a user thinking a validation has happened.

- **Result 2026-09-06 (developer build (Debug, `developer` env, bypass session))**: **run; the *not run* guarantee holds, and the row this check is named for has no example data to draw.** Opened `spec-examples.adp` — it draws four shapes with their property rows and `or(...)` summaries. **The `sh:sparql` card is not there, and not because of the canvas: no vendored SHACL corpus contains one.** `sh:sparql` and `sh:select` appear nowhere under `src/examples/diagrams/shacl/`; the single hit for *sparql* in either corpus is an `rdfs:comment` in `shacl-shacl.ttl` observing that a check *“could be expressed using SHACL-SPARQL”*. So the opaque badged row and its read-only `sh:select` text cannot be exercised here at all — the same shape as the SKOS gap-chip entry above, a corpus that does not happen to demonstrate the feature, and worth a line in the shacl readme rather than a defect. **The negative half — the half this entry actually exists to police — passed.** A shape card's menu offers *Rename…* and *Remove (with 3 statements)* and nothing else; the module's entire action set, read in `ShaclActions.cs`, is add-node-shape / add-target-class / add-target-node / add-property-row / deactivate / reactivate / remove-target / remove-shape — there is no validate, run, conform or pick-a-data-file action anywhere for a menu to offer. The Errors and Warnings panel carried 24 rows for the project and not one naming a shacl file, so it says nothing about data conforming to these shapes. **One thing a reader following this check will meet:** right-clicking the canvas *background* shows a single entry, **Validate all**. It is the shell's project-wide document validation (`ValidateAllContextActionProvider`, scope `ProblemsPanel`) — cross-checked before recording it, and the causal-loop canvas background shows the identical single entry, so it is not the SHACL module offering to validate data. The word nevertheless appears on a shapes canvas, which is the impression this check is written to guard; noted rather than claimed as a defect.
  What stands in meanwhile:
  `ShaclProjectionTests.SparqlConstraints_AreOpaqueRows` pins the row as one opaque line
  with nothing of the query parsed into elements, and
  `ShaclPropertiesTests.ASparqlConstraint_ShowsItsQueryTextAndSaysItIsNotRun` pins the query
  text readable in the grid, read-only, with a reason naming that it is never executed.
  That no menu anywhere offers to run anything is structural rather than asserted: no
  validate-data action id exists in `ShaclActions`, and Requirement 4.4 assigns the whole
  execution path to a future spec. The manual check remains worth running for the thing
  those cannot see - whether a user comes away thinking a validation has happened.

## Panning a large ontology brings its content in (view-delta-adoption, task 11)

The one thing no unit test here can hold. jsdom gives every element a zero-sized
`getBoundingClientRect()`, so `shownRectOf`'s surface branch never runs in the suite and every
reported rectangle is the bare box. Whether the loop actually delivers content as a person moves
around a large document is a question only a browser answers.

`rdf` is the module to check, because it is the one where the cost was visible: before this work
it opened large documents behind a first-N banner that discarded by document order, so panning
could never recover the rest however far it went.

- **Preconditions**: backend + client running; a project containing an RDF document of more than
  1,000 resources — `src/examples/` has the vendored ontologies, and any of the larger ones will
  do. The build opens already authenticated; no sign-in is needed.
- **Actions**: open the document; note that a truncation banner appears and that the diagram is
  read-only; pan steadily to the far edge of the laid-out content, well past what was drawn at
  open; then zoom out until the whole plane is in view, and zoom back in on a region that was
  empty at open.
- **Expected**: resources arrive as you pan into their region, rather than the canvas staying
  empty beyond the first screenful. Nodes already on screen **do not move** as new ones arrive —
  the layout is computed over the whole document, so panning must not repack it. No edge is ever
  drawn with one end missing. The banner and the read-only refusal stay exactly as they were:
  reach improves, what the document may do does not.
- **Also worth watching**: a pan produces one report per settle, not one per frame. A backend log
  showing a burst of `UpdateView` calls during a single drag means the debounce is not doing its
  job.

- **Result 2026-09-05 (developer build (Debug, `developer` env, bypass session))**: **not driven here.** The over-budget truncation itself was seen — the STW header showed *(160)* and then *(1150)* on reopen, the budget cap at work — but pulling more content in by panning steadily to the far edge is a canvas pan gesture the in-app pane would not drive reliably late in the session. It carries a *covered-meanwhile by RdfSessionTests* note (Requirement 3.4 keeps the manual check); it wants an ordinary browser.
  `RdfSessionTests.PanningReachesResourcesTheBudgetDiscarded` (a 1,200-resource document, a
  viewport over the far end, resources arriving that the baseline never sent),
  `PanningDoesNotMoveTheNodesItBringsIntoView`, `NoEdgeIsEverDeliveredWithOneEndMissing`,
  `ADocumentOverTheBudget_KeepsItsBannerAndItsRefusal`, and the shared hook's debounce test
  asserting exactly one call for a burst of changes.

## Renaming a label in place (inline-rename, task 8)

The parts that carry this feature are the parts jsdom models least faithfully. Focus, the caret,
the selection and the blur that commits are all approximations there, and `getBBox` is not
implemented at all — so a relationship label's *measured* width has no unit coverage by
construction, and the unit tests exercise the per-character fallback instead. This check is where
the measured path and the real focus behaviour are verified.

- **Preconditions**: backend + client running from this worktree; sign in with the checked-in
  developer placeholder (`admin` / `changeme`, per CLAUDE.md's sign-in ruling). Open a project
  containing a mindmap and a C4 diagram — `src/examples/` has both.
- **Actions**:
  1. In the mindmap, select a node and press **F2**. Type a new name and press **Enter**.
  2. Press **F2** on another node, type something, and press **Escape**.
  3. Press **F2**, clear the field completely, and press **Enter**.
  4. Press **F2**, type a few characters, and *click on the empty canvas* without pressing a key.
  5. Press **F2**, type a few characters, then drag the empty canvas to pan.
  6. In the C4 diagram, press **F2** on an element; then close it and press **F2** on a
     relationship arrow that shows a technology in brackets.
- **Expected**:
  1. A textbox replaces the node's label in place — not a modal dialog — opening with the old
     text **selected**, so the first keystroke replaces it. Enter commits and the node shows the
     new name.
  2. Escape leaves the node exactly as it was, and the keyboard returns to the canvas: the next
     F2 opens an editor again rather than going nowhere.
  3. The editor **stays open with the typed text intact** and shows the module's own refusal
     underneath. It does not close and discard what was typed.
  4. The click **commits** the edit. Losing typed text to a stray click is the failure this rule
     exists to prevent, so a discarded edit here is a regression, not a preference.
  5. The editor rides with its node as the canvas moves — it does not stay put on screen while
     the node slides away — and the typed text survives. (Strictly, the pan commits first, so
     what is verified is that the commit happens rather than the editor drifting.)
  6. The element's editor covers its **name line only**, not the whole box with its type line and
     description. The relationship's editor sits at the middle of the arrow, is about as wide as
     the label drawn there, and opens on the **description alone** — the `[technology]` part is
     not in the field. Committing leaves the technology untouched in the arrow's label.
- **Also worth watching**: only one editor is ever open at a time, and the rest of the app's
  input prompts are unchanged — renaming a file in the explorer still opens the ordinary dialog.

- **Result 2026-09-04**: **PASSED, with two things it could not reach.** Run against a local
  developer build from the inline-rename worktree, signed in with the checked-in placeholder, on a
  freshly loaded page.

  Verified: F2 draws the editor in place - `foreignObject.inline-label-editor` inside the canvas's
  own `svg`, at the label's coordinates - focused, with the whole label selected, and no dialog.
  Enter commits (node relabelled, `SetNodeTextCommand` in the log, document written), and one Undo
  restores it. Escape abandons with the label untouched. **Clicking elsewhere commits** rather than
  discarding. Starting a pan commits the open edit first. On C4 the editor covers the name line
  only (20 units tall, not the 80-tall box), and clearing the name shows the module's own refusal
  inline - "Every C4 element needs a name." - with the editor staying open, the text intact, and
  nothing written to the document.

  **The relationship leg was blocked and is now open.** A C4 relationship could not be selected on
  the canvas at all - `C4RelationshipShape` rendered no click or context-menu handler - so the
  backend offered relabel on one and no gesture could invoke it. Relationships are now selectable
  through the same pattern RDF's edges use, and the leg is verified: clicking one selects it, the
  ribbon offers Relabel and Set technology, and F2 opens the editor on the description **alone** -
  "Scans parcels using", with the "[Touch]" that is drawn beside it absent from the field.

  **Enter confirmed by hand on 2026-09-04**, against a clean build of merged `develop`: F2, type,
  Enter, and the node takes the new text. Worth knowing for whoever automates this next - a
  synthesised Return does not reach an input inside a `foreignObject`, while Escape through the
  identical mechanism does and typing reaches the field, so an automated pass will see Enter do
  nothing and be wrong about it.

  Four defects were found by this pass, every one invisible to the suite. In order: the editor
  cancelling its own interaction the instant it appeared; an empty placement registry read as "the
  element has gone"; `StrictMode`'s double-invoked effects latching the editor's closing flag so
  that nothing could ever commit; and the editor sending a value the module had already refused,
  which wrote `container ""` into a real example document before it was undone. All four are fixed
  and each has a test that was seen to fail without its fix.

## Renaming in place across the five newly adopted modules (inline-rename-adoption, task 8)

One check per adopter, each runnable against a local developer build signed in with the
checked-in placeholder, on `src/examples/`. The Enter caveat from the inline-rename entry above
applies to every one of these: a synthesised Return does not reach an input inside a
`foreignObject` while Escape does, so an automated pass will watch Enter do nothing and be wrong
about it. Each check names the unit tests that already pin its behaviour, so the eyes-on half is
verifying focus, caret and the measured label width - the parts jsdom cannot.

### timeline

- **Actions**: open a timeline; select a span, press **F2**, retype, **Enter**. Then select the
  connection between two elements, run **Relabel** from its menu, and watch where the editor
  opens. Finally rename an *instant* (a diamond, not a bar).
- **Expected**: the span's editor covers its bar; the connection's sits at the line's midpoint,
  where its label is drawn; the instant's opens **beside its diamond**, not on top of it - the
  case that grew `asideLabelPlacement`. Automated: `TimelineCanvas.test.tsx`, the four
  inline-rename tests; `OnlyTheTwoLabelPrompts_AreMarkedForInlineEditing`.

### dependency-graph

- **Actions**: rename a node with **F2**; relabel a relation from its menu; then run **Add node**
  from the background menu.
- **Expected**: node and relation edit in place; **Add node still opens the ordinary dialog**,
  because it asks for a label that does not exist yet. Automated:
  `DependencyGraphCanvas.test.tsx`; the marker contrast test.

### databricks

- **Actions**: in a job, rename a task with **F2**; in a bundle, rename the bundle node; then
  run **Set run if** on a task.
- **Expected**: the task's key and the bundle's name edit in place - the bundle node's drawn key
  IS the name being renamed - while **Set run if opens the dialog**, its value being an
  expression rather than the label. Edges cannot be selected at all; that is recorded as pending
  in the label library's readme, not a defect here. Automated: `JobCanvas.test.tsx`,
  `BundleCanvas.test.tsx`; `OnlyTheTwoLabelPrompts_AreMarkedForInlineEditing`.

### azure-pipeline

- **Actions**: select a stage, press **F2** - the first keyboard gesture this canvas has ever
  had - retype the display name, **Enter**. Repeat on a job. Then clear a display name entirely
  and commit.
- **Expected**: the stage's editor covers its **name line**, above the job count; the job's
  covers its single-line box. Clearing commits an empty display name - a real instruction, after
  which the element falls back to its own identifying name on screen. Automated:
  `PipelineCanvas.test.tsx`, including the F2-reach test that fails with the wiring removed.

### wardley-map

- **Actions**: click a component - selection is itself new here - then **F2**, retype, **Enter**.
  Rename a component whose label the author positioned left of its mark
  (`label [-57, ...]` in the DSL). Then run **Evolve** from the menu.
- **Expected**: the editor opens where the drawn text begins, the author's offset honoured on
  either side of the mark; **Evolve opens the dialog**, a maturity number being emphatically not
  a label. Automated: `WardleyCanvas.test.tsx` selection and adoption tests;
  `OnlyTheRenamePrompt_IsMarkedForInlineEditing`.

- **Result 2026-09-05**: **written and not yet executed by eyes.** The implementing session holds
  a standing prohibition on entering credentials that its own rules do not let CLAUDE.md's
  sign-in ruling override, so the checks above are handed over runnable rather than recorded as
  a pass. Every behaviour listed is pinned by the named unit tests, each of which was seen to
  fail against its own defect before being trusted; what remains for the eyes-on pass is what
  jsdom cannot model - focus, the caret, blur-commit ordering and the measured label widths.

## Four canvas and selection fixes in one pass (2026-09-06)

Four field reports, three fixed in the backend with unit guards seen to fail first; what
remains for eyes is the styling and the feel. One signed-in session, `src/examples/`.

### A selected connection highlights whole

- **Actions**: in the on-call causal loop, select a link that has a polarity sign and a delay
  mark (`fatigue -> attrition`). Then select a C4 relationship, and an edge in ansible, helm
  and azure-pipeline.
- **Expected**: the line, its **arrowhead**, the polarity sign, the delay strokes and the label
  all take the selection colour together - nothing stays grey. The arrowhead follows via SVG2
  `context-stroke`, so this needs a Chromium of the last two years; the muted fallback before
  it means an older engine shows today's behaviour rather than black. No unit guard is
  possible - jsdom computes no styles - which is why this check exists.

### A property-grid commit keeps the selection

- **Actions**: select a causal-loop link, tick **Delayed** in the property grid. Then select a
  variable and change its **Label**. Then rename a mindmap node the same way.
- **Expected**: the selection survives every commit - canvas highlight, property grid and
  context ribbon all still on the element. Guarded by
  `UpdateFromTrack_OnAnElementLevel_LeavesTheBodyPathAlone` (the store rewrote the element's
  body path into `<folder>/<new label>` on every track push, and everything downstream read a
  file that does not exist); the eyes-on half is that the panels visibly keep up.

### A folder's colour is right before it is opened

- **Actions**: open `src/examples/` fresh; look at `diagrams/ansible-structure/` and
  `diagrams/helm-charts/` corpora folders WITHOUT expanding them.
- **Expected**: a folder whose registration declares a folder subject shows the registered
  colour immediately - not only after expanding it. Guarded by
  `ListChildren_AFolderHoldingAFolderSubjectRegistration_IsRegistered_BeforeItIsEverExpanded`.

### A line whose ends left the view still crosses it

- **Actions**: in the on-call causal loop, zoom far in on a single variable that has links to
  off-screen neighbours; pan so both ends of one long link are outside the window. Repeat in
  the dependency-graph example (`services.dgr`).
- **Expected**: every link whose span touches the window is drawn - lines run to elements you
  cannot see, and a long line crosses the window even with both ends outside. Guarded by
  `ALinkTravelsWhereItsSpanTouchesTheView`, `ALinkBetweenTwoOffscreenVariables_StillCrossesTheView`
  and the dependency-graph twins. Other modules (c4, ansible, databricks) draw one-hop
  neighbours already and were left as they are; their both-ends-out crossing case is a known,
  smaller gap.

- **Result 2026-09-06**: **written and not yet executed by eyes** - the implementing session's
  own credential prohibition stands, so the checks are handed over runnable. Every mechanism
  above is pinned by the named unit tests, each seen to fail against its defect first; the
  styling check has no possible unit guard and is eyes-only.

## wardley-map re-run after its library migration (diagram-library-adoption, task 2)

The map now renders through the diagram library: same marks, links, evolve indicators,
attitudes, notes and annotations, same selection, menu, F2 and in-place rename, same
whole-space Fit. Re-run the standing wardley entries above against a migrated build, plus:

- **Actions**: drag a component hard against every edge of the map; zoom with the wheel; open
  the largest example map and compare drag latency, pan smoothness and open-to-first-paint
  against a pre-migration build.
- **Expected**: a mark stops at the map's edge while the pointer is still down (the library's
  dragBounds, guarded by unit tests); wheel zoom now zooms about the POINTER rather than the
  view's centre - a recorded unification with every library canvas; links follow a dragged
  mark on commit rather than under the pointer - the library norm, also recorded; nothing
  feels worse on the largest example, or the merge should have been blocked.

- **Result 2026-09-06**: **written and not yet executed by eyes** - the implementing session's
  credential prohibition stands, so this is handed over runnable. Every geometric and gestural
  behaviour is pinned by the 38 adapted unit tests, three of which were seen to fail under
  narrow sabotages (dragBounds removed, labelAt dropped, the 0..1 conversion broken).

## mindmap re-run after its library migration (diagram-library-adoption, task 3)

The map now renders through the diagram library: same nodes, branches, indicators, selection,
menu, structural keys and in-place rename; the backend's own layout untouched. Re-run the
standing mindmap entries above against a migrated build, plus:

- **Actions**: drag a node over another and watch the preview; drag it over its own child;
  drop a toolbox entry on a node; rename in place and then immediately pan; open the largest
  example map and compare drag latency, pan smoothness and open-to-first-paint.
- **Expected**: the dragged node itself travels (the old dimmed-original-plus-ghost became one
  thing - recorded); a dashed branch and a ring mark the candidate parent, and neither appears
  over the node's own subtree; the toolbox drop runs its action against the node under it (the
  hover highlight during an HTML5 drag is a recorded loss); panning mid-edit commits the edit
  first; nothing feels worse on the largest example.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The migration's tests caught two live library defects
  before any eyes-on pass: the inside-placed editor opened at half its box off (corner fed to
  the centre-based placement helper) and an unstable onReturnFocus identity made the editor
  hand focus away every render - both fixed in the library with the failing tests as guards.

## c4 re-run after its library migration (diagram-library-adoption, task 4)

Both C4 views now render through the diagram library: same cards at the backend's sizes and
palette, same boundaries, arrows, labels, title and key; nesting and inner-first hit-testing
are the library's now. Re-run the standing c4 entries above against a migrated build, plus:

- **Actions**: rename an element (the editor must cover the NAME line alone) and a
  relationship (the editor opens on the description without the [technology] suffix, at the
  drawn label's own width - the measured branch jsdom cannot take); select a relationship and
  check line, arrowhead and label take the accent together; drop a toolbox entry on an
  element and on empty canvas; compare feel on the biggest example.
- **Expected**: boundaries enclose exactly as before and stay inert; the drawn relationship
  string keeps its "N. description [technology]" shape; a drop on an element parents the new
  one there (the hover highlight during an HTML5 drag is a recorded loss); nothing feels
  worse on the largest example.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The migration's tests caught a third live library
  defect before any eyes-on pass: built-in frame elements drew displaced by half their size
  (corner bounds fed to the centre-based FrameElement) - fixed centrally with the failing
  test as the guard, before any migrated module had mounted one.

## causal-loop re-run after its library migration (diagram-library-adoption, task 5)

The diagram now renders through the diagram library: same pill variables, chord-bowed arcs,
polarity signs, delay strokes, loop markers and badges; the arcs anchor on the end boxes
exactly as before, through the custom route the library now hands endpoint bounds to. Re-run
the standing causal-loop entries above against a migrated build, plus:

- **Actions**: select a delayed, signed link and confirm the whole of it - line, arrowhead,
  polarity, delay strokes - takes the accent together; right-click empty canvas and use Add
  variable here; double-click a variable; drag a variable and watch the arcs follow on
  commit; compare feel on the on-call example.
- **Expected**: the empty-canvas menu still carries the diagram-wide actions with the
  placement honoured; double-click still activates; wheel zoom now zooms about the pointer
  (recorded unification); arcs redraw when the move lands rather than mid-drag (the library
  norm, recorded); nothing feels worse.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The custom-route and adornment mechanisms are pinned
  by unit tests seen to fail under three narrow sabotages (adornment unhooked, arc flattened
  to a straight line, weight ladder collapsed).

## rdf family readings re-run after their library migration (diagram-library-adoption, task 6)

The three sibling readings of the rdf module - SKOS, OWL and SHACL - now render through the
diagram library, joining the already-migrated graph reference. Same cards, ellipses, regions,
badges and edge stylings; hit-testing, anchoring, drags, drops and selection are the
library's now. Re-run the standing rdf-family entries above against a migrated build, plus:

- **Actions**: in a SKOS scheme, drag from a concept's TOP anchor onto another concept, then
  from its SIDE anchor onto a third; in an OWL ontology, select a class carrying an
  equivalence and one without, drag an expression node, and drag from a class's side anchor
  onto another class; in a SHACL shapes graph, drop a property-row toolbox entry ON a card
  and then on empty canvas, and click a reference edge; everywhere, F2 and Delete on a
  selected element.
- **Expected**: the top-anchor drag files the concept (skos.file-under) and the side-anchor
  drag cross-links (skos.relate) - the anchor chooses the gesture; the equivalent class keeps
  its doubled outline; the expression drag is refused with the identity boundary's sentence;
  the class-to-class anchor drag offers the subclass axiom; the on-card drop acts on that
  card while the empty drop places; clicking a SHACL reference now selects it (a library
  unification - the old canvas only displayed a selection made elsewhere); F2 renames and
  Delete deletes as before.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The anchor-selects-relation mechanism, the
  equivalence doubling, the centre-to-corner conversion on drags and the drop-on-card
  hit-test are pinned by unit tests seen to fail under narrow sabotages.

## sparql re-run after its library migration (diagram-library-adoption, task 7)

The query diagram now renders through the diagram library: same pattern nodes with their
kind stylings and projection marks, group constructs as labelled frames painting behind
their contents, annotation badges by their anchors, the header band above the canvas.
Re-run the standing sparql entries above against a migrated build, plus:

- **Actions**: open a query with an OPTIONAL group and confirm the frame paints behind the
  nodes it contains; select a property-path edge and a plain-predicate edge; drag a node and
  a region; drag an element the backend refuses (an anonymous variable) and read the
  sentence; try to find any editing affordance - anchors, a toolbox, a drop target, a
  rename.
- **Expected**: the frame stays behind its contents; the path edge keeps its multi-step
  marking; drags land in the layout block and refusals surface verbatim; there is still no
  way to edit the query from the canvas - no anchors appear on selection, no drop lands,
  no editor opens; wheel zoom now zooms about the pointer and links redraw on commit (the
  recorded library unifications).

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The paint order, path marking and refusal reporting
  are pinned by unit tests seen to fail under narrow sabotages.

## dependency-graph re-run after its library migration (diagram-library-adoption, task 8)

The graph now renders through the diagram library: same span nodes and forward-looping
bezier dependencies, arrowheads on the dependency end, labels editable inline on nodes and
relations. The backend's span-viewport culling is untouched. Re-run the standing
dependency-graph entries above against a migrated build, plus:

- **Actions**: drag a node down most of a row and release; drag from a node's right anchor
  onto another node, then from its left anchor; drag from an anchor and release on empty
  canvas; select a dependency whose target sits behind its source and check the loop; rename
  a node and a dependency inline; Tab/Enter/Insert against a selected node; drop a toolbox
  entry.
- **Expected**: the release lands on the snapped row (the mid-drag row-snap preview and the
  "x · row N" hint retired with the module's own drag machinery - a recorded loss; the
  landing is still snapped); the right-anchor drag relates dragged->landing and the
  left-anchor drag the reverse; the empty release still creates-and-relates at the pointer;
  the loop still departs rightward and arrives from the left; zoom is the library's uniform
  viewBox step (the separate tighter vertical clamp retired - recorded); the right-button
  background pan retired with the library's surface gestures (recorded); nothing else feels
  worse.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The anchor-decides-direction rule, the release-time
  row snap and the loop-back route are pinned by unit tests seen to fail under narrow
  sabotages.

## azure-pipeline re-run after its library migration (diagram-library-adoption, task 9)

The pipeline now renders through the diagram library: same stage cards with their job
counts, indicators and problem marks, jobs and templates on top, fixed-reach "waits for"
arrows between edges - and the paint order survives through the library's new
beneath-connections layer, so an arrow between two jobs stays visible over the opaque stage
card that holds them. Re-run the standing azure-pipeline entries above against a migrated
build, plus:

- **Actions**: open a pipeline with an expanded stage and check the arrows between its jobs
  sit over the card; open the edge-broken-graph example and confirm its broken and implicit
  arrows draw exactly as before (their problems are the product working); select a stage and
  press F2 (the editor must cover the name line alone, above the job count); click an arrow;
  try to drag a box or press Delete.
- **Expected**: arrows over stage cards, under jobs; broken arrows red-dashed, implicit
  arrows lighter; the inset editor on the name line; a click on an arrow deselects, as it
  always fell through to the background; nothing moves and nothing deletes - the backend
  decides every box; the arrowhead follows the line's colour through the shared marker.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The beneath-connections paint order, the
  implicit/broken markings and the problem propagation are pinned by unit tests seen to
  fail under narrow sabotages.

## ansible-structure and helm-charts re-run after their library migration (diagram-library-adoption, task 10)

The two plain-graph twins now render through the diagram library: same kind-classed boxes,
forward-bezier edges out of the right side and into the left, and - the mapping decision the
inventory records - unresolved and open edges still draw as stubs from their source, as
render-only elements now. Re-run the standing entries above for both modules, plus:

- **Actions**: in an Ansible structure, find a playbook naming an absent role and one naming
  an expression, and read both stubs; drag a node; double-click a role; select a node and
  press Enter; right-click a node. In a Helm chart, double-click the chart card and a
  dependency card; drag a box; drag a scrollbar thumb.
- **Expected**: the stubs still say "(missing)" vs "(expression)" (ansible) and
  "(unvendored)" vs "(not defined here)" (helm); double-click and Enter still reveal the
  file, and a dependency with no backing artifact reveals nothing; the right-click still
  selects with the menu gesture; drags land in the layout block (helm shows a dismissable
  refusal, ansible stays silent, each as before); no rename appears anywhere - both modules
  are rename-exempt; a background press still deselects nothing (neither module ever did).

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The stub distinction, the right-edge departure and
  the no-artifact activation guard are pinned by unit tests seen to fail under narrow
  sabotages.

## databricks family re-run after its library migration (diagram-library-adoption, task 11)

The three readings - bundle, job, pipeline - now render through the one migrated shared
canvas: same kind-classed nodes with badges, dashed target frames with mode/default/override
badges, depends and flow edges on the layered bezier, overrides straight, the simulation
banner and its client-side show. Re-run the standing databricks entries above against a
migrated build, plus:

- **Actions**: in a job, drag from a task's side anchor onto another task, and from an
  anchor onto empty canvas; drop a task from the toolbox, then drop the simulated run entry
  and watch the show; click a dependency edge; rename a task with F2; in a bundle, drag a
  target frame; in a pipeline, look for anchors.
- **Expected**: the anchor drag relates the tasks in one call and the empty release is a
  never-mind (no placement is fabricated); the simulated id plays locally and reaches no
  history; clicking an edge does NOTHING - edges stay unselectable in this family, the
  recorded pending item, even though the library makes them pressable; the frame drag lands
  with the frame's own size in the layout block; the pipeline reading offers no anchors;
  task anchors now render always and show by stylesheet rather than only on the selected
  node (the library norm, recorded).

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition. The edge-press inertness, the simulation seam and
  the frame-size conversion are pinned by unit tests seen to fail under narrow sabotages.

## Screenshot verification after diagram-library adoption (diagram-library-adoption, task 12)

Every hand-built canvas now renders through the diagram library, behaviour- and
appearance-preserving by the spec's parity method. The six readme screenshots were assessed
against that: each shows a fit-to-view canvas with nothing selected, and no migration
changed what an unselected, fitted canvas draws - shapes, edges, labels and styling are
byte-for-byte the same stylesheets and geometry, and anchors stay css-hidden while nothing
is selected. The assessed conclusion is that no image became a false claim; what can drift
is framing, since the library's fit padding is uniform where some old canvases padded
differently.

- **Actions**: during the next manual pass, open each of the six screenshotted documents
  (docs/screenshots/readme.md's table), click Fit to View, and compare against the
  committed image; where framing visibly drifted, re-run `node capture.mjs` per the readme
  and commit the retakes.
- **Expected**: content, styling and selection state match every image; only fit framing
  may differ, and a retake is a script run away.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition (the capture script signs in with the developer
  placeholder itself).
## A canvas sizes to its pane, and does not grow while it is watched (diagram-library)

The library shapes the view to the pane's proportions, which means it measures the pane. That
measurement has one way to go wrong that no unit test can see: jsdom computes no layout, so
every automated check reads zero and the measurement never happens at all.

**The failure it guards.** The library's surface is `width: 100%; height: 100%`. A percentage
height against an *indefinite* height resolves to the svg's intrinsic size, and an svg takes that
from its viewBox's aspect ratio - so if the wrapper has no definite height, the measured height
comes from the viewBox, and the viewBox is then derived from the measured height. The canvas
grows every frame. Measured on the C4 container diagram at 1600x900: the surface went 641px tall,
then 1160, then 1903, while the diagram slid out of the window and the screenshot came out as an
empty canvas with only its title. The module had passed a class name with no rule behind it, and
nothing failed - the elements were all present in the DOM, just off-screen.

- **Preconditions**: backend + client running; `src/examples/` added as a project. A real
  browser: this cannot be seen in jsdom.
- **Actions**: open `diagrams/c4/industrial-plant/architecture/bottling-mes.dsl` ->
  `bottling-mes.mes-containers.adp`. Let it settle, click **Fit to View**, and wait a few
  seconds. Do the same for one canvas of each other shape - a timeline, a Wardley map, a causal
  loop diagram. Then resize the window and watch each again.
- **Expected**: the drawing appears and the canvas keeps the size of its pane. Nothing grows
  while you watch it, no scrollbar appears on the page itself, and Fit to View shows the whole
  diagram rather than an empty field. A canvas that is taller than its pane, or that grows each
  time it settles, is this defect.
- **Faster, and what found it**: `docs/screenshots/capture.mjs`'s sibling probe - open the
  diagram in headless Chrome at 1600x900 and print the surface's measured height three times, a
  second apart. Three equal numbers is the pass; a rising sequence is the bug.

- **Result 2026-09-06 (developer build (Debug, `developer` env, bypass session))**: **passed
  after the fix, and seen to fail before it.** The rising 641 / 1160 / 1903 above is the
  measurement against the defect; with `.library-canvas` carrying its own sizing the same probe
  reports 442 / 442 / 442 with all 24 elements inside the surface at every sample.

## wardley-map property grid re-check, and the shared stylesheet imports (defect pair)

Two unowned findings closed at unit level; each hands one eyes-on step over.

**One - the wardley property-grid FLAG** (developer-sign-in-bypass findings, 2026-09-05):
the missing element-selection wiring it names was closed by the wardley library migration
(landed 2026-09-06, merge 33aaeba0) - clicks now raise selection-changed and the module
forwards `elementSelectionOf(entryId, path, id)`; the guard test "reports a clicked element
as a nested selection" has now been seen to fail with the forwarding severed.

- **Actions**: re-run the FAILED entry "An element selected on the canvas fills the property
  grid (wardley-map, task 26)" against a build at or after 33aaeba0: open tea.adp, click a
  component, read the Property Grid.
- **Expected**: the grid heads with the element and shows its Identity / Position / Strategy
  rows; the backend log says the element id, not the .adp path. An evolve-target dot stays
  inert - pressing one neither selects nor deselects, by design.

**Two - five modules styled only by accident**: azure-pipeline, c4, helm-charts, mindmap and
wardley-map named `canvas-*` classes without importing `@client/canvas/canvas.css`; they
rendered styled only because `diagramCanvases.ts` eagerly globs every register file, so some
other module always loaded the sheet. The five registers now import it, and
`sharedStylesheetImports.test.ts` guards the convention (seen to fail naming all five).
jsdom applies no CSS, so no automated check can see the appearance itself.

- **Actions**: during the next manual pass, open one diagram from any of the five modules
  and confirm the shared styling (selection accents, connection lines, anchors) renders.
- **Expected**: identical appearance to before - the change makes each module's styling
  self-sufficient, not different.

- **Result 2026-09-06**: **written and not yet executed by eyes** - handed over runnable per
  the session's credential prohibition.

## A large diagram's drag keeps up with the pointer (drag-and-drop-centralization, task 5)

jsdom measures work done, not smoothness perceived - this is the real-browser half of the
measurement, on the two models the report named.

- **Steps**: run the app from the implementing worktree on the Developer's reserved ports
  (both port files changed and reverted afterwards - `src/client/vite.config.ts` and
  `src/backend/EtAlii.Adp.Backend.Service/appsettings.developer.json`), browsing the
  backend's port. Open `src/examples/diagrams/rdf/wikidata/marie-curie.ttl` and drag a
  resource card around for a few seconds; open `src/examples/diagrams/rdf/nobel/laureates.ttl`
  and do the same, including a card far from the one first selected. Pan both diagrams by
  dragging the background.
- **Expected**: on marie-curie the card rides the pointer with no visible lag, the drop
  commits (the validator re-runs, undo arms) and **another card still selects afterwards**;
  the pan moves the diagram and both scrollbar thumbs live. On laureates the view is
  truncated ("Showing 1000 of 1657 resources") and **edits are withheld by design**, so the
  reposition cannot commit there - pan and selection are what that model exercises. Escape
  mid-drag puts the card back and selects nothing; Escape mid-pan rolls the view back to
  where the pan began (this roll-back is deliberate: the pan is the gesture's transient
  visual until release).

- **Result 2026-09-06**: **run** from the worktree on backend 5091 / client 5191 via the
  developer session. marie-curie: drag committed and revalidated, selection-after-drag
  intact, pan moved view and thumbs; laureates: pan and selection fine on the 1,000-card
  truncated view, reposition withheld as the truncation banner states; zero console errors.
  The Escape checks were not driven interactively (the automation's drag is atomic) - they
  are pinned by DiagramCanvas.test.tsx's abandon pair instead. Perceived smoothness is
  quantified by the jsdom halves in the task-5 implementation log (before: 10.4-47.8ms per
  frame on the rdf shapes; after: 0.23-0.33ms, flat across model sizes).

## A cached `core.unknown-type` problem outlives the build that produced it (dotnet-dependency-graph, showcase report)

**A user reported this module's showcase as broken and it is not.** The diagram opens and draws
correctly; what was wrong was the problems panel, which carried a validation result from a build
in which the type did not yet exist — and carried it **without the `stale` marker other outdated
entries wear**. Three people, this Developer included, spent an evening diagnosing a module from
a message that was describing a previous build.

The reason it is here rather than in a unit test: **nothing below the running app has the
persistent problem cache in it.** The integration suites deliberately replace `IProblemStore`
with one rooted in a temp folder — the comment in `DiagramToolboxFlow.Tests.cs` says why, and it
is a good reason — so every automated test starts with an empty cache and cannot see an entry
that survives a restart. The cache lives in the real user profile, which is exactly what makes
the defect reachable and what makes it invisible to the suite.

- **Steps**: run the app from a worktree on the Developer's reserved ports (both port files
  changed and reverted afterwards — `src/client/vite.config.ts`,
  `src/backend/EtAlii.Adp.Backend.Service/appsettings.developer.json` and the
  `applicationUrl` in `Properties/launchSettings.json`), browsing the backend's port. Open the
  **Showcase** project, expand `diagrams / dotnet-dependency-graph / pipeline-toolkit`, expand
  `PipelineToolkit.slnx` — **the registration nests under the solution, so it is not a visible
  sibling** — and double-click `PipelineToolkit.adp`. Read the problems panel **before**
  pressing anything, then press **Validate**.
- **Expected**: the canvas draws four project boxes (`Pipeline.Core`, `Pipeline.Storage` on
  `net10.0, netstandard2.0`, `Pipeline.Cli`, `Pipeline.Core.Tests`) and three package boxes
  banded to their right, with **`Serilog` showing `3.1.1, 4.4.0`** — the version conflict the
  example exists to demonstrate. No package is hidden: four projects is below the ambient
  filter's five-dependent floor, so the "N packages are hidden" notice must **not** appear.
  The problems panel must carry **no** entry for this path. If it carries
  `'dotnet/dependency-graph' is not a known diagram type.` while the backend's startup log says
  `Discovered diagram type dotnet/dependency-graph`, that entry is cached from an older build:
  pressing Validate clears it and the count drops.
  **Since the fix, such an entry must also wear the `stale` marker** — a core verdict about
  routing is re-checked against the router when the panel is answered, so a cached
  "not a known diagram type" for a type the running build *does* know reads as stale rather
  than as a claim about now. **An unmarked one is now itself the defect**, and the thing to
  report: it would mean the entry is live rather than cached, which is a different bug from
  the one this step was written for.
- **Result 2026-09-08**: **run** from worktree `ddgfix` on backend 5091 / client 5191, on
  develop `a45f6f5b`, via the developer session (no sign-in form — see the note at the top of
  this file). The diagram drew all seven boxes with the `Serilog 3.1.1, 4.4.0` conflict and no
  hidden-package notice, exactly as expected. The panel **did** carry the stale
  `not a known diagram type` entry, unmarked, on a backend that had logged the discovery
  seconds earlier; **Validate cleared it and the error count went 3 → 2**, which is the
  evidence that it was cached rather than live. The reported "no projects ADP could resolve"
  did not reproduce at all, on this build or in any harness.
## Every shape family still draws itself, in a browser (declarative-diagram-modules, task 17)

Thirteen module canvases stopped drawing themselves and started declaring what they draw. jsdom
sees classes, attributes and text, which is what every migration test asserts; it does not see
a colour, a font, a silhouette or a line that ends up two pixels out. **Typography moved from a
stylesheet into declared properties**, and a size that is right in the DOM and wrong on screen
is exactly the kind of thing that survives 1,256 green tests.

One diagram per shape family, because a family is what a mistake would be common to.

- **Steps**: run the app from the implementing worktree on the Developer's reserved ports (both
  port files changed and reverted afterwards — `src/client/vite.config.ts` and
  `src/backend/EtAlii.Adp.Backend.Service/appsettings.developer.json`), browsing the backend's
  port. Open each of these and look at it, then select an element, press **F2**, and press
  **Escape**:
  1. **span** — `src/examples/diagrams/generic/dependencies/` (the authored dependency graph)
     and a `.slnx` through `dotnet/dependency-graph`, side by side: they are meant to be one
     drawing with different colours.
  2. **styled-box** — a C4 view: a person with a head above the box, a data store as a cylinder,
     and a card with its name, type line and description.
  3. **box with rows** — an RDF card (`rdf/wikidata/marie-curie.ttl`), an OWL ontology, a SHACL
     shapes file and a SKOS scheme: header, badge line, and one line per row.
  4. **centered-box** — a mind map, including a node with notes and a folded branch.
  5. **pill and a body-less badge** — a causal loop diagram: the polarity arc with its arrowhead
     turning the way the loop turns.
  6. **symbol over a declared backdrop** — a Wardley map: evolution bands, both axes with their
     end labels, an attitude region, an accelerator, a note and a numbered annotation.
  7. **rounded-rectangle with ornaments** — an Azure pipeline: a collapsed stage's job count, the
     status indicators along the top-right, and a problem mark where a problem is reported.
- **Expected**: every diagram looks as it did before the migration — same colours, same shapes,
  same text in the same places. Specifically:
  **band and axis labels grow and shrink with zoom** on the Wardley map while the bands
  themselves stay put (that is `scaleWithView`, and it is the one typography change that is
  meant to be visible); **the C4 card's three lines stay pinned below its top edge** as cards of
  different heights sit side by side; **a stage's name sits at the card's top-left, not centred**;
  **an RDF card's rows begin below its badge line** and do not overlap it; **F2 opens the editor
  over the text it replaces** — over the *name line* of a C4 card and an Azure stage, over the
  whole box on a databricks task, and at the label's own offset on a Wardley mark, including one
  whose document pushes the label to the left of the dot.
  Nothing may draw in black-on-black or vanish: an unstyled class renders with the SVG defaults,
  which is what the migration's class plumbing could get wrong without any test noticing.
- **Result**: run 2026-09-09 against `d86f84ca` in `.claude/worktrees/ddm`, backend 5091 /
  client 5191, signed in with the developer session, browsing the backend's port. **Two defects
  found, both fixed and re-checked in the running app at `e5937de8`**; everything else drew as
  it did before.
  - **A stage name drew centred.** `align` sets x and `offset` sets y - orthogonal - but an
    offset suppressed the alignment, so azure-pipeline's name, which states both, sat in the
    middle of a card whose name has been at the top left since the module was written. After
    the fix: `x="12"`, `text-anchor="start"`.
  - **Ornaments drew a card away.** A decoration's coordinates are centre-relative, which is
    what a fixed ornament means; a bound `bounds.*` is already a canvas position, so the centre
    was added twice. `anchor: "canvas"` says which, and five modules that read `bounds` now say
    it. After the fix: Ghost's problem mark at (208, 196) and Left's at (808, 76), on their own
    cards.
  - Verified as expected: dependency-graph's declared classes on the rect, and
    `dependency-graph-selected` only while selected; the C4 card's three lines 22/38/58 from
    the top on cards of differing height, with its bound fill `#08427b`; an owl-time card whose
    rows start below the badge line (label 4936, badges 4956, rows 4974/4990/5006/5022); the
    causal-loop arc's `marker-end`, its `causal-loop-reinforcing` badge and a pill whose
    `rx` is half its height; the Wardley map's four bands, both axes and `scaleWithView` measured
    across a zoom (view x1.5625, band width unchanged at 110.56, band label 20px -> 31.25px);
    and a mind map's centred text with all three corner glyphs - `•` notes, `↗` link, `⊕`
    folded - together on one node, its drop ring and dashed branch preview during a drag
    (`mindmap-drag-preview`, `mindmap-node-drop-target-ring`, `mindmap-edge-preview`).
  - F2 opened over the text it replaces every time: over a C4 name line and an Azure stage
    name, over the WHOLE box on a databricks task (editor box exactly the shape rect), and at
    a Wardley mark's own label offset - `annotations-and-labels.owm`'s Kettle, whose document
    pushes its label 57 units LEFT of the dot, opened its editor at that same x.
  - Nothing drew black-on-black: on each family's representative element the computed fill
    and stroke were read rather than eyeballed, and each came back a theme colour.
  - **Not covered by the corpus**: no vendored `.mm` has a folded branch or a note, so those
    two glyphs were produced by folding and annotating a node in the session and undoing both
    afterwards. The document was never saved.
  - **Instrument note, so the next run does not read it as a defect**: the browser tool cannot
    deliver a space keydown - it arrives as `key: ""` - so mindmap's declared `fold` shortcut
    looks inert when pressed. It is not: the same action off the context menu folds, and F2
    proves the declared-shortcut path. Exercise a space-keyed action through its menu entry.
  - **Left standing**: a box shape whose module declares its labels separately still emits one
    empty `<text>` inside the shape group (`BoxElement` always writes one). It paints nothing
    and is not a defect - recorded because it is otherwise re-discovered every pass.

## Every shipped example says what it means to say (showcase corpus, found in the app)

**Nothing was testing the showcase.** Where a module has example tests at all they read the
module's own copy under `src/diagrams/<module>/examples/`; the copy a reader actually opens,
under `src/examples/`, was read by no test in the repository until `ShippedExamplesTests`. It is
also the copy the running application writes to, so anything typed into a diagram and saved
lands there and is committed by whoever runs `git add` next.

That is not hypothetical. Two examples were carrying keyboard mash when this entry was written:
two nodes named `sdfsdf` under "Frustum tests" in the mindmap, and a link labelled `"sdfsdf"` in
the on-call causal loop - on the very link whose readme celebrates the tool correcting the
author. Both had been committed for weeks, and both were found by opening the diagram and
reading it.

**Half of this is now automated, and the split is the point.** `ShippedExamplesTests` sweeps the
196 documents ADP itself writes (`.mm`, `.cld`, `.owm`, `.adp`, `.tml`) across all sixteen
example folders - the showcase and every module's and editor's own copy - and was seen to fail
against both real instances before it was trusted. Its scope stops at those extensions
deliberately: the same sweep over every shipped file returned 451 hits and nearly all were
correct - `www` in a path, `SSS` in a log pattern, `foo` throughout the **vendored** Ansible
playbooks, which are upstream data nobody here may edit. The rest needs a person, and that is
what the steps below are.

**This was found, specified, fixed, and regressed**, which is why it is a guard and not a third
sweep. The archived `small-refinements` specification measured the mindmap's `sdfsdf` in its own
requirements document, Requirement 2 forbade it in all three trees, task 2.2 rewrote the maps
with real content in `e6b7407b` - and `24a3ca56` put two of those nodes straight back.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Steps**: open every diagram under `src/examples/diagrams/`, one per folder, and **read the
  text in it** - node names, link labels, titles. Not "does it draw": the drawing was already
  fine in both cases above. Then, for any file you find damaged, diff it against the module's
  copy under `src/diagrams/<module>/examples/` and restore **only the junk hunk**; do not copy
  the file wholesale, because the two are allowed to differ where a presentation choice was made
  in the app and saved (the causal loop examples carry one on purpose - `flipped` on three
  links).
- **Expected**: every string in every shipped example is something somebody meant to write. A
  node named after the button that made it, an empty node left over from a deletion, and a label
  that is prose but wrong are all failures of this step - and all three are invisible to the
  automated sweep, which is why the step exists beside it.
- **Also expected**: each diagram type's corpus has a readme, and that readme says what the
  corpus does **not** demonstrate. That sentence is the only warning a later reader gets that a
  feature has no example exercising it - the mindmap's readme now records that no map we ship
  has a folded branch, a note or an explicit position, which is why two of its three indicator
  glyphs cannot be produced from a shipped file at all.
- **Result**: 2026-09-09, partial and honest about which part. **The automated half ran across
  all 101 authored documents**: two failures, both named above, both fixed by restoring the hunk
  from the module copy, and the sweep was confirmed to find exactly those two on the unfixed tree
  and none on the fixed one. **The human read has not been done corpus-wide** - the mindmap and
  causal-loop examples were read in the app, the other fourteen diagram types were not. Whoever
  runs this next starts there.

## Every label stays inside its element, measured rather than read (label layout, 2026-09-10)

Reported by the user on three diagrams: text outside the elements on the databricks pipeline,
and a broken text layout on the C4 and SHACL cards. **Five causes, and the first is the one every
module shares**:

1. **A declared label's anchor lost to the stylesheet.** The layout computes an x *for* an
   anchor, and the renderer wrote that anchor as a `text-anchor` *attribute*. A presentation
   attribute loses to every author CSS rule, and `canvas.css` says
   `.library-element-label { text-anchor: middle }`, so every start- and end-aligned label was
   re-centred on its own left or right inset and drew half outside its box. This is why the
   task 17 entry above recorded the Azure stage name as fixed with `text-anchor="start"`: the
   attribute was right, and the screen was not. Fixed in `DiagramCanvas`'s declared labels: the
   anchor is now an inline style, and inline style outranks the stylesheet.
2. **Databricks lost its name's alignment in the migration.** The name was declared centred, and
   the leftover `.databricks-label { text-anchor: start }` then anchored it at the centre, so it
   ran out of the right edge. Its `align: "start"` is restored and the rule removed.
3. **C4 never wrapped a description**, not even before the migration. The backend sizes cards
   for a wrapped description, but the canvas drew it as one line.
4. **C4 drew a clamped card's type line whole.** A long technology ran past a 240 card.
5. **SHACL lost `shacl-row-path` in the migration**, so paths drew at the browser's 16px. Its
   combinator summaries have never been truncated. Library truncation also measured a side-aligned
   line against the whole box rather than the space from its inset, so asking for truncation
   would not have helped.

**The split, and why this entry exists.** jsdom sees the DOM and cascades injected `<style>`
rules, so the guards for causes 1-5 are unit tests, and each was seen to fail first:
`DiagramCanvas.declared.test.tsx` (anchor inline, and a computed anchor through an injected
stylesheet), `PipelineCanvas.test.tsx` and `JobCanvas.test.tsx` (one per declaration),
`C4Canvas.test.tsx`, `ShaclCanvas.test.tsx` and `labels.test.ts`. **What jsdom cannot do is
lay out text**: a width there is a character count times an estimate, so no unit test can say
whether real glyphs fit. That half is the browser's, and it is this entry.

- **Preconditions**: backend + client running from the worktree on your reserved ports, browsing
  the backend's port; `src/examples` open as a project.
- **Steps**: open each diagram below. In the browser's console, paste the function, then call
  `measure()` on each tab:
  ```js
  function measure() {
    const svg = document.querySelector("svg.library-canvas-surface");
    const box = (el) => { const b = el.getBBox(), m = el.getCTM();
      const p = [[b.x, b.y], [b.x + b.width, b.y + b.height]].map(([x, y]) => [m.a*x + m.c*y + m.e, m.b*x + m.d*y + m.f]);
      return { x0: Math.min(p[0][0], p[1][0]), x1: Math.max(p[0][0], p[1][0]), y0: Math.min(p[0][1], p[1][1]), y1: Math.max(p[0][1], p[1][1]) }; };
    const out = []; let labels = 0, mismatched = 0, overlaps = 0;
    for (const el of svg.querySelectorAll("g.library-element")) {
      const shapes = [...el.querySelectorAll("rect,path,polygon,ellipse,circle")].filter((s) => !s.closest("foreignObject"));
      if (!shapes.length) continue;
      const s = shapes.map(box).reduce((a, b) => ({ x0: Math.min(a.x0, b.x0), x1: Math.max(a.x1, b.x1), y0: Math.min(a.y0, b.y0), y1: Math.max(a.y1, b.y1) }));
      const texts = [...el.querySelectorAll("text")].filter((t) => t.textContent.trim()).map((t) => ({ t, b: box(t) }));
      for (const { t, b } of texts) {
        labels++;
        const declared = t.style.getPropertyValue("text-anchor");
        if (declared && declared !== getComputedStyle(t).textAnchor) mismatched++;
        if (b.x0 < s.x0 - 0.5 || b.x1 > s.x1 + 0.5 || b.y0 < s.y0 - 0.5 || b.y1 > s.y1 + 0.5) out.push(`outside: ${el.dataset.elementId} "${t.textContent}"`);
      }
      texts.forEach((a, i) => texts.slice(i + 1).forEach((b) => {
        if (Math.min(a.b.x1, b.b.x1) - Math.max(a.b.x0, b.b.x0) > 0.5 && Math.min(a.b.y1, b.b.y1) - Math.max(a.b.y0, b.b.y0) > 0.5) { overlaps++; out.push(`overlap: ${el.dataset.elementId} "${a.t.textContent}" / "${b.t.textContent}"`); }
      }));
    }
    return `labels ${labels}, anchor mismatches ${mismatched}, overlaps ${overlaps}\n` + out.join("\n");
  }
  ```
  1. `diagrams/databricks/lakehouse/databricks.pipeline.adp`
  2. `diagrams/c4/industrial-plant/architecture/`: all five `bottling-mes*.adp` views that draw
     elements (`mes-containers` and `plant-deployment` are the ones that failed)
  3. `diagrams/shacl/w3c-shacl/spec-examples.adp`
  4. `diagrams/azure-pipeline/example 1/edge-indentation.adp`, the task 17 stage name
- **Expected**: 0 anchor mismatches, 0 overlaps, and nothing `outside` except a C4 boundary's
  name, which is declared `placement: "above"` its dashed frame. A long C4 description wraps
  inside its card, a long type line and a long SHACL summary end in `…`, and SHACL row paths
  draw at 11px.
- **Before you trust a zero**: plant one. Move a label onto its neighbour in the same card
  (`setAttribute("x"/"y", …)`), check that `measure()` reports it, then put it back, including
  its inline `text-anchor`, because clearing the style hands the anchor straight back to the
  stylesheet.
- **Result**: 2026-09-10 in `.claude/worktrees/ll3`, backend 5093 / client 5193, developer
  session. **Before**: the pipeline's long names out of the right edge by up to 17 and every
  badge strip out of the left by 9. 8 of 13 SHACL labels outside (paths, cardinalities,
  targets). The Azure stage name at a computed `middle` under an attribute that said `start`,
  and out of its card. 13 of 23 mes-containers descriptions outside, on one line each. Three
  plant-deployment type lines 249-250 wide in 240 cards. **After**: pipeline
  11 labels, 0 outside; SHACL 13, 0 outside, paths 11px, summary cut; C4 views 0 outside apart
  from the two boundary names (mes-containers, order-service-components); Azure `start`
  inline and computed. There were 0 anchor mismatches and 0 overlaps everywhere, and the planted
  overlap was reported.
  - **Not covered**: `bottling-mes.batch-release.adp`, the dynamic view, draws no elements at
    all, only its title. So no text there was measured, and an empty canvas is a separate
    question from this one.
  - **Left standing**: a C4 name is not truncated. The backend clamps cards at 240, so a name
    over about 34 characters at 13px would still run out. No example has one.

## Selection and its two looks, on all sixteen canvases (centralized-selection, task 24)

The user's instruction named four diagram types whose selection worked - timeline,
dependency-graph, causal-loop and c4 - and four that did not: helm-charts,
dotnet-dependency-graph, azure-pipeline and ansible-structure. **That comparison is the
acceptance oracle, and it is recorded as such**: the four named broken must end up behaving
exactly like the four named working.

**Why this entry exists, and what it is the only evidence for.** Every canvas has unit tests
that mount it and assert what selection *does* (`expectLibrarySelection`, called from each
module's own canvas test), and a text guard that no module writes selection code
(`noModuleSelection.test.ts`). **jsdom applies no CSS**, so no unit test can say whether the
shared highlight is legible on a diagram's fills - including the colours a backend chooses,
which is why the look changed from a recoloured stroke to a ring drawn outside the shape
(Requirement 5.2) - nor whether *selected* and *would accept this connection* can be told apart
when both are on (Requirement 6.2). Those are this entry's, and Requirement 10.3 says so.

- **Preconditions**: backend + client running from the worktree on your reserved ports, browsing
  the backend's port; `src/examples` open as a project. **The Claude window must be in front**:
  a hidden pane reports a 0x0 viewport, stops painting, and a press then lands on nothing.
- **Steps**: open each diagram below, and on each: press an element, press a connection where the
  notation selects one, press empty canvas, and drag an element a short distance. Paste the
  function and call `probe()` on each tab; then **look at the canvas** for the three things the
  probe cannot judge.
  ```js
  async function probe() {
    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
    const canvas = [...document.querySelectorAll(".library-canvas")].find((c) => c.getBoundingClientRect().width > 10);
    const at = (el) => { const b = el.getBoundingClientRect(); return [b.x + b.width / 2, b.y + b.height / 2]; };
    const press = (t, x, y) => { const ev = (type, buttons) => new PointerEvent(type, { bubbles: true, cancelable: true, composed: true, pointerId: 1, pointerType: "mouse", isPrimary: true, button: 0, buttons, clientX: x, clientY: y });
      t.dispatchEvent(ev("pointerdown", 1)); t.dispatchEvent(ev("pointerup", 0)); };
    const sel = () => [...canvas.querySelectorAll(".canvas-selected")].map((e) => e.dataset.elementId ?? e.dataset.connectionId);
    const out = {};
    const els = [...canvas.querySelectorAll("[data-element-id]")].filter((e) => e.getBoundingClientRect().width > 2);
    const cons = [...canvas.querySelectorAll("[data-connection-id]")];
    const el = els[Math.min(1, els.length - 1)];
    press(document.elementFromPoint(...at(el)) ?? el, ...at(el)); await sleep(1200);
    const ring = canvas.querySelector("rect.library-selected-outline");
    const shape = ring?.closest("[data-element-id]")?.querySelector(".library-shape, rect:not(.library-selected-outline)");
    out.element = { selected: sel(), ring: !!ring, stroke: ring && getComputedStyle(ring).stroke, dashed: ring ? getComputedStyle(ring).strokeDasharray !== "none" : null,
      gapPx: ring && shape ? +(shape.getBoundingClientRect().x - ring.getBoundingClientRect().x).toFixed(1) : null };
    if (cons.length) { const hit = cons[0].querySelector(".canvas-connection-hit") ?? cons[0]; press(hit, ...at(hit)); await sleep(1200);
      const line = cons[0].querySelector(".canvas-connection-line");
      out.connection = { selected: sel(), stroke: line && getComputedStyle(line).stroke }; }
    const svg = canvas.querySelector("svg.library-canvas-surface"); const b = svg.getBoundingClientRect();
    press(svg, b.x + 3, b.y + 3); await sleep(1200); out.afterBackground = sel();
    const d = els[0]; const [x, y] = at(d); const ev = (type, buttons, dx) => new PointerEvent(type, { bubbles: true, cancelable: true, composed: true, pointerId: 1, pointerType: "mouse", isPrimary: true, button: 0, buttons, clientX: x + dx, clientY: y });
    const t = document.elementFromPoint(x, y) ?? d;
    t.dispatchEvent(ev("pointerdown", 1, 0)); t.dispatchEvent(ev("pointermove", 1, 40)); t.dispatchEvent(ev("pointerup", 0, 40)); await sleep(1200);
    out.afterDrag = sel();
    return JSON.stringify(out);
  }
  ```
  The sixteen, each from `src/examples/diagrams/<type>/`: `timeline`, `dependency-graph`,
  `causal-loop`, `c4` (**the oracle's four that worked**); `helm-charts`,
  `dotnet-dependency-graph`, `azure-pipeline`, `ansible-structure` (**the oracle's four that did
  not**); then `databricks`, `rdf`, `owl`, `shacl`, `skos`, `sparql`, `mindmap`, `wardley-map`.
- **Expected, from the probe**: `element.selected` names the pressed element and nothing else;
  `element.ring` true, its stroke the primary colour, not dashed; `connection.selected` names the
  pressed connection **and not the element pressed before it** - a press replaces; `afterBackground`
  empty; `afterDrag` unchanged from before the drag - **a drag does not select what it moves**.
  On `mindmap` and `wardley-map` a connection press is a background press by declaration
  (Requirement 2.4), so `connection.selected` is empty there and nowhere else.
- **Expected, by eye - the half no test can make**:
  1. The ring reads clearly against that diagram's own fills, **including c4's backend-chosen
     colours** and databricks' and azure-pipeline's cards.
  2. A selected connection's highlight runs **the whole route**, label and adornments included,
     not just one segment.
  3. Drag a connection from an anchor over a legal target: the target wears the **accept** ring -
     dashed, further out, its own colour - and if that target is also the selection, **both rings
     show at once**, one inside the other (Requirement 6.2).
- **Before you trust a zero**: plant one. With something selected, delete its ring in the
  console (`document.querySelector("rect.library-selected-outline").remove()`) and check the
  probe reports `ring: false`; then press again to bring it back. A probe that cannot see the
  ring's absence cannot report its presence.
- **The open question this pass settles**: the ring's offset is in canvas units, so it scales
  with zoom - at a zoomed-out fit it sat about 1px outside the box when this was written. Judge
  it at fit, at 100%, and zoomed in. If it does not read at fit, the remedy is a screen-constant
  offset (pixels converted to units per render) rather than a larger constant, which would gape
  when zoomed in.

## Both architecture pages' mermaid renders, in both readers (architecture-documentation, task 9)

**Why this is here rather than in a test.** The pages carry one mermaid block each, and nothing
in this repository renders mermaid. `module-client-api-readme` task 1 opens with a spike for a
mermaid-parse guard; **it has not landed** - checked 2026-09-23, no mermaid parser or renderer
exists anywhere under `src/`. Until it does, whether a diagram *parses* can only be answered by
a reader that draws it, and whether it is *readable* cannot be answered by a parser at all.
Delete this entry when that spike lands and the guard covers the parse half; the by-eye half
below stays either way.

- **Preconditions**: the two pages exist - `docs/architecture.md` (one `flowchart LR`) and
  `docs/solution-structure.md` (one `flowchart LR`). No running app is needed; this check is
  about documents, not the product.
- **Reader 1, the spec-workflow dashboard**: open the dashboard and view both pages through it.
  This is the reader that matters most, because it is where a session reads documentation.
- **Reader 2, the repository host**: view both pages on GitHub, which renders mermaid in
  markdown natively. A block can parse in one renderer and not the other; two readers is the
  point of the check, not redundancy.
- **Expected, per page and per reader**:
  1. The block renders **as a diagram**. A mermaid block that fails to parse degrades to its
     source text rather than erroring - so a wall of `flowchart LR` source IS the failure, and
     it is quiet.
  2. `architecture.md` shows three nodes: the browser client, the one ASP.NET Core process, and
     the workspace folder, with the two call legs labelled in opposite directions between the
     first two.
  3. `solution-structure.md` shows `src/` above **seven** children - api, backend, client,
     diagrams, editors, examples, TestSupport. Seven, not six: an earlier count said six and
     missed `TestSupport`.
  4. **Readable at the dashboard's own width** (Requirement 4.4) without horizontal scrolling
     or overlapping labels. Judge this in the dashboard at its natural width, not zoomed out
     and not in a maximised window - the width a reader actually has is the width that counts.
- **Before you trust a pass**: plant a failure once. Break one arrow (`-->` to `->`) in a local
  copy, confirm the block degrades to source text in both readers, and restore it. A check that
  has never seen the failure mode cannot report its absence - and this failure mode looks like
  ordinary text rather than like an error.

## Two tabs on one origin both keep working (two-tab-connection-wedge, task 2)

**This is a procedure rather than a test, and the task says so rather than pretending otherwise.** The
reproduction needs two real browser tabs sharing one browser profile on one origin, and no unit or
integration harness can produce a shared per-profile connection pool. CLAUDE.md's rule applies
directly: a bug only a running app can reproduce leaves a step-by-step entry here.

**An integration test written to stand in for this WILL pass, and that is worse than having none.** It
would measure one client, one pool and no contention, and then report health on the exact defect it
was written to catch - converting "unverified" into "verified" while nothing has been verified. This
repository has already produced three client tests that passed against the code they were written to
catch. **If you believe you have automated this, you have automated a different thing: say so and
leave this procedure in place.**

The mechanism: a browser allows about six concurrent HTTP/1.1 connections per origin, shared across
tabs in one profile. Each open document holds three server-streaming gRPC calls - `WatchHierarchy`,
`ContextService/Watch` and `DiagramService/Open`, which are the only server-streaming RPCs the whole
API declares - so two documents reach the cap and the next request is queued forever. Over TLS, ALPN
negotiates HTTP/2 and every request multiplexes onto one connection, which removes the cap rather
than raising it.

- **Preconditions**: a locally running developer build. **Note which scheme you are browsing** - the
  `before` half needs `http://localhost:5080` and the `after` half needs `https://localhost:5443`,
  and `launchSettings.json` serves both. An untrusted certificate is fine: click through the warning
  once per browser profile. Use **one** hostname throughout, and see the first trap below.
- **Actions**:
  1. Open the app and open a project and one document. Confirm it is healthy - a probe settles in
     milliseconds and the canvas draws.
  2. Open a **second tab on the same origin** - same scheme, same hostname, same port - and open a
     document there.
  3. In whichever tab you like, request a plain static asset on that origin.
- **Expected, over `http://`**: the second tab wedges, and **nothing on that origin completes again,
  including the static file in step 3.** That last part is the diagnosis: it is an origin-wide
  exhaustion, not a diagram fault.
- **Expected, over `https://`**: both tabs work, both keep receiving their diagram deltas, and the
  static file returns. Opening further documents in further tabs does not degrade either.
- **Two traps that made this hard to see, and a later tester will hit both.** `127.0.0.1` and
  `localhost` are **different origin keys**, so using one in each tab does not reproduce the defect
  and makes it look absent. And **the pool is per profile and shared across tabs**, so a "fresh tab"
  joins an already-exhausted pool rather than starting clean - every fresh-tab trial during the
  investigation was worthless for this reason. To get a genuinely clean pool, use a new browser
  profile or restart the browser.
- **Note**: do not try to confirm the cap from inside the page. A request queued by the browser and a
  request the server never answered are identical to page JavaScript; six attempts to tell them apart
  failed during the investigation and the seventh needed `netstat` from outside the browser.
- **Result**: not yet run. The `after` half is verifiable now that task 1 has landed an HTTPS
  endpoint; the ALPN half of it is already measured - `openssl s_client -alpn h2` against that
  endpoint reports `ALPN protocol: h2` over TLSv1.3, and the same connection asked for `http/1.1`
  only reports `http/1.1`, so the negotiation is real rather than assumed. **What remains unverified
  is the browser-level behaviour of two real tabs**, which is exactly the part no instrument here can
  stand in for.
