# Design Document

## Overview

Twelve requirements, twelve centralizations. Each one moves a decision the modules currently repeat into one place, and leaves behind a **structural guard** that fails when a new copy appears rather than when the data changes.

Two things shape every choice below.

**Where a thing is painted or decided must be the place a module cannot reach past.** The mindmap incident (`59f1ed3a`) and `centralized-selection`'s highlight amendment both landed on the same answer: a module stylesheet can reach any shape the library draws inside a module's group, so the library paints **inline**. This design applies the other half — the modules stop reaching — so the two together make the rule hold from both ends.

**A guard must be able to see what it claims.** jsdom cascades by source order alone, with no specificity and no `!important`, so a CSS-cascade fix cannot be proved by a unit test. Every guard below is therefore structural (it reads source, or it mounts and reads what rendered), and the two questions that only a browser can answer are named as such.

## Steering Document Alignment

`tech.md`'s rule that a guard is not a second copy of the data decides the shape of all twelve. `product.md`'s "a diagram type is a module, not an edit to the shell" decides the direction: every requirement removes something a module has to write, or something it can silently get wrong.

## Code Reuse Analysis

| Existing piece | Used how |
|---|---|
| `client/src/diagrams/useDiagramStream.ts` | Already the single stream loop after the bug fix (`179d197b`); Requirement 7's move call joins it |
| `client/src/themeTokens.test.ts` | Widened in place for Requirement 1 rather than duplicated |
| `client/src/canvas/library/testing/expectLibrarySelection.ts` | The pattern Requirement 10's shared fake follows: one helper, called from each module's own harness |
| `canvas/library/ringLooks.ts` → deleted by task 28 | Its lesson (paint inline) is the premise of Requirement 3 |
| `client/src/test-setup.ts` | Requirement 10's home for the jsdom stubs |
| `DiagramCanvas`'s inline paint | Requirement 3 leaves it as the only painter of library shapes |

## Architecture

### A. Tokens: one vocabulary, one guard (Requirement 1)

Every `var(--x)` in client CSS must resolve to a token the theme defines, or one defined in the same stylesheet. The four private namespaces are **mapped to the theme token of the same meaning**, which is the user's ruling:

| Namespace | Mapping |
|---|---|
| `azure-pipeline` `--adp-*` (11) | `--color-*` equivalents: surface, surface-sunken, border, border-strong, text, text-muted, accent, danger, warning |
| `dotnet-dependency-graph` `--canvas-*` (13) | the same, plus its note and warning fills |
| `helm-charts` `--helm-*` (12) | its six kind fills and strokes become declared element-type paint, not tokens |
| `causal-loop` `--vscode-*` (5) | `--color-surface`, `--color-text-muted`, and the two chart colours become theme tokens |
| `--font-sans`, `--font-mono` | defined once in the theme |
| `editors.css` `--accent`, `--panel-border`, `--warning-*` | `--color-*` equivalents |

**Confirmed in the browser, not argued from code** (audit, 2026-09-22): azure-pipeline paints `rgb(243,244,246)` surfaces and `rgb(209,213,219)` borders **in the dark theme**; causal-loop paints a `#252526` fill with a **black label on it in the light theme**, so the variable names are unreadable. These are the two visible changes Requirement 1 buys.

**Guard:** `themeTokens.test.ts` widens from `--color-*` to every custom property. Its canaries: a planted undefined token must fail it, and a module-local palette defined in its own sheet (`ansible-structure`'s `--ansible-play-hue-*`) must pass.

### B. One surface for refusals and status (Requirement 2)

**The library wraps each module's canvas where `DiagramPanel` mounts it** - option 1, chosen by the user in the Scrum master's chat on 2026-09-25. That wrapper draws the one refusal line and the loading, reconnecting and unavailable status, and provides a **per-canvas context**:

- **`executeAction`, `executeShortcut`, `setProperty` and `moveElementTo` report into it**: an attempt clears the line, a refusal shows it, and a click dismisses it - the clearing rule task 3 records;
- **`useDiagramStream` reports its state** into the same context, so loading, reconnecting and unavailable are drawn by the wrapper, replacing three styles across sixteen canvases;
- **a failed save arrives as the refusal of the call that wrote**, which is the client half of Architect 1's backend requirement that a save returns a result the caller inspects;
- **modules delete their rejection and status blocks and their early returns, and add no prop and no call - with one exception, ruled by the user: `useCanvasRefusal()`**, used only where a refusal starts **inside** a module rather than coming back from the backend. It returns `{ attempted(), refuse(message) }`, with its type inlined rather than exported, so it is a name to call and not a type to build on. It has two users: `causal-loop`, for a drop it refuses on the client before anything is sent, and `mindmap`, for its own re-parenting move, whose refusal the canvas used to discard.

The canvas with no `onActionRefused` handler — `ansible-structure` — needs none afterwards, and the three that task 6 gave one (`azure-pipeline`, `c4`, `mindmap`) need theirs no longer, because every refusal reaches the wrapper's line. `action-refused` **stays** in the contract for a module that wants to add something of its own, but nothing is required to listen.

**Guard:** mounted. Every registered canvas is rendered, a refusal is pushed, and the check fails if the message does not appear, if more than one surface shows it, or if a module declares a rejection or status element of its own.

### C. Modules stop reaching library shapes (Requirement 3)

A module stylesheet may select **its own declared classes**, and may not select an SVG element type by descendant. The 21 rules become declared classes on the shape, which the definition already supports (`classNames … on: "shape"`).

**Guard:** source-structural, one rule — no `rect|path|circle|ellipse|polygon|line|text` as a descendant in any module stylesheet. It **absorbs** `ringsSurviveModuleStyles`/`highlightSurvivesModuleStyles`, which is the same rule proved by rendering; the mounted one stays as the belt, this is the braces. **Developer 1's latent instance is the worked example in its doc-comment**: `wardley.css`'s `.wardley-annotation circle` also matches the library's own anchors and sets them to the colour they already have, so nothing is visibly wrong today — which is exactly why a structural guard is the right instrument. It is excluded in task 28's guard with that reason written beside it, and this requirement is where it is actually fixed.

### D. One character metric, one fit (Requirement 4)

`canvas/label/textMetrics.ts` (new) exports the metric and the fit:

- `AVERAGE_ADVANCE` — 0.55 of the font size, the backend's shared metric, replacing 7 in `labels.ts`, 7 in `labelPlacement.ts`, 8 in `DiagramCanvas`'s content sizing, 5.5 in `c4` and 6.2 in `rdf`'s OWL canvas;
- `widthOf(text, fontSize)` — the estimate, used where no measured width exists;
- `fitToWidth(text, width, fontSize)` — the ellipsis, replacing the formula written twice in the library and `c4`'s `typeLineFitted`.

**Where the backend computes the box** (c4 clamps a card at 240), the metric is Architect 1's and this module cites its fixture; the fit stays here. `c4`'s word wrap goes to Architect 1's `functional-decomposition-graph` `wrap`.

**Guard:** no file outside `textMetrics.ts` contains a character-width constant or an ellipsis-by-slice; plus a fixed-sample fact that the metric and the fit agree — a label sized to fit is not then trimmed.

### E. The backend keystroke comes from the declaration (Requirement 5)

The library derives the `ContextShortcut` from the declaration that fired the action: an `invokedBy: { kind: "shortcut" }` carries its key already, and a gesture-invoked action carries a **declared** backend key (`backendKey?: string` on `ActionDeclaration`) instead of a manufactured `"Delete"`. The 11 `BACKEND_KEYS` maps go.

**No wire change**, which is the user's ruling. `backendKey` is a new module-facing name → Architect 1's readme hears it.

**Guard:** no module constructs a `ContextShortcut` or maps an action id to a key.

### F. The library owns rename wiring (Requirement 6)

`useContextPrompt` moves inside `DiagramCanvas`, which already knows the selection the prompt applies to. The `editing` prop leaves the contract, so **typecheck refuses a module that still wires it** — the add-then-remove order `centralized-selection` task 21 used, and the reason this requirement is cheap.

### G. One move call (Requirement 7)

`useDiagramStream` returns `moveElementTo`. The 14 wrappers go, and with them `ansible-structure`'s swallowed error message. **This reverses archived `technical-debt-cleanup` R3.2 deliberately**, on the user's ruling of 2026-09-20, because the 14 bodies are one call with one signature: the reason that decision rested on has not held.

### H. Rows, ids and the seam (Requirements 8 and 9)

- **Rows:** `snapToStep` becomes module-facing and the two private `nearestRow` copies go. The rule's **owner is Architect 1** (the backend persists the row); this design cites its fixture, including the halfway and negative-zero cases five earlier copies got wrong.
- **Ids:** `canvas/selection/gestureIds.ts` (new) builds `new:x,y` and `rel:a->b`, and **checks a prefix before removing it**. `CausalLoopCanvas.tsx:359-360` strips `variable:` by length with no guard, so `variable:` yields an empty end and an unprefixed id is silently truncated. The **grammar and its parsers are Architect 1's**; this module cites its fixture and tests the empty and unprefixed cases.

**Guard:** ids are built only here; a prefix is never removed without a check.

### I. One canvas test harness (Requirement 10)

- the pointer-capture stubs move to `test-setup.ts` (20 copies, identical);
- `canvas/library/testing/canvasHarness.ts` (new) exports the pointer-event factory, the `pushedIds` adapter, and **one fake context connection with every member a `vi.fn()`** — replacing 20 partial fakes, whose drift is a canvas calling a member its own mock omits;
- `renderCanvas` **stays per module**: its props differ, so it is not a duplicate.

**Guard:** no canvas test declares its own copy of a shared helper.

### J. No rule without an emitter (Requirement 11)

A **mounted** guard renders every canvas on its shipped examples, collects the classes actually emitted, and fails on a stylesheet class nothing emits unless it is listed with a reason. This is the only instrument that can answer the question: `pipeline-problem-{payload.problemSeverity}` and `databricks-sim-{payload.simulated}` are composed at runtime from declared templates, so a text search reports them dead. Its first run removes `timeline`'s two confirmed-dead rules.

## Order (Requirement 12's cost, cheapest confidence first)

1. **Requirement 10** (test harness) — no product behaviour, and it makes every later requirement's tests smaller.
2. **Requirement 1** (tokens) — two confirmed visible defects, one widened guard.
3. **Requirement 2** (refusal and status surface) — the largest user-visible gain: four canvases stop swallowing refusals.
4. **Requirement 3** (stylesheets) — removes the cause the highlight amendment had to paint around.
5. **Requirement 5**, **6**, **7** (keystrokes, rename wiring, move call) — each deletes a whole class of module glue; 6 and 7 are self-enforcing through typecheck.
6. **Requirement 4** (text metric) — touches drawn output, so it follows the cheap ones.
7. **Requirements 8 and 9** (rows, ids) — wait on Architect 1's fixtures.
8. **Requirement 11** (dead CSS) — last, because its first run is a measurement whose findings are then removed.

## Testing Strategy

- **Structural guards** for Requirements 1, 3, 4, 5, 7, 9 and 10: they read source and fail naming each offender. Each carries the two canary shapes — a floor on files walked, and a named member present — so an empty walk cannot pass.
- **Mounted guards** for Requirements 2 and 11: they render every registered canvas, because what a module emits cannot be read from its source (declared templates) and what a rule paints cannot be read from a stylesheet (the cascade).
- **Typecheck** is the guard for Requirement 6 and half of 7: the prop leaves the contract.
- **Seen red first.** Every guard is run against a planted offender before it is accepted, and the two defects the scan already found are cited as the reds that prove the shape: the hot-loop stream (`179d197b`) and the silent refusals (`c9d7c72d`).
- **Browser-only, stated as such:** whether the theme mapping in Requirement 1 looks right in both themes, and whether the single refusal line is legible on every canvas's ground. jsdom applies no CSS, so it is not evidence for either.

## Deviations and notes

- **Requirement 2 leaves `action-refused` in the contract** although nothing must listen. Removing it would be a second, unrelated contract change, and a module may still want its own display.
- **Requirement 4 does not centralise word wrap**, which is Architect 1's `wrap`. Two owners for one seam would be the drift this specification exists to remove.
- **Not in this specification, and named so it is not mistaken for an oversight:** `dotnet-dependency-graph`'s dead selection (found in the audit, Developer 1's to fix), the task-26 edge offenders, and the connection-width drift (2.5px shared, 1px on ansible, 2px on c4) which task 28 sweeps.
