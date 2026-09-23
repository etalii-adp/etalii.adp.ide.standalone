# Tasks Document

One worktree for the whole specification (`.claude/worktrees/ccen`), one Developer owning it until every task is done. Every landing goes through the shared gate: `gate.sh` on the implementer's own scratch tree, then the printed `land.sh` line run by hand. Running the app for a look check happens from that worktree on the implementer's reserved ports, both port files reverted before merging.

**Two lessons from `centralized-selection`'s browser passes are conditions on the guards below, not commentary.**
1. **A guard over looks enumerates what the library can draw, never what the modules happen to declare.** The selection guard rendered one element type, so it could only see shapes some module used; walking the library's own list found `styled-box` and then `symbol`'s plain circle, which a canvas-by-canvas human pass had missed. **Guards here walk the code's own lists.**
2. **A look guard runs in both themes, or it states which one it checked.** A probe comparing a computed colour against one hard-coded token reported every light-theme canvas as broken while the highlight worked. **Resolve the token at measurement time.**

**Order.** The two defects a user can see come first, because they are Requirement 1's own evidence and the thing the user is looking at when they call the application inconsistent. Everything invisible follows, cheapest confidence first.

---

## Group 1 — What a user can see today

- [x] 1. `azure-pipeline` and `causal-loop` join the theme
  - Files: `src/diagrams/azure-pipeline/client/azure-pipeline.css`, `src/diagrams/causal-loop/client/causal-loop.css`, `src/client/src/index.css`
  - **Measured in the browser, both on develop:** azure-pipeline paints stage fill `rgb(243,244,246)` and border `rgb(209,213,219)` **in the dark theme** — the `--adp-surface-sunken: #f3f4f6` and `--adp-border: #d1d5db` fallbacks; causal-loop paints variable fill `rgb(37,37,38)` (`#252526`, the `--vscode-editorWidget-background` fallback) with a **black label on it in the light theme, contrast 1.37:1, unreadable**.
  - Map each private token to **the theme token of the same meaning** (the user's ruling): azure's **ten** `--adp-*` (18 `var()` occurrences across ten distinct names), causal-loop's five `--vscode-*`. Where the theme has no equivalent, define one rather than keep a private name — **and name it `--color-*`**, because task 2 widens `themeTokens.test.ts` to walk every custom property and a new private prefix would defeat the guard that this task's own work is meant to satisfy. *Corrected on 2026-09-23: this said **eleven**. Developer 4 counted ten while writing the guard, and a re-count confirmed ten — `accent`, `accent-subtle`, `border`, `border-strong`, `danger`, `surface`, `surface-sunken`, `text`, `text-muted`, `warning`.*
  - **Checked in both themes**, by resolving each token at measurement time; the light-theme label contrast on causal-loop is the acceptance number and it must clear 4.5:1.
  - _Requirements: 1.1, 1.2, 1.3, 12.1_

- [ ] 2. The rest of the private namespaces, and the guard that keeps them out
  - Files: `dotnet-dependency-graph.css` (`--canvas-*`, 13), `helm-charts.css` (`--helm-*`, 12), `editors.css` (`--accent`, `--panel-border`, `--warning-*`), `--font-sans`/`--font-mono`, `DiagramCanvas.tsx:2553`'s own `var(--canvas-node-fill …)` fallback, `src/client/src/themeTokens.test.ts`
  - **Widen `themeTokens.test.ts` from `--color-*` to every custom property**: each `var(--x)` resolves to a theme token or to one defined in the same stylesheet. **Canaries:** a planted undefined token fails it, and `ansible-structure`'s locally defined `--ansible-play-hue-*` palette passes.
  - **Seen red first** against today's tree, which has at least 44 undefined names.
  - _Requirements: 1.1, 1.2, 1.4, 12.1_

- [ ] 3. One surface for refusals, one for status
  - Files: `DiagramCanvas.tsx`, `canvas.css`, the fifteen module rejection surfaces, the three module loading/unavailable blocks
  - The library shows a refused action, shortcut, move and **failed save** in one place, and the loading, reconnecting and unavailable states in one appearance. **No module declares a rejection or status element.** The four canvases with no `onActionRefused` handler — `ansible-structure`, `azure-pipeline`, `c4`, `mindmap` — need none afterwards; `action-refused` stays in the contract for a module that wants its own display.
  - **Guard: mounted, over every registered canvas** — push a refusal and fail if no message appears, if more than one surface shows it, or if a module declares one.
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 12.1_

---

## Group 2 — The causes behind the looks

- [ ] 4. A module stylesheet stops reaching library-drawn shapes
  - Files: the 21 descendant rules in `c4.css` (8), `sparql.css` (5), `mindmap.css` (3), `causal-loop.css` (2), `databricks.css` (1), `wardley.css` (2); the declared `classNames … on: "shape"` replacements
  - **The live instance this prevents:** `.mindmap-node rect` matched the library's own ring and painted an opaque box over the label (`59f1ed3a`). **The latent one Developer 1 found:** `.wardley-annotation circle` also matches the library's own anchors, setting them to the colour they already have — invisible today, which is exactly why a structural guard rather than an eye is the instrument.
  - **Guard: no module stylesheet selects an SVG element type by descendant.** It absorbs `highlightSurvivesModuleStyles`, which stays as the rendered half.
  - _Requirements: 3.1, 3.2, 3.3, 12.1_

- [ ] 5. One character metric and one fit
  - Files: `canvas/label/textMetrics.ts` (new), `labels.ts:45-51`, `labelPlacement.ts:35`, `DiagramCanvas.tsx:2925`, `SpanElement.tsx:145`, `C4Canvas.tsx:51`
  - One exported metric replaces 7, 7, **8** and 5.5 px per character; one fit function replaces the formula written twice in the library and `c4`'s `typeLineFitted`. **A label must not be sized by one metric and trimmed by another** — the drift behind the overflow reports on `databricks`, `c4` and `shacl`.
  - **Where the backend computes the box** (c4 clamps at 240) the metric is Architect 1's and this task cites its fixture. **`c4`'s word wrap is not re-implemented here**; it belongs to `functional-decomposition-graph`'s `wrap`.
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 12.1_

- [ ] 6. The backend keystroke comes from the declaration
  - Files: `definition/actions.ts` (`backendKey?`), `DiagramCanvas.tsx`, the 11 `BACKEND_KEYS` maps
  - The library derives the `ContextShortcut` from the declaration that fired the action; a gesture-invoked action carries a **declared** backend key instead of a manufactured `"Delete"` — the synthesised keystroke `declarative-diagram-modules` said must disappear. **Nothing on the wire changes.** `backendKey` is a new module-facing name → tell Architect 1 in the same change.
  - **Guard: no module constructs a `ContextShortcut` or maps an action id to a key.**
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 12.1_

- [ ] 7. The library owns inline-rename wiring
  - Files: `DiagramCanvas.tsx`, the 8 identical `editing={{ … }}` wirings
  - `useContextPrompt` moves inside the library, which already knows the selection the prompt applies to; the `editing` prop leaves the contract, so **typecheck refuses a module that still wires it**.
  - _Requirements: 6.1, 6.2, 6.3, 12.1_

- [ ] 8. One move call
  - Files: `useDiagramStream.ts`, the 14 `moveElementTo` wrappers
  - The hook returns `moveElementTo`; the wrappers go, and with them `ansible-structure`'s swallowed message — **the backend's own sentence is reported**, which is this specification's one other permitted visible change.
  - **Record the reversal in the commit**: archived `technical-debt-cleanup` R3.2 kept the call per module deliberately, and the user reversed it on 2026-09-20 because all 14 bodies are the same call.
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 12.1_

---

## Group 3 — The seams, and the last sweep

- [ ] 9. One row-rounding rule, shared with the backend
  - Files: `DiagramCanvas.tsx` (`snapToStep` exported), `DependencyGraphCanvas.tsx:166`, `TimelineCanvas.tsx:135`
  - The two byte-identical `nearestRow` copies go. **Architect 1 owns the rule** (the backend persists the row); this task cites its fixture, **including the halfway and negative-zero cases** five earlier copies got wrong, and tells Architect 1 that `snapToStep` is now module-facing.
  - _Requirements: 8.1, 8.2, 8.3_

- [ ] 10. One builder per id grammar
  - Files: `canvas/selection/gestureIds.ts` (new), the ~8 canvases building `new:x,y` and `rel:a->b`, `CausalLoopCanvas.tsx:359-360`
  - **The defect this removes:** causal-loop strips `variable:` by length with no `startsWith` guard, so an id of exactly `variable:` yields an empty end and an unprefixed id is silently truncated. Every other strip in the client guards.
  - **The grammar and its parsers are Architect 1's**; this task cites its fixture and tests the empty and unprefixed ids.
  - _Requirements: 9.1, 9.2, 9.3_

- [ ] 11. One canvas test harness
  - Files: `test-setup.ts`, `canvas/library/testing/canvasHarness.ts` (new), the 18–20 copies across canvas tests
  - The pointer-capture stubs move to `test-setup.ts`; the pointer-event factory, the `pushedIds` adapter and **one fake context connection with every member** replace 20 partial fakes. `renderCanvas` **stays per module** — its props differ, so it is not a duplicate.
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

- [ ] 12. No stylesheet rule without an emitter
  - Files: a new mounted guard; `timeline.css`'s two confirmed-dead rules
  - **Mount every canvas on its shipped examples, collect the classes actually emitted**, and fail on a stylesheet class nothing emits unless it is listed with a reason. **A text search cannot answer this**: `pipeline-problem-{payload.problemSeverity}` and `databricks-sim-{payload.simulated}` are composed at runtime from declared templates and read as dead.
  - The first run's findings are **removed rather than listed**.
  - **The mirror direction is NOT this task's** — *a class that IS emitted and that no rule anywhere claims* belongs to `canvas-single-scrollbar`, by the Scrum master's ruling of 2026-09-23, so that neither specification claims the same guard. It is the same walk over the same two lists in the opposite direction, and it is worth knowing it exists: `library-canvas-surface` is emitted with no CSS rule at all, which left the svg `display: inline` on a text baseline and spilled ~4px of descender into the scrolling parent **on every canvas in the product**. Whoever implements this task should read that one first; one guard could serve both, and if it is written here instead, the other specification drops it rather than both carrying it.
  - _Requirements: 11.1, 11.2, 11.3_

- [ ] 13. Gate, land, and check the record against the code
  - Four gates on the merged tree; both port files reverted; **the coverage diff re-run against files and strings rather than task claims**; and the permitted-visible-change list checked against what actually changed — the two theme fixes, the refusal surface, a trimmed label's character, and ansible's move message. **Anything else visible is a defect, not a side effect.**
  - _Requirements: 12.1, 12.2_

---

## Requirement coverage

Every acceptance criterion in the approved requirements was checked against every task claim above, mechanically, in both directions. **Forty-three criteria** (4, 5, 3, 4, 4, 3, 4, 3, 3, 5, 3 and 2 across Requirements 1 to 12), all claimed, and no claim names a criterion that does not exist.

**No criterion is unclaimed.** Requirement 12 is the specification's own bar rather than a piece of work: 12.1 is claimed by every task that may change something visible, and 12.2 by task 13, which re-runs the diff against the code and judges the visible changes against the list. A claim is a promise, not proof — task 13 is where each is traced to a file and a string.
