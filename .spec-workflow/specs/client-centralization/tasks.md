# Tasks Document

One worktree for the whole specification, one Developer owning it until every task is done. *The name `.claude/worktrees/ccen` was specified here and is withdrawn on 2026-09-23: a session is bound to its own worktree and the harness refuses writes into another, so Developer 4 could create that directory but not work in it, and cut `claude/client-centralization` in its own tree instead. **A tasks document cannot enforce which directory a session works in and should not promise one.** The constraint this sentence carries - one worktree for the specification, one owner - is unaffected.* Every landing goes through the shared gate: `gate.sh` on the implementer's own scratch tree, then the printed `land.sh` line run by hand. Running the app for a look check happens from that worktree on the implementer's reserved ports, both port files reverted before merging.

**Two lessons from `centralized-selection`'s browser passes are conditions on the guards below, not commentary.**
1. **A guard over looks enumerates what the library can draw, never what the modules happen to declare.** The selection guard rendered one element type, so it could only see shapes some module used; walking the library's own list found `styled-box` and then `symbol`'s plain circle, which a canvas-by-canvas human pass had missed. **Guards here walk the code's own lists.**
2. **A look guard runs in both themes, or it states which one it checked.** A probe comparing a computed colour against one hard-coded token reported every light-theme canvas as broken while the highlight worked. **Resolve the token at measurement time.**

**Order.** The two defects a user can see come first, because they are Requirement 1's own evidence and the thing the user is looking at when they call the application inconsistent. Everything invisible follows, cheapest confidence first.

---

## Group 1 — What a user can see today

- [x] 1. `azure-pipeline` and `causal-loop` join the theme
  - Files: `src/diagrams/azure-pipeline/client/azure-pipeline.css`, `src/diagrams/causal-loop/client/causal-loop.css`, `src/client/src/index.css`
  - **Measured in the browser, both on develop:** azure-pipeline paints stage fill `rgb(243,244,246)` and border `rgb(209,213,219)` **in the dark theme** — the `--adp-surface-sunken: #f3f4f6` and `--adp-border: #d1d5db` fallbacks; causal-loop paints variable fill `rgb(37,37,38)` (`#252526`, the `--vscode-editorWidget-background` fallback), and **its variable label - the label node carrying `canvas-node-label` - computes `rgb(15,23,42)` on it in the light theme, contrast 1.17:1, unreadable**. *Corrected on 2026-09-23, and it was wrong twice: the label is painted from `--color-text`, not black, and the true ratio is worse than recorded. The black came from **my probe taking a text node from the element group without scoping to the label class** - every declared element also renders an **unclassed, EMPTY text node** (`DiagramCanvas.tsx:2449` passes `""` when a type declares `labels`), eight per canvas here, inheriting black and painting nothing. Developer 4 read the stylesheet, got 1.17, and reported the conflict rather than reconciling it. **The two instruments never disagreed - one was reading a different population.***
  - Map each private token to **the theme token of the same meaning** (the user's ruling): azure's **ten** `--adp-*` (18 `var()` occurrences across ten distinct names), causal-loop's five `--vscode-*`. Where the theme has no equivalent, define one rather than keep a private name — **and name it `--color-*`**, because task 2 widens `themeTokens.test.ts` to walk every custom property and a new private prefix would defeat the guard that this task's own work is meant to satisfy. *Corrected on 2026-09-23: this said **eleven**. Developer 4 counted ten while writing the guard, and a re-count confirmed ten — `accent`, `accent-subtle`, `border`, `border-strong`, `danger`, `surface`, `surface-sunken`, `text`, `text-muted`, `warning`.*
  - **Checked in both themes**, by resolving each token at measurement time. **The acceptance number names its element: the `canvas-node-label` on a causal-loop variable, against that variable's fill, SHALL clear 4.5:1 in BOTH themes** - measured 1.17:1 light before the fix, and 17.85 light / 13.35 dark after mapping the fill to `--color-surface`. **A ratio alone is not sufficient as the assertion**: the guard SHALL first require that every declared label class is painted from a `--color-*` token, and only then take the ratio. *Why that order (Developer 4's framing, which survives the correction above): an unclassed or inherited black is **a state to forbid, not a colour to measure** - "clears 4.5:1 in both themes" is unsatisfiable for black, since it fails against every dark surface - and the ordering also catches the eight unclassed black text nodes per canvas that mis-measured this very criterion.*
  - _Requirements: 1.1, 1.2, 1.3, 12.1_

- [x] 2. The rest of the private namespaces, and the guard that keeps them out
  - Files: `dotnet-dependency-graph.css` (`--canvas-*`, 13), `helm-charts.css` (`--helm-*`, 12), `editors.css` (`--accent`, `--panel-border`, `--warning-*`), `--font-sans`/`--font-mono`, `DiagramCanvas.tsx:2553`'s own `var(--canvas-node-fill …)` fallback, `src/client/src/themeTokens.test.ts`
  - **Widen `themeTokens.test.ts` from `--color-*` to every custom property**: each `var(--x)` resolves to a theme token or to one defined in the same stylesheet. **Canaries:** a planted undefined token fails it, and `ansible-structure`'s locally defined `--ansible-play-hue-*` palette passes.
  - **Seen red first** against the tree as it stood on 2026-09-23, before this task's own fixes: **32 distinct undefined names across six files** - `dotnet-dependency-graph` 13 `--canvas-*`, `helm-charts` 12 `--helm-*`, `editors.css` 4, `--font-sans`/`--font-mono` 2, and `DiagramCanvas.tsx`'s own inline `var()` strings. *Corrected on 2026-09-23: this said **at least 44**, which counted a different population - it included azure's and causal-loop's names, which task 1 owns and has now fixed, and omitted `editors.css`'s four. 44 - 16 + 4 = 32, and the two figures reconcile exactly; Developer 4's walk is not short. **The gap was never occurrences against names - it was which files were in scope.***
  - **The library had come to depend on a module's private palette, which is this specification's own subject turned inward:** `DiagramCanvas.tsx` defaulted any element whose type declares no colour to `var(--canvas-node-fill, #3b6ea5)` and `var(--canvas-node-label, #ffffff)` - **`dotnet-dependency-graph`'s namespace, reached from the library.** It now defaults to `--color-surface` and `--color-text`, which `canvas.css` already states an undeclared node and label are. *Found by Developer 4 while renaming: of the fourteen `--canvas-*`, eight became theme tokens and six stayed local as `--dependency-*`, on the user's ruling of option 3 and the Scrum master's test - **a name is semantic if a different module would want the same token for the same reason.** Each assignment is recorded one line each, because a judgement that leaves no trace reads to the next person as a measurement.*
  - **A local palette SHALL declare both colour schemes or neither.** Declaring one satisfies *the name is defined* while leaving the defect exactly where it was: `dotnet-dependency-graph`'s names were undefined **and** its literals were dark, so a one-mode fix would have moved the phantom rather than closed it. **`--font-sans`/`--font-mono` are exempt** - a type stack is the same in both modes and saying so twice is noise.
  - **A fallback chain can simulate a design system, and the module's own documentation will vouch for it.** `helm-charts` read fifteen `--helm-*` names, declared three, and reached the twelve between them as hard-coded literals through a `var()` indirection - while its header comment claimed a per-kind palette *"which keeps a theme change a stylesheet change rather than a protocol change"*. **True of the mechanism, false of the values: a reader checking whether helm had a palette would have found a sentence saying yes.**
  - **`dotnet-dependency-graph` is task 1's defect inverted, and it makes Requirement 1 two defects rather than one:** its fallbacks are **dark** (`#3b6ea5` nodes, `#24303d` note ground, `#ffffff` labels), so it renders dark-looking **in the LIGHT theme** where azure rendered light-looking in the dark one.
  - **Two findings a colour-only guard cannot reach, which is the argument for walking every property:** `wardley-map` has seven `font-family: var(--font-sans)` with **no fallback at all**, so those declarations are **invalid at computed-value time** and have never done anything - a void declaration rather than a wrong colour; and **`helm-charts` had no palette** - fifteen names read, three declared, the twelve between them hard-coded literals reached through a `var()` indirection, so **a fallback chain simulated a design system** and a reader would have built on something that did not exist.
  - _Requirements: 1.1, 1.2, 1.4, 12.1_

- [x] 3. One surface for refusals, one for status
  - Files: `DiagramCanvas.tsx`, `canvas.css`, the sixteen module rejection surfaces across thirteen modules, and the status blocks of four modules plus the shell's own
  - **Runs after tasks 8 and 6, in that order, and cannot run first.** Five sources feed the sixteen module rejection surfaces, and only one of them reaches the library today (counted before tasks 8 and 6): `onActionRefused` (12 surfaces). Moves (14 canvases), shortcuts (11), actions (10) and the failed save (1) each reach only a module's own surface - counted by canvas and by reading every call line, since eight of the shortcut and action calls are awaited and three canvases route theirs through a module-local `surfaceRefusal`, which a pattern for the awaited form cannot see. Task 8 consolidates moves into the one `useDiagramStream` call so a single site can route them to the canvas surface; it does **not** move them into the library, so that client-internal channel is still owed afterwards. Task 6 moves shortcuts into the library. Run task 3 first and every module surface it deletes drops its refusals silently, which is the defect this specification exists to end. (Ordering ruled by the user in the Scrum master's chat, 2026-09-25.)
  - **When a refusal stops being shown - Requirement 2 does not say, so the user ruled it (the Scrum master's chat, 2026-09-25): the library's surface clears on any new gesture that reaches the backend - a move, a command, a shortcut or a drop - and is dismissed by a click.** Today the sixteen canvases use three rules and one extra: before the next move on eleven (`ansible-structure`, `c4`, `databricks`, `dependency-graph`, `owl`, `rdf`, `shacl`, `skos`, `sparql`, `timeline`, `wardley-map`), before any command on three through a module-local `surfaceRefusal` (`azure-pipeline`, `c4`, `mindmap`), only when clicked away on three (`causal-loop`, `dotnet-dependency-graph`, `helm-charts`), and `timeline` also before a resize - `c4` and `timeline` each appear twice. The ruling is the superset, so no canvas loses a way its message clears, and a refusal comes to mean *your last attempt failed* rather than *something failed at some point*. It also ends a live defect: on the eleven that clear only before a move, a refused shortcut is not cleared by a later successful one, so a stale refusal stays visible through successful commands until something is dragged. This is a visible change.
  - The library shows a refused action, shortcut, move and **failed save** in one place, and the loading, reconnecting and unavailable states in one appearance. **No module declares a rejection or status element.** The canvas with no `onActionRefused` handler — `ansible-structure` — needs none afterwards, and the three that task 6 gave one (`azure-pipeline`, `c4`, `mindmap`) need theirs no longer; `action-refused` stays in the contract for a module that wants its own display.
  - **Guard: mounted, over every registered canvas** — push a refusal and fail if no message appears, if more than one surface shows it, if a module declares one, **and fail if the surface that appears is not the library's.** The fourth condition is not redundant: *more than one* catches a module that **adds** to the library's surface, and there are three that **replace** it — `dotnet-dependency-graph`, `helm-charts` and `wardley-map` name their own rejection class and do not compose `canvas-rejection` at all, where the other thirteen surfaces do. Against those three one surface appears, the count is right, and it is the wrong surface, so a guard counting only surfaces is green on precisely the cases the task exists for.
  - **Four modules declare a status element, not three, and the counts here are by class token rather than by substring.** `azure-pipeline`, `mindmap` and `c4` each declare a loading and an unavailable class; `rdf` declares `shacl-loading` at `ShaclCanvas.tsx:324` with **no unavailable counterpart**. Outside the diagram modules the shell has `text-editor-loading`/`-unavailable`, `explorer-tree-loading`, `explorer-tree-loading-children`, `explorer-tree-node-unavailable`, `choice-tree-row-unavailable`, `property-grid-row-unavailable` and a bare `unavailable`. Where this list and the rule disagree, **the rule governs**: no module declares a rejection or status element.
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 12.1_

---

## Group 2 — The causes behind the looks

- [x] 4. A module stylesheet stops reaching library-drawn shapes
  - Files: the 21 descendant rules in `c4.css` (8), `sparql.css` (5), `mindmap.css` (3), `causal-loop.css` (2), `databricks.css` (1), `wardley.css` (2); the declared `classNames … on: "shape"` replacements
  - **The live instance this prevents:** `.mindmap-node rect` matched the library's own ring and painted an opaque box over the label (`59f1ed3a`). **The latent one Developer 1 found:** `.wardley-annotation circle` also matches the library's own anchors, setting them to the colour they already have — invisible today, which is exactly why a structural guard rather than an eye is the instrument.
  - **Guard: no module stylesheet selects an SVG element type by descendant.** It absorbs `highlightSurvivesModuleStyles`, which stays as the rendered half.
  - _Requirements: 3.1, 3.2, 3.3, 12.1_

- [x] 5. One character metric and one fit
  - Files: `canvas/label/textMetrics.ts` (new), `labels.ts:45-51`, `labelPlacement.ts:35`, `DiagramCanvas.tsx:2925`, `SpanElement.tsx:145`, `C4Canvas.tsx:51`
  - One exported metric replaces 7, 7, **8** and 5.5 px per character; one fit function replaces the formula written twice in the library and `c4`'s `typeLineFitted`. **A label must not be sized by one metric and trimmed by another** — the drift behind the overflow reports on `databricks`, `c4` and `shacl`.
  - **Where the backend computes the box** (c4 clamps at 240) the metric is Architect 1's and this task cites its fixture. **`c4`'s word wrap is not re-implemented here**; it belongs to `functional-decomposition-graph`'s `wrap`.
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 12.1_

- [x] 6. The backend keystroke comes from the declaration
  - Files: `definition/actions.ts` (`backendKey?`), `DiagramCanvas.tsx`, the 11 `BACKEND_KEYS` maps
  - The library derives the `ContextShortcut` from the declaration that fired the action; a gesture-invoked action carries a **declared** backend key instead of a manufactured `"Delete"` — the synthesised keystroke `declarative-diagram-modules` said must disappear. **Nothing on the wire changes.** `backendKey` is a new module-facing name → tell Architect 1 in the same change.
  - **Guard: no module constructs a `ContextShortcut` or maps an action id to a key.**
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 12.1_

- [x] 7. The library owns inline-rename wiring
  - Files: `DiagramCanvas.tsx`, the 8 identical `editing={{ … }}` wirings
  - `useContextPrompt` moves inside the library, which already knows the selection the prompt applies to; the `editing` prop leaves the contract, so **typecheck refuses a module that still wires it**.
  - _Requirements: 6.1, 6.2, 6.3, 12.1_

- [x] 8. One move call
  - Files: `useDiagramStream.ts`, the 14 `moveElementTo` wrappers
  - The hook returns `moveElementTo`; the wrappers go, and with them `ansible-structure`'s swallowed message — **the backend's own sentence is reported**, which is this specification's one other permitted visible change.
  - **Record the reversal in the commit**: archived `technical-debt-cleanup` R3.2 kept the call per module deliberately, and the user reversed it on 2026-09-20 because all 14 bodies are the same call.
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 12.1_

---

## Group 3 — The seams, and the last sweep

- [x] 9. One row-rounding rule, shared with the backend
  - Files: `DiagramCanvas.tsx` (`snapToStep` exported), `DependencyGraphCanvas.tsx:166`, `TimelineCanvas.tsx:135`
  - The two byte-identical `nearestRow` copies go. **Architect 1 owns the rule** (the backend persists the row); this task cites its fixture, **including the halfway and negative-zero cases** five earlier copies got wrong, and tells Architect 1 that `snapToStep` is now module-facing.
  - _Requirements: 8.1, 8.2, 8.3_

- [x] 10. One builder per id grammar
  - Files: `canvas/selection/gestureIds.ts` (new), the ~8 canvases building `new:x,y` and `rel:a->b`, `CausalLoopCanvas.tsx:359-360`
  - **The defect this removes:** causal-loop strips `variable:` by length with no `startsWith` guard, so an id of exactly `variable:` yields an empty end and an unprefixed id is silently truncated. Every other strip in the client guards.
  - **The grammar and its parsers are Architect 1's**; this task cites its fixture and tests the empty and unprefixed ids.
  - _Requirements: 9.1, 9.2, 9.3_

- [x] 11. One canvas test harness
  - Files: `test-setup.ts`, `canvas/library/testing/canvasHarness.ts` (new), the 18–20 copies across canvas tests
  - The pointer-capture stubs move to `test-setup.ts`; the pointer-event factory, the `pushedIds` adapter and **one fake context connection with every member** replace 20 partial fakes. `renderCanvas` **stays per module** — its props differ, so it is not a duplicate.
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

- [x] 12. No stylesheet rule without an emitter
  - Files: a new mounted guard on the client; **a backend test that exports each module's shipped examples as the model the client would receive**; `timeline.css`'s two confirmed-dead rules
  - **Mount every canvas on its shipped examples, collect the classes actually emitted**, and fail on a stylesheet class nothing emits unless it is listed with a reason. **A text search cannot answer this**: `pipeline-problem-{payload.problemSeverity}` and `databricks-sim-{payload.simulated}` are composed at runtime from declared templates and read as dead.
  - **How an example's model reaches the client - 11.2 names the method but not the route, so the user ruled it (the Scrum master's chat, 2026-09-25): a backend test exports each module's shipped examples as the model the client would receive, and the client guard mounts each canvas on those.** This makes the task **cross-stack**. The cheaper substitute - mounting on the hand-written models the canvas tests already build - was rejected, because those models never compose a template class, so `pipeline-problem-{payload.problemSeverity}` would read as dead and the guard would fail on the very classes it exists to see.
  - The first run's findings are **removed rather than listed**.
  - **The mirror direction is this task's too, by criterion 11.4**: a class a canvas emits that no rule claims fails unless it is listed with a reason. It is the same walk over the same two lists in the opposite direction, so one guard serves both. It was assigned to `canvas-single-scrollbar` on 2026-09-23, and that specification fixed the instance that motivated it - `library-canvas-surface`, emitted with no CSS rule, now a block box (`b2b8ba03`) - and completed without the guard. So the guard's first run in this direction will not find that instance; it exists to find the next one.
  - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [ ] 13. Gate, land, and check the record against the code
  - Four gates on the merged tree; both port files reverted; **the coverage diff re-run against files and strings rather than task claims**; and the permitted-visible-change list checked against what actually changed — the two theme fixes, the refusal surface, a trimmed label's character, and ansible's move message. **Anything else visible is a defect, not a side effect.**
  - _Requirements: 12.1, 12.2_

---

## Requirement coverage

Every acceptance criterion in the approved requirements was checked against every task claim above, mechanically, in both directions. **Forty-four criteria** (4, 5, 3, 4, 4, 3, 4, 3, 3, 5, 4 and 2 across Requirements 1 to 12; 11.4 was added on 2026-09-23), all claimed, and no claim names a criterion that does not exist.

**No criterion is unclaimed.** Requirement 12 is the specification's own bar rather than a piece of work: 12.1 is claimed by every task that may change something visible, and 12.2 by task 13, which re-runs the diff against the code and judges the visible changes against the list. A claim is a promise, not proof — task 13 is where each is traced to a file and a string.
