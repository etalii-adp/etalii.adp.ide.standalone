# Tasks Document

One worktree for the whole specification (`.claude/worktrees/ddm`, per CLAUDE.md's one-worktree-per-specification rule), one Developer owning it until every task is done. Every task lands only when the four gates — `npm test`, `npm run typecheck`, `dotnet format style --verify-no-changes --severity info`, `dotnet test` — exit zero, judged by exit codes captured before any pipe. Running the app happens from the worktree on the implementing Developer's reserved ports, both port files reverted before merging.

**The order is the design's and must not be flattened.** Group 1 is the shared library, gated and landed **before any module is touched** (Requirement 3.6). Group 2 is the sufficiency table, which is a **check on Group 1** rather than a plan for Group 3. Group 3 is the migration. A task from a later group started early is the failure this ordering exists to prevent: a vocabulary proven by the module that needed it is not proven.

---

## Group 1 — The shared library surface

- [-] 1. The binding resolver
  - Files: `src/client/src/canvas/library/definition/binding.ts` (new), `binding.test.ts` (new)
  - Field paths, templates, collection bindings with `each`, and `when` conditions, resolving against `DiagramModelElement` and its module payload. Pure, no React.
  - **The prohibition goes in the code, not only in the design**: a doc-comment stating that a binding is data and **cannot call anything**, with the reason — a callable binding is the escape hatch under a new name, and `binding.ts` is where someone will be tempted, not the design document. The type makes a function-valued binding unrepresentable rather than discouraged.
  - Tested: each binding form; a path that does not resolve draws nothing rather than throwing; a collection binding over an empty list yields nothing.
  - _Requirements: 2.5, 3.1_

- [ ] 2. `labels` — multiple, bound, stacked
  - Files: `definition/diagramDefinition.ts`, `DiagramCanvas.tsx`, tests
  - The text declaration of the design's table: `text` binding, `slot`/`offset`, `stack` for collections, `typography`, `editable`, `truncate`, `when`, `tooltip`. `LabelRule` is superseded; a single-label module writes one entry.
  - **The collection case is the acceptance**, not a variant of it: a declaration must express `owl-card`'s shape — a header, a badge line present only when its list is non-empty, and one line per entry of a model collection composed as `predicate: value annotation` and truncated to the box. A design that passes with three fixed slots has not satisfied this task.
  - _Requirements: 2.2, 2.3, 3.1_

- [ ] 3. `decorations`
  - Files: `definition/diagramDefinition.ts`, `DiagramCanvas.tsx`, tests
  - The closed glyph set — `line`, `path`, `circle`, `rect`, named marker — with bound geometry, class and optional text. Drawn relative to its element, **no hit-testing, no gesture, no anchor**; anything needing those is an element type.
  - Acceptance is the five renderers that use no shared component today: a stub, a badge, an annotation, a target.
  - _Requirements: 3.2_

- [ ] 4. `background`
  - Files: `definition/diagramDefinition.ts`, `DiagramCanvas.tsx`, tests
  - Bands, axes with end labels and rotated titles, gridlines, and regions bound to model collections. Beneath everything, `aria-hidden`, takes no gestures.
  - Acceptance is `wardley-map`'s twenty-one background lines and `timeline`'s ruler — two instances, which is what makes it a category rather than a special case.
  - _Requirements: 3.3_

- [ ] 5. `actions` and anchor enablement, and the library owning the keyboard
  - Files: `definition/diagramDefinition.ts`, `DiagramCanvas.tsx`, tests
  - `id`, `invokedBy` (shortcut/menu/gesture), `appliesTo`, `enabled` binding, `label`. The library dispatches; **only the module's handler stays imperative.**
  - **Two things disappear and the task is not done until they do**: the four hand-written shortcut key lists, and the synthesised `{ key: "Delete", … }` object in nine canvases. A module must have no way to name an action by manufacturing a key event.
  - The existing half-declared enablement — `dragging`, `draggable`, `deletable`, `label.editable`, `connectOnRightDrag`, endpoint constraints, `layout.modes` — is **kept, not rewritten**; symmetry is not a reason to touch working declarations across thirteen migrations.
  - **Anchors gain `visible` and `enabled` bindings on the same mechanism.** `AnchorSet` says where anchors are and nothing about whether they are shown or usable; Requirement 2.4 requires the declaration to state **whether an anchor is visible or enabled, and never how it looks**. All twenty-nine anchor declarations in the tree are `{ kind: "edge" }`, so the positional half has never been needed and the half that was missing is the one modules actually want.
  - _Requirements: 2.4, 2.6, 2.7, 2.8, 3.4_

- [ ] 6. The structural half of the model over the wire
  - Files: module backends, the diagram contract, the client stream hooks
  - Identity, type, position, size and connection endpoints arrive shaped for the canvas, removing the bulk of the 665 mapping lines. **The semantic half stays a binding** — the user ruled for this split on 2026-09-08 with the alternative in front of them.
  - Each module's backend keeps owning what its elements mean; only the handover shape changes.
  - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [ ] 7. Chrome declared
  - Files: `definition/diagramDefinition.ts`, `DiagramCanvas.tsx`, tests
  - Loading, unavailable, title and legend as declarations with content bound to model fields. A module wanting none declares none, **as explicitly as presence**.
  - _Requirements: 6.1, 6.2, 6.3_

- [ ] 8. Gate and land Group 1
  - All four gates green on the merged tree; the library surface is on `develop` before any module task begins.
  - _Requirements: 3.6_

---

## Group 2 — Sufficiency, proven before anything migrates

- [ ] 9. The translation table
  - Files: `.spec-workflow/specs/declarative-diagram-modules/sufficiency.md` (new)
  - **Twenty-eight rows, one per element type**, naming: module, current custom shape, replacing built-in, labels needed, decorations needed, background needed, actions needed, and **any property that does not yet exist**.
  - Produced **after** Group 1 and **before** Group 3, so it checks the design rather than plans the work.
  - **When a row cannot be filled, the procedure is fixed and involves no judgement under pressure**: record the gap with the module and what it needs; extend the vocabulary **centrally** (Requirement 1.5); re-check the table. **A module is never marked an exception, and migration does not begin with an unfilled row.** This clause matters most at the moment everyone wants to be finished.
  - The table is also Group 3's order and its acceptance: a module is done when its rows are satisfied and its tests pass unchanged. **A property added because a row needed it is justified by that row and named in it** (Requirement 3.5); nothing is added speculatively.
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 1.5, 3.5_

---

## Group 3 — The migration, one module per landing

- [ ] 10. Reference migration: `dependency-graph`
  - One element type, one route, one shared component with no raw SVG, a full action set including a synthesised Delete, median size at 382 lines. It exercises labels, actions and the shape swap **without** also being the first test of decorations or background. `dotnet-dependency-graph` is deliberately not the reference despite being smaller: it was under active change when this was written, and a reference measured against a moving target proves nothing.
  - **Its diff is the pattern**, and the gaps it exposes are closed centrally before task 11 begins.
  - Its readme records what it declares.
  - _Requirements: 9.1, 9.2, 9.4, 8.1_

- [ ] 11. The single-label modules: `dotnet-dependency-graph`, `timeline`, `c4`, `mindmap`, `azure-pipeline`
  - Each its own landing, each with its existing tests passing unchanged.
  - _Requirements: 9.3, 9.4, 8.1, 8.2_

- [ ] 12. The multi-label family: `rdf` — all four readings
  - Seven element types between them and the richest label needs in the tree; `owl-card` is the shape task 2 was specified against, so this is where that specification is tested against the thing it was drawn from.
  - _Requirements: 9.3, 9.4, 8.1_

- [ ] 13. The decoration cases: `ansible-structure`, `helm-charts`, `causal-loop`, `sparql`, `databricks`
  - The stubs, badges and annotations task 3 was specified against.
  - _Requirements: 9.3, 9.4, 8.1_

- [ ] 14. `wardley-map`, last
  - The background's proof: twenty-one of its twenty-four raw-SVG lines are axes and stage bands, and its shapes are a symbol and a dot. Last because a vocabulary that expresses it expresses the rest, and because failing here early would stall the twelve that do not need it.
  - _Requirements: 9.3, 9.4, 8.1_

---

## Group 4 — The guard, the record, the proof

- [ ] 15. The conformance guard
  - Files: `src/client/src/canvas/library/declarativeModules.test.ts` (new)
  - Walks every module client, collects every offender, fails once naming all of them with file and rule. Fails a render function, a shortcut key list, a synthesised key event, raw SVG, or a function-valued binding.
  - **Keyed by artifact, per file owning the property**, so `databricks`' three wrappers over one inner canvas cannot misreport.
  - **Both canary shapes**: a floor on modules walked, and a named member asserted present.
  - **Seen to fail first**, against a deliberately non-compliant module, before acceptance.
  - Its limit stated in its own doc-comment: it reads text, so a renderer under an unrecognisable name goes unseen — it catches the copy, which is how this actually happens.
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

- [ ] 16. `BUILT_IN_SHAPES`' comment made true
  - Files: `definition/diagramDefinition.ts`
  - Its comment describes a guard and a toolbox that derive from it; both are imagined. Task 15 creates the guard, so that half becomes true; the toolbox half is corrected or removed rather than left as a second imagined consumer.
  - _Requirements: 7.6_

- [ ] 17. Re-measure, and the manual check
  - Files: implementation log, `tests.md`
  - **Re-run the line attribution** and record the new shares against the approved table — declarative 9%, permitted 7%, imperative 59% — so the outcome is a number rather than a claim.
  - A `tests.md` entry covering one diagram per shape family in a real browser, because typography moving from stylesheets into declared properties is a visible-outcome change.
  - _Requirements: 9.6, 8.3, 8.4_

- [ ] 18. Gate and merge
  - Four gates zero on the merged tree; ports reverted; the coverage diff re-run against the finished code.
  - _Requirements: 8.1_

---

## Requirement coverage

The mechanical diff of every acceptance criterion against the task claims above:

| Criterion | Claimed by | | Criterion | Claimed by |
| --- | --- | --- | --- | --- |
| 1.5 | 9 | | 6.1–6.3 | 7 |
| 2.2, 2.3 | 2 | | 7.1–7.5 | 15 |
| 2.5 | 1 | | 7.6 | 16 |
| 2.6–2.8 | 5 | | 8.1 | 10–14, 18 |
| 3.1 | 1, 2 | | 8.2 | 11 |
| 3.2 | 3 | | 8.3, 8.4 | 17 |
| 3.3 | 4 | | 9.1, 9.2 | 10 |
| 2.4, 3.4 | 5 | | 9.3, 9.4 | 10–14 |
| 3.6 | 8 | | 9.6 | 17 |
| 4.1–4.4, 3.5 | 9 | | 5.1–5.4 | 6 |

**Two criteria are claimed by no task, and both are the second kind — *no task can claim it*, not *no task claimed it*:**

- **Requirement 1.1–1.4**, which define the permitted surface. These are not work; they are the definition every other task is judged against, and task 15 is the only thing that can assert them. Stated here so a reader does not add a task to "do" a definition.
- **Requirement 2.1 and 2.9** — that no module client contains a render function, and that `CustomShapeRef` becomes unreachable — are **outcomes of Group 3 completing**, not separate work. Task 15 is what makes them true rather than hoped, which is why the guard is a task and the outcome is not.

**Requirement 9.5** — ordering accounting for modules under active change — is discharged in task 10's justification rather than by a task of its own.

The diff runs again, against files and strings rather than task claims, when the implementation is finished.
