# Requirements Document

## Introduction

The user asked: *"Scan the codebase and find out what client side patterns are still duplicate and can be centralized. Create a specification for this."*

This specification is the result of that scan, measured on `develop` between 2026-09-15 and 2026-09-22. Its scope is the client: `src/client/src` and every `src/diagrams/*/client`. Architect 1 holds the backend twin, and the wire between them is a seam neither side changes alone.

**A candidate became a requirement only when three things were true**: every copy was named by file and line; the copies were shown to do the same *job* rather than merely to look alike; and the place they have already **drifted** was named. Drift is the argument. A pattern with many lookalikes and different contracts is not a duplicate, and is excluded below by name.

**What the scan already produced, before this document.** Three defects were found and, on the user's ruling, fixed ahead as bugs rather than waiting for this specification: nine hand-rolled stream loops that re-opened a cleanly closed stream with no delay and kept elements deleted while disconnected (`179d197b`), refusals that vanished silently on three canvases (`c9d7c72d`), and move refusals discarded on two more (`24dc9a8c`). They landed at `2a4dfb53` and `6a51791e` and are excluded here, cited rather than repeated.

## Alignment with Product Vision

`product.md` says a diagram type should be a module rather than an edit to the shell. Every requirement below removes something a module currently has to write, or something a module can silently get wrong, so that a new module is smaller and a wrong one is louder. `tech.md`'s rule that a guard is structural rather than a copy of the data it guards is why each requirement carries a guard that fails when a **new** copy appears, not a list of today's copies.

## Requirements

### Requirement 1 — Every colour a canvas paints comes from the theme

**User Story:** As a reader who switches to the dark theme, I want every diagram to follow it, so that no canvas keeps painting the light theme's greys.

#### Acceptance Criteria

1. WHEN a stylesheet or a component names a custom property THEN that property SHALL be defined by the theme, or defined in the same stylesheet that uses it, and **no canvas SHALL depend on a `var()` fallback for its colour**.
2. WHEN this is implemented THEN the four private namespaces SHALL be gone: `azure-pipeline`'s `--adp-*` (11 names, fallbacks such as `#f3f4f6`), `dotnet-dependency-graph`'s `--canvas-*` (13), `helm-charts`'s `--helm-*` (12), and `causal-loop`'s `--vscode-*` (5, which are VS Code's own token names and include a `#252526` fill). `--font-sans` and `--font-mono` (6 uses) and `editors.css`'s `--accent`, `--panel-border` and `--warning-*` SHALL be defined or replaced, and the library's own fallback to `var(--canvas-node-fill…)` (`DiagramCanvas.tsx:2553`) SHALL go with them.
3. WHEN each private token is replaced THEN it SHALL be replaced by **the theme token of the same meaning**, which is the user's ruling of 2026-09-20, and the resulting colour change SHALL be listed under *Permitted visible changes*.
4. WHEN the guard is written THEN `themeTokens.test.ts` SHALL be **widened from `--color-*` to every custom property**, so that the failure it already describes — a token the theme never defines, rendering its fallback in both themes and looking correct in review — cannot hide behind a different prefix. A module-local palette defined in its own sheet, as `ansible-structure`'s `--ansible-play-hue-*` is, SHALL pass.

### Requirement 2 — One place says what was refused, one says what the canvas is doing

**User Story:** As a user whose edit was refused, I want to be told, on whichever diagram I am looking at.

#### Acceptance Criteria

1. WHEN the backend refuses an action, a shortcut, a move or a save THEN the **library** SHALL show it, in one place, on every canvas — and **no module SHALL declare a rejection element of its own**. This replaces 15 module surfaces: 11 `.canvas-rejection` banners, `wardley-map`'s private `wardley-rejection` (`WardleyCanvas.tsx:662`), and the three added ahead by the bug fix.
2. WHEN a library-run menu action is refused THEN the message SHALL appear **without the module handling `action-refused`**. Four canvases have no handler today — `ansible-structure`, `azure-pipeline`, `c4` and `mindmap` — so a refusal is silent there; `databricks` is not among them, because its three views render `DatabricksCanvas`, which handles it.
3. WHEN a canvas is loading, reconnecting or unavailable THEN that SHALL be said by the library in one appearance, replacing the three styles in use today: seven canvases on the shared `canvas-host-message` and `canvas-status`, five with module-only message classes, three with their own loading and unavailable blocks, and `shacl` using `canvas-hint` for loading.
4. WHEN the guard is written THEN it SHALL **mount every registered canvas** and fail where a rejection or status element is declared by the module, or where more than one such surface exists.
5. WHEN a save fails THEN it SHALL reach this same surface, which is the client half of Architect 1's backend requirement that a save returns a result the caller must inspect.

### Requirement 3 — A module stylesheet never reaches a shape the library drew

**User Story:** As a maintainer, I want a module to style its own shapes without being able to repaint the library's.

#### Acceptance Criteria

1. WHEN a module stylesheet selects a shape THEN it SHALL select a **declared class**, and SHALL NOT select an SVG element type (`rect`, `path`, `circle`, `ellipse`, `polygon`, `line`, `text`) **by descendant**. There are 21 such rules today: `c4` 8, `sparql` 5, `mindmap` 3, `causal-loop` 2, `databricks` 1, `wardley-map` 2.
2. WHEN this is implemented THEN the incident behind it SHALL be impossible to repeat: `.mindmap-node rect` matched the library's own ring, because the ring is a `rect` in that group, and painted an opaque box over the label (`59f1ed3a`).
3. WHEN the guard is written THEN it SHALL fail on any such selector, and `ringsSurviveModuleStyles.test.tsx`, renamed `highlightSurvivesModuleStyles.test.tsx`, SHALL stay as its mounted half, which sees a ring hidden by means other than a type selector (the user's chat ruling, 2026-09-27).

### Requirement 4 — One character metric and one fit

**User Story:** As a reader, I want a label that is trimmed to fit to actually fit.

#### Acceptance Criteria

1. WHEN any code estimates how wide text will be THEN it SHALL use **one exported metric**. Today there are at least three values for one character: 7 px in `labels.ts:45` and `labelPlacement.ts:35`, **8 px in the library's own content sizing** (`DiagramCanvas.tsx:2925`) and 5.5 px in `c4` (`C4Canvas.tsx:51`). The one metric is the backend's shared one, characters × font size × 0.55, applied at the label's font size rather than as a fixed pixel count (the user's chat ruling, 2026-09-27). OwlCanvas's own fit (6.2 px per character, `OwlCanvas.tsx:494`) is a fifth copy and moves onto it.
2. WHEN text is trimmed to a box THEN it SHALL use **one fit function**, replacing the identical formula written twice inside the library (`labels.ts:51` and `SpanElement.tsx:145`) and `c4`'s own `typeLineFitted`.
3. WHEN this is implemented THEN a label SHALL NOT be sized by one metric and trimmed by another, which is the drift behind the recurring overflow reports on `databricks`, `c4` and `shacl`.
4. WHERE the backend computes the box a label must fit into THEN the metric is **Architect 1's**, and this specification cites its fixture rather than keeping a second copy. `c4`'s own word wrap (`descriptionLines`) belongs to Architect 1's `functional-decomposition-graph` `wrap` and is not re-implemented here.

### Requirement 5 — The keystroke the backend expects comes from the declaration

**User Story:** As the author of a diagram type, I want to declare a shortcut once.

#### Acceptance Criteria

1. WHEN a declared action must reach the backend's keystroke-keyed table THEN **the library** SHALL derive the keystroke from the declaration, and **no module SHALL build a `ContextShortcut`**. This removes 11 `BACKEND_KEYS` maps (`azure-pipeline`, `c4`, `databricks`, `dependency-graph`, `mindmap`, the four `rdf` readings, `timeline`, `wardley-map`).
2. WHEN an action is invoked by a gesture rather than a key — every module's `delete` — THEN its backend key SHALL be **declared**, not manufactured. A synthesised `"Delete"` is what `declarative-diagram-modules` said must disappear; it moved from the call sites into these maps rather than going away, and this criterion finishes that work.
3. WHEN this is implemented THEN **nothing on the wire SHALL change**, which is the user's ruling of 2026-09-15.
4. WHEN the guard is written THEN it SHALL fail on a module that maps an action id to a key.

### Requirement 6 — The library owns inline-rename wiring, as it owns selection

**User Story:** As the author of a diagram type, I want renaming to work without wiring it.

#### Acceptance Criteria

1. WHEN a canvas supports inline rename THEN the library SHALL wire it, and the `editing` prop SHALL leave the module-facing contract. Eight canvases write the identical `useContextPrompt` to `editing={{ editingId, onPropose, onSubmit, onCancel }}` glue today.
2. WHEN the prop is removed THEN **typecheck SHALL refuse a module that still wires it**, which is the add-then-remove order `centralized-selection` used for selection.
3. WHEN a module-facing name moves THEN Architect 1's `module-client-api-readme` SHALL be told in the same change.

### Requirement 7 — One move call

**User Story:** As a maintainer, I want an element's move sent in one place.

#### Acceptance Criteria

1. WHEN a canvas moves an element THEN it SHALL call **one shared move**, replacing 14 identical wrappers in `use*Stream.ts`.
2. WHEN a move fails THEN the backend's own sentence SHALL be reported. `ansible-structure` discards it today and substitutes "The position could not be saved.", which is the drift this removes and a listed visible change.
3. **This reverses an approved decision, deliberately.** Archived `technical-debt-cleanup` Requirement 3.2 kept the move call per module on purpose. The user reversed it on 2026-09-20, because all 14 bodies are the same unary call with the same arguments, so the reason that decision rested on no longer holds.
4. WHEN the guard is written THEN it SHALL fail on a module that defines a move wrapper of its own.

### Requirement 8 — One row-rounding rule, shared with the backend

**User Story:** As a user dragging an element onto a row, I want the client and the backend to agree where it lands.

#### Acceptance Criteria

1. WHEN a module needs the nearest row THEN it SHALL use the library's rule, and the two byte-identical private copies SHALL go (`DependencyGraphCanvas.tsx:166`, `TimelineCanvas.tsx:135`), which requires `snapToStep` to become module-facing.
2. WHEN the rule is exercised THEN it SHALL be checked against **Architect 1's fixture**, since the backend persists the row, and that fixture SHALL include the halfway and negative-zero cases that five earlier copies got wrong.
3. WHEN `snapToStep` becomes module-facing THEN Architect 1's readme SHALL be told, because it lists that name as library-internal today.

### Requirement 9 — One builder per id grammar

**User Story:** As a maintainer, I want the ids the client sends to be built in one place, to the grammar the backend parses.

#### Acceptance Criteria

1. WHEN a canvas builds a placement or gesture id (`new:x,y`, `rel:a->b`) THEN it SHALL use **one shared builder**, replacing the hand-written forms in about eight canvases.
2. WHEN an id is taken apart THEN the prefix SHALL be **checked before it is removed**. `CausalLoopCanvas.tsx:359-360` strips `variable:` by length with no `startsWith` guard, so an id of exactly `variable:` yields an empty end — which the backend now refuses — and an unprefixed id is silently truncated. Every other strip in the client guards (`databricksModel.ts:166`, `dotnetDependencyGraphModel.ts:173`, `selection.ts:62`).
3. WHEN the builder is tested THEN it SHALL be tested against **Architect 1's grammar fixture**, including an empty id and an unprefixed id, because the backend owns the grammar and its parsers.

### Requirement 10 — One canvas test harness

**User Story:** As a test author, I want the jsdom workarounds written once.

#### Acceptance Criteria

1. WHEN a canvas test needs jsdom's missing pointer capture THEN it SHALL come from `test-setup.ts`. The same line is copied into 20 test files today and `test-setup.ts` has none.
2. WHEN a canvas test builds a pointer event, or reads the ids a canvas pushed THEN it SHALL use a shared helper, replacing the factory in 18 files and the `pushedIds` adapter in 16.
3. WHEN a canvas test fakes the context connection THEN it SHALL use **one shared fake with every member**, replacing 20 partial fakes that each supply a different subset — the drift being a canvas that starts calling a member its own mock omits.
4. WHERE a helper is genuinely per module — `renderCanvas`, whose props differ — it SHALL stay, and this specification SHALL NOT count it as a duplicate.
5. WHEN the guard is written THEN it SHALL fail on a canvas test that declares its own copy of a shared helper.

### Requirement 11 — No stylesheet rule without something that emits it

**User Story:** As a maintainer, I want a dead rule to be found by a test rather than by reading.

#### Acceptance Criteria

1. WHEN a stylesheet declares a class THEN some canvas SHALL emit it, or it SHALL be listed with a reason.
2. WHEN this is checked THEN it SHALL be checked by **mounting every canvas on its shipped examples and collecting the classes actually emitted**. A text search cannot answer this: `pipeline-problem-{payload.problemSeverity}` (`PipelineCanvas.tsx:89`) and `databricks-sim-{payload.simulated}` (`DatabricksCanvas.tsx:95`) are composed at runtime from declared templates, and a search reports them dead.
3. WHEN the guard first runs THEN it SHALL name the rules that are dead today, `timeline`'s `.timeline-selected` and `.timeline-connect-target` among them, and they SHALL be removed rather than listed.
4. WHEN the guard is written THEN it SHALL also check the mirror direction: a class a canvas emits that no stylesheet rule claims SHALL be listed with a reason, or the guard SHALL fail. It is the same walk over the same two lists - the classes emitted and the classes styled - read the other way, so one guard serves both. The instance that asked for it: `library-canvas-surface` was emitted with no CSS rule, which left the svg `display: inline` on a text baseline and spilled about 4 px of descender into the scrolling parent on every canvas. `canvas-single-scrollbar` fixed that one instance (`b2b8ba03`, 2026-09-24) and completed without the guard that would find the next, so a direction assigned there on 2026-09-23 is moved here.

### Requirement 12 — What this work may change for a user

**User Story:** As the user, I want the only visible changes to be the ones written down here.

#### Acceptance Criteria

1. WHEN any change in this specification is made THEN the only permitted visible changes SHALL be these, and **everything else SHALL be invisible**:
   - theme-correct colours on `azure-pipeline`, `dotnet-dependency-graph`, `helm-charts`, `causal-loop` and the editors, mostly in the dark theme (Requirement 1);
   - refusals becoming visible on `ansible-structure`, `azure-pipeline`, `c4` and `mindmap`, and one appearance for the refusal line and the loading and unavailable text on all sixteen canvases (Requirement 2);
   - a label trimmed, wrapped or content-sized at a different length, because every client width estimate now uses the backend's shared metric, characters × font size × 0.55 (backend-centralization Requirement 10), at the label's own font size. This includes OWL's own trim, which the scan missed as a fifth copy (Requirement 4; the user's chat ruling, 2026-09-27);
   - `ansible-structure` reporting the backend's own sentence when a move fails (Requirement 7);
   - `causal-loop` refusing a link whose end is not a variable, with "Draw a link between two variables.", where it sent an empty or truncated id (Requirement 9);
   - `ansible-structure`'s play colours, per-kind outlines, hollow-role hatching, hover and focus, and `wardley-map`'s muted bold axis titles, which were written but matched nothing (the user's chat ruling, 2026-09-27, "Fix both");
   - `helm-charts`' per-kind node colours and its unreadable-node dashing, which matched nothing for the same reason, now painted from the theme's `--color-diagram-helm-*` (the user's chat ruling, 2026-09-27, "Map to theme");
   - monospace text in `ansible-structure`, `helm-charts` and `skos` taking the defined `--font-mono` stack, and `wardley-map`'s `--font-sans` declarations taking effect (Requirement 1);
   - an element whose type declares no colour painting `--color-surface` and `--color-text` rather than `#3b6ea5` on `#fff` (Requirement 1);
   - the refusal line clearing on the next gesture and dismissing on a click, and the text editor's save failure and loading text moving onto the library's line (the user's chat ruling, 2026-09-25).
2. WHEN a test changes THEN it SHALL change only where it pinned behaviour a named criterion above changes, it SHALL name that criterion, and it SHALL change only that far — the general rule `centralized-selection` Requirement 11.1 now carries.

## Non-Functional Requirements

### Code Architecture and Modularity
- **One owner per rule.** A rule computed on both tiers has a single owner and one fixture both suites read; this specification cites Architect 1's fixtures for rows, the text box, the id grammar, type strings and permanent error codes rather than keeping a copy.
- **Structural guards.** Every guard above fails when a *new* copy appears. A guard that lists today's copies would be a second copy of the data.

### Reliability
- No requirement here changes what crosses the wire. Anything that would is a seam item, named in both specifications and owned by the side that needs it.

### Excluded, each considered and named
- **Fixed ahead as bugs** (the user's ruling): the nine hand-rolled stream loops (`179d197b`), silent refusals on three canvases (`c9d7c72d`), discarded move refusals on two (`24dc9a8c`); landed at `2a4dfb53` and `6a51791e`.
- **The `applyDelta` folds** (13 modules, plus three that fold differently): one outer shape, but per-module payload contracts, so they are lookalikes rather than duplicates. `wardley-map` ignoring group deltas is documented intent (`wardleyModel.ts:139`), not drift. Low value, high cost.
- **Owned elsewhere:** `centralized-selection` tasks 21–27 and its highlight amendment, which takes the at-rest `--color-primary` uses, the module selected and connect-target rules, `ansible-structure`'s `:focus-visible` outline and the stale anchors comment; `declarative-diagram-modules`; `drag-and-drop-centralization`; `file-io-centralization`; `inline-rename-adoption`, except the glue in Requirement 6; `functional-decomposition-graph`; and `module-client-api-readme`'s name lists.
- **Architect 1's tier and the seam.**
