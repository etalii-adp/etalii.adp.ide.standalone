# Tasks Document

**The order follows the design: the library half first, then the module half.** Tasks 1 to 5 are library work and wait on nothing outside this document. Tasks 6 to 9 are the module, its documentation and its checks, and each names the tasks it waits on.

**Every task's guard is seen to fail before it is trusted** (CLAUDE.md, *Bugs found during implementation or verification*), and each task names the planted defect its guard must fail against. **No library declaration or test names this diagram** (Requirement 7.5): the mode is `row-packed`, not "compact".

**The open questions are ruled** (chat, 2026-09-27): the toggle is not remembered (Q1), a trend keeps its stored row (Q2), and phases are split evenly (Q3).

**Coverage, run before this card was raised** (task 9): **42 of 42 acceptance criteria are claimed**, and no task claims a criterion that does not exist. Several criteria are claimed twice, once where the library makes a behaviour possible and once where the module declares it or the browser shows it; both claims are real work.

## The library half

- [ ] 1. The `row-packed` layout and its inverse
  - File: `src/client/src/canvas/library/layout/rowPackedLayout.ts` (new), `src/client/src/canvas/library/layout/rowPackedLayout.test.ts` (new), `src/client/src/canvas/library/layout/layoutAlgorithm.ts`, `src/client/src/canvas/library/definition/diagramDefinition.ts`
  - Add `"row-packed"` to `LayoutMode` and `rowPacked?: { width: number; gap: number }` to `LayoutDefinition`. Widen `LayoutPositions` to carry an optional `width` per element. Add an optional `inverse(point, input, layout)` to `LayoutAlgorithm`.
  - Implement `place` as the design states it: sort by manual x, ties by id; place a group of equal starts at the largest of the previous group's rightmost x and each touched row's end plus `gap`; a second trend on one row within a group follows at that row's end plus `gap`; every element gets the declared width and keeps its manual y. Implement `inverse` by interpolating manual x between the two placed neighbours of the point, extrapolating one manual `width` per placed `width` beyond either end, and returning the point unchanged with no elements. The inverse calls `place`; it does not re-derive the placement.
  - Register it in `LAYOUT_ALGORITHMS`.
  - Guard: time order kept on a shuffled input of fifty; no two elements on a row closer than `gap`; each element at the leftmost x satisfying both; the same output for every shuffle; equal starts on two rows share x whatever their ids; every element carries the width; the inverse interpolates, extrapolates at both ends and is the identity on an empty input; a point dropped between two trends on different rows maps to a manual x between theirs, and `place` puts an element there between them.
  - Seen to fail against: a `place` that returns the manual positions (the order, overlap and width assertions fail), one that places by element order instead of by group (the shared-x assertion fails for ids sorting the other way), and an inverse that returns the point unchanged (the interpolation assertion fails).
  - _Requirements: 7.1, 7.5, 3.1, 3.2, 3.3, 3.4, 3.7, 6.1, 6.2, 6.5_

- [ ] 2. The canvas applies a layout's width, over every element
  - File: `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/layout/layoutAlgorithm.test.tsx`
  - Where the canvas maps `layoutPositions` onto the model's elements, apply `width` when the layout returns one. Keep the layout computed over `model.elements`, before the filter removes any, and keep its dependency on `model` so it re-runs on every change.
  - Guard: under `row-packed`, every drawn element is the declared width; hiding half the elements with the filter leaves the others' x unchanged; a model update moving one element's manual x re-places it; `tree` and `manual` draw exactly as before.
  - Seen to fail against: a canvas that ignores the returned width (the width assertion fails), and one that lays out `visibleElements` (the filter assertion fails).
  - _Waits on: task 1_
  - _Requirements: 7.1, 3.5, 3.6, 2.1_

- [ ] 3. Per-mode definition overrides
  - File: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/api/diagramRuntimeConfig.ts`, `src/client/src/canvas/library/api/diagramRuntimeConfig.test.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.test.ts`, `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/DiagramCanvas.layoutModes.test.tsx` (new)
  - Add `LayoutDefinition.modeOverrides`. `effectiveDefinition(definition, config, activeMode)` applies the active mode's overrides between the definition and the runtime's; `layout` cannot be overridden. The canvas computes the effective definition for the active mode, so `draggingEnabled`, the `resizable` prop, the boundary handles and `bottomRulers` read the overridden values with no change of their own. The validator checks each mode's merged definition and rejects a `row-packed` mode without `rowPacked`.
  - Guard: under an override setting `dragging: "disabled"`, `sizing: "model"`, `draggableBoundaries: false` and `rulers: []`, no drag starts, no resize cursor or handle is drawn, no boundary handle is drawn and no ruler is drawn; switching back restores all four; connecting, selecting, inline editing and a toolbox drop still work under the override; runtime overrides still win over mode overrides; the validator rejects the missing `rowPacked`.
  - Seen to fail against: a canvas that reads the base definition (every "not drawn" assertion fails), and a merge that applies mode overrides after the runtime's (the precedence assertion fails).
  - _Requirements: 7.2, 7.5, 4.1, 4.2, 4.3, 4.4_

- [ ] 4. A drop translated through the active layout's inverse
  - File: `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/DiagramCanvas.layoutModes.test.tsx`
  - In the drop handler, when the active algorithm has an `inverse`, raise `element-dropped` with the inverse of the pointer's canvas position; otherwise raise the pointer's position as today.
  - Guard: under `row-packed`, a drop between two elements raises a position whose x lies between their manual x; under `manual`, the raised position is the pointer's, unchanged.
  - Seen to fail against: a handler that always raises the pointer's position (the between assertion fails).
  - _Waits on: task 1_
  - _Requirements: 7.3, 7.5, 6.1_

- [ ] 5. The layout toggle, and the active mode as view state
  - File: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.test.ts`, `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/DiagramCanvas.layoutModes.test.tsx`, the library stylesheet
  - Add `LayoutDefinition.toggle?: { caption; on }`. The canvas holds the active mode in state; `config.activeLayoutMode` still wins over it. With a toggle declared, draw `<button className="library-layout-toggle" aria-pressed>` directly after the filter box's legend, switching between `modes[0]` and `toggle.on` and raising `layout-mode-changed`, and draw no `library-layout-switcher`. The switcher's buttons now also set the state. Style both states with existing theme tokens only. The validator rejects a toggle whose `on` is not a declared mode, a toggle on other than two modes, and a toggle without a filter.
  - Guard: the button is the legend's next sibling and carries the caption; `aria-pressed` follows a click; a click switches the layout and raises the event once; the selection, the filter's tags and the viewport are unchanged by a switch; a read-only canvas still toggles; a fresh mount starts in `modes[0]`; the switcher is drawn only without a toggle; a switcher click now changes the layout; the three validator rejections.
  - Seen to fail against: a toggle that raises the event without changing state (the layout assertion fails, as the switcher does today), and one drawn before the filter row (the sibling assertion fails).
  - _Requirements: 7.4, 7.5, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

## The module half

- [ ] 6. The hype cycle declares compact mode
  - File: `src/diagrams/gartner-hypecycle-graph/client/GhgCanvas.tsx`, `src/diagrams/gartner-hypecycle-graph/client/ghgDefinition.test.ts`, `src/diagrams/gartner-hypecycle-graph/client/ghgConnect.test.tsx`, `src/diagrams/gartner-hypecycle-graph/backend/EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests/GhgCommands.Tests.cs`
  - In `definitionFor(unit)`, declare `modes: ["manual", "row-packed"]`, `toggle: { caption: "Compact", on: "row-packed" }`, `rowPacked: { width: 12 * GhgScale.unitsPerMonth, gap: GhgScale.unitsPerMonth }`, and a `row-packed` override with the trend type at `sizing: "model"`, `draggable: false`, `draggableBoundaries: false` and no `boundaries` binding, `dragging: "disabled"` and `rulers: []`. Change no event handler unless a test shows one must.
  - Guard: for every time unit, the definition and its merged `row-packed` definition validate; the override is as stated; under `row-packed` a trend is drawn at the compact width with even segments, an influence is drawn from one phase edge to another and a second in the same direction is refused, a selected influence's end still slides, and rename, delete, phases, tags and undo run through the same actions as in true-time; on the backend, an add-trend at a translated position gets that month and the row under it, twelve steps long with four phases, through the existing command. The module guard suites pass unchanged.
  - Seen to fail against: a definition that keeps `boundaries` in the override (the even-segments assertion fails), and one that omits `rulers: []` (the definition assertion fails).
  - _Waits on: tasks 1 to 5_
  - _Requirements: 8.1, 8.2, 8.3, 1.1, 2.1, 2.2, 2.3, 4.1, 4.2, 4.3, 5.1, 5.2, 5.3, 5.4, 6.3_

- [ ] 7. Documentation
  - File: `docs/creating-a-diagram-module.md`, `docs/diagrams.md`, `docs/architecture.md` (only if a sentence about layout modes is made false)
  - Document `row-packed`, `rowPacked`, `modeOverrides`, `toggle` and the drop inverse where module authors are pointed for the layout declaration. Add compact mode and this specification to the `gartner/hypecycle-graph` row.
  - Guard: `ArchitecturePages.Tests` passes; a reader following the layout section can declare a toggle without reading library source.
  - Seen to fail against: not applicable; this task writes prose, and its check is the read-through.
  - _Waits on: task 6_
  - _Requirements: 9.1_

- [ ] 8. The browser pass
  - File: `tests.md`
  - Write and run an entry covering Requirement 9.2 in a real browser, in both themes, on the `technology-trends` example: the Compact toggle below the legend and its pressed state; every trend one width with even phases; time order kept and no overlap on a row; no move, resize or chevron drag offered; the time axis hidden and restored; an influence drawn in compact and seen at the same phase and fraction in true-time; a trend dropped between two others, placed between them, with approximated dates in the property grid and in true-time; and `git diff` on the `.ghg` file empty after toggling back and forth.
  - Guard: the entry is recorded with its outcome per step; jsdom results are not cited as evidence.
  - _Waits on: task 6_
  - _Requirements: 9.2, 9.3, 1.2, 2.2, 3.2, 3.3, 4.4, 5.5, 6.4_

- [ ] 9. The coverage diff, before the tasks card and again after implementing
  - File: this document
  - Diff every requirement reference in this document against every acceptance criterion in the requirements. Before the card: every criterion claimed. After implementing: each claim traced to the files and strings that show it, and two traces read back to their artefacts to confirm the evidence is that criterion's.
  - _Requirements: 9.4_
