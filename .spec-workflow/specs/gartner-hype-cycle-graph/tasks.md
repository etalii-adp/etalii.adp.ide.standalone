# Tasks Document

**The order follows the design: the library half first, then the module half.** Tasks 1 to 12 are the library half and wait on nothing outside this document. Tasks 13 to 24 are the module half, and each names the library tasks it waits on. Task 13 checks those waits on `develop` rather than trusting this list.

**Every task's guard is seen to fail before it is trusted** (CLAUDE.md, *Bugs found during implementation or verification*), and each task names the planted defect its guard must fail against. **No library declaration or test names this diagram** (Requirement 10.1, 10.12): shapes, events and fields are named for geometry and behaviour.

**Coverage, run before this card was raised** (the criterion-to-claim diff, task 23): **71 of 71 acceptance criteria are claimed**, and no task claims a criterion that does not exist. Several criteria are claimed twice, once where the library makes a behaviour possible and once where the module declares or answers it; both claims are real work.

## The library half

- [x] 1. The arrow banner, its segments, their tooltips, and the `before` label placement
  - File: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/shapes/outline.ts`, `src/client/src/canvas/library/definition/labels.ts`, `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/shapes/outline.test.ts`, `src/client/src/canvas/library/DiagramCanvas.segments.test.tsx` (new), `src/client/src/canvas/library/definition/labels.test.ts`
  - Add `"arrow-banner"` to `BuiltInShape`, `BUILT_IN_SHAPES` and `OUTLINED_SHAPES`, outline `(0,0) (w - p, 0) (w, h/2) (w - p, h) (0, h)` with `p = min(h/2, w/2)` (design L1). Add the optional `segments` declaration (`SegmentDeclaration`: `count` and `boundaries` bindings, `max`, `classNames`, `tooltips`, `divider: "chevron" | "line"`). Only the first `count` segments are drawn, the last drawn one ends in the point, and each segment exposes its own stretch of the top and bottom edges as a region for task 3. Resting the pointer on a segment shows its tooltip, and on the point shows the last drawn segment's (L11).
  - Add the `LabelPlacement` value `"before"`: right edge 8 units left of the element's left edge, vertically centred, end-anchored (design, *`before` placement*).
  - Guard: a 4-segment banner draws four filled paths and three chevrons; a 2-segment banner two and one; each segment's corners lie inside the outline (point in polygon); hovering segment 2 shows its tooltip and the point shows the last drawn segment's; a `before` label ends 8 units left of the element and is centred on it.
  - Seen to fail against: a banner that draws `max` segments whatever `count` says, and a `before` label that reuses `beside`'s start anchor.
  - _Requirements: 10.1, 10.2, 10.12, 4.1, 4.2, 4.3, 4.4, 5.1_

- [x] 2. Draggable segment boundaries
  - File: `.../definition/diagramDefinition.ts`, `.../api/diagramEvents.ts`, `.../DiagramCanvas.tsx`, `.../DiagramCanvas.segments.test.tsx`
  - `SegmentDeclaration.draggableBoundaries?: boolean`. When true, each inner boundary between two drawn segments gets a horizontal drag handle over its divider, snapped with the element's `snap.x`. A drag raises the new event `segment-boundary-moved { elementId, index, x }` with `x` snapped, clamped so no segment becomes narrower than one snap step.
  - Guard: dragging boundary 1 raises the event with a snapped x; a drag past the next boundary clamps one step short of it; a declaration without `draggableBoundaries` renders no handle.
  - Seen to fail against: a handle that raises the raw pointer x (the snapped-x assertion fails) and one that does not clamp (the clamp assertion fails).
  - _Waits on: task 1_
  - _Requirements: 10.11, 10.12, 4.6_

- [x] 3. Continuous edge attachment
  - File: `.../definition/diagramDefinition.ts`, `.../api/diagramModel.ts`, `.../api/diagramEvents.ts`, `.../DiagramCanvas.tsx`, `.../DiagramCanvas.attachment.test.tsx`
  - `AnchorPositions` gains `{ kind: "along", edges, regions?: "segments" }`. `DiagramModelConnection` gains optional `sourceAttachment` and `targetAttachment` (`EdgeAttachment { edge, region?, at }`), `at` a fraction of the region's length. The end point is recomputed from the element's current bounds and segment boundaries on every render. `connection-drawn` carries both attachments; a connection without one behaves exactly as today.
  - Guard: a connection at `at: 0.25` of segment 2's top edge ends at that point, and after the element doubles in width it ends at 0.25 of the new segment 2; a gesture started over segment 1's bottom edge carries `{ edge: "bottom", region: 0 }`; the named-anchor suites pass unchanged.
  - Seen to fail against: an attachment stored as an absolute offset (the doubled-width assertion fails).
  - _Waits on: task 1_
  - _Requirements: 10.3, 10.12, 6.2, 6.3_

- [x] 4. One connection per pair
  - File: `.../definition/diagramDefinition.ts`, `.../DiagramCanvas.tsx`, `.../DiagramCanvas.acyclic.test.tsx` or `.../DiagramCanvas.perPair.test.tsx` (new)
  - `Cardinality.perPair?: "ordered" | "unordered"`. `connectVerdict` gains an independent check that refuses, and does not highlight, a target that already has a connection of that relation type with the source (`ordered`: same direction; `unordered`: either). The check reads the model, so hidden connections count.
  - Guard: with A → B present, `ordered` refuses A → B and allows B → A; `unordered` refuses both; a connection hidden by task 5 still blocks its duplicate; the type, cardinality and cycle checks each still refuse on their own.
  - Seen to fail against: a verdict that skips the check, and one that counts only drawn connections (the hidden-duplicate case then passes).
  - _Requirements: 10.4, 10.12, 6.4, 7.3_

- [x] 5. Conditional connection visibility
  - File: `.../definition/diagramDefinition.ts`, `.../DiagramCanvas.tsx`, `.../DiagramCanvas.segments.test.tsx`
  - `RelationTypeDefinition.hideWhenAttachmentHidden?: boolean`. A connection whose source or target attachment names a segment at or beyond the element's drawn `count` is not drawn, hit-tested or selectable, and stays in the model untouched.
  - Guard: lowering a banner's count from 4 to 2 removes the drawn path of a connection attached to segment 3 and leaves it in the model; raising it back draws it again.
  - Seen to fail against: a canvas that removes the connection from the model instead of hiding it (the model assertion fails).
  - _Waits on: tasks 1 and 3_
  - _Requirements: 10.5, 10.12, 7.2_

- [x] 6. Snapping while resizing
  - File: `.../DiagramCanvas.tsx`, `.../DiagramCanvas.resize.test.tsx`, `.../snapToDeclaredStep.test.tsx`
  - The resize gesture applies the element's `snap.x` to the moving edge, during the gesture and on release; the far edge never moves; a resize is clamped to at least one step.
  - Guard: a width drag ending between two steps lands on the nearer one; a drag that would shrink below one step stops at one step; `timeline`'s resize suite passes unchanged.
  - Seen to fail against: a resize that snaps only on release (the during-gesture assertion fails).
  - _Requirements: 10.6, 10.12, 3.2_

- [x] 7. A rendered bottom ruler with a decade rung
  - File: `.../definition/chrome.ts`, `.../definition/chrome.test.ts`, `.../DiagramCanvas.tsx`, `.../surface/` (the ruler strip, new), `.../DiagramCanvas.ruler.test.tsx` (new)
  - `DiagramCanvas` renders each `chrome.rulers` entry with `edge: "bottom"` as a strip pinned to the viewport's bottom in screen coordinates, ticks from `rulerRangeOf` and `resolveTicks` following horizontal pan and zoom. `RulerRung` gains `decade`. The ruler reads its scale from the declaration (`{ unit: "month", unitsPerStep: 4, origin: "1900-01" }` for this diagram).
  - Guard: the strip's top stays at the viewport bottom minus its height after a vertical scroll; a tick at 1950-01 stays over canvas x of 1950-01 after a pan and a zoom; the ladder steps months, quarters, years, decades as zoom falls.
  - Seen to fail against: a strip placed in canvas coordinates (it scrolls away), and a ladder without the decade rung (the 150-year assertion finds year ticks too dense to label).
  - _Requirements: 10.7, 10.12, 9.1, 9.2, 9.3_

- [x] 8. A canvas filter over a tag expression
  - File: `.../definition/diagramDefinition.ts`, `src/client/src/canvas/library/filter/tagExpression.ts` (new), `.../filter/tagExpression.test.ts` (new), `.../DiagramCanvas.tsx`, `.../DiagramCanvas.filter.test.tsx` (new)
  - `DiagramDefinition.filter?: { field, label }`. When declared, the chrome shows a filter box; `parseTagExpression` implements `expr := term ("or" term)*`, `term := factor ("and" factor)*`, `factor := tag | "(" expr ")"`, case-insensitive. Non-matching elements and every connection touching them are not drawn. The filter is canvas view state, never sent to the backend, and survives deltas. A parse error keeps the last good filter and shows the error with its position.
  - Guard: `energy and (transport or industry)` matches `[energy, industry]` and not `[energy]`; `a or b and c` groups as `a or (b and c)`; an unbalanced parenthesis is an error at its position and leaves the previous filter applied; a filtered element's connections are not drawn; a delta does not clear the filter.
  - Seen to fail against: a parser that ignores precedence (the `a or b and c` case), and a filter that hides elements but not their connections.
  - _Requirements: 10.8, 10.12, 8.2, 8.3, 8.4_

- [x] 9. Invisible anchors
  - File: `.../DiagramCanvas.tsx`, `.../DiagramCanvas.declared.test.tsx`
  - `AnchorEnablement.visible: false` stops anchor dots being drawn at rest and on hover. Attachment still works, and during a connect gesture the valid-target highlight still shows the edge region under the pointer.
  - Guard: an element declaring `visible: false` renders no anchor dot on hover, and still highlights as a valid target during a drag.
  - Seen to fail against: today's canvas, which ignores `visible` (the no-dot assertion fails).
  - _Requirements: 10.10, 10.12, 6.5_

- [x] 10. A slider property editor
  - File: `src/api/context-contract.proto`, `src/client/src/shell/panels/PropertyRow.tsx`, its test
  - `ContextPropertyEditor` gains `SLIDER = 4`; its stops are the existing `candidates`. `PropertyRow` renders a range input with one stop per candidate and the current candidate's label beside it, and commits the candidate as CHOICE does. An unknown editor number is still shown read-only.
  - Guard: a SLIDER row with four candidates renders four stops and commits the chosen candidate; an unknown editor number still renders read-only.
  - Seen to fail against: a row that commits the stop index rather than the candidate.
  - _Requirements: 10.9, 10.12, 7.1_

- [x] 11. The five hype cycle colour tokens and their contrast guard
  - File: `src/client/src/index.css`, `src/client/src/moduleThemeContrast.test.ts`
  - Add `--color-diagram-hype-peak`, `-trough`, `-slope`, `-plateau` and `-chevron` in both modes with the design's values. Add each fill against the chevron to the contrast pairs.
  - Guard: every pair clears 3:1 in both modes (the design computes 4.96:1 or better).
  - Seen to fail against: a planted light chevron of `#cbd5e1`.
  - _Requirements: 4.5_

- [x] 12. Move `timeline` onto the rendered ruler, or record why not
  - File: `src/diagrams/timeline/client/TimelineCanvas.tsx`, `TimelineRuler.tsx`, their tests, the implementation log
  - Declare `timeline`'s ruler through `chrome.rulers` with a seconds unit and remove `TimelineRuler.tsx`, **only if its tick output is unchanged**. If it would change what `timeline` draws, keep `TimelineRuler.tsx` and record the reason in the implementation log, as Requirement 10.7 allows.
  - Guard: `timeline`'s tick labels for its example at three zoom levels are byte-compared before and after.
  - Seen to fail against: a declaration with the wrong origin, which shifts every tick.
  - _Waits on: task 7_
  - _Requirements: 10.7_

## The module half

- [x] 13. Check the waits, before writing any module code
  - File: the implementation log only
  - Establish on `develop`, by measurement: tasks 1 to 11 have landed (name each capability's type or function and the commit), and the shared backend pieces FDG uses exist (`RestoreDocumentCommand<TStore>`, `LineDocument`, `LineSplice`, `AdpFileWriter`, the `new:` / `rel:` gesture grammar, `useDiagramStream`).
  - If a piece is missing, stop and report rather than writing a module-local copy (Requirement 1.2).
  - _Requirements: 1.2_

- [x] 14. The `.ghg` document: parse, splice, round-trip
  - File: `src/diagrams/gartner-hypecycle-graph/backend/EtAlii.Adp.Diagram.GartnerHypeCycleGraph/GhgParser.cs`, `GhgWriter.cs`, `_Model/*.cs`, `GhgDocumentFactory.cs` (all new), `.gitattributes`, fixtures under `.../EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests/Fixtures/`
  - The header `gartner-hypecycle-graph: 1`, then `trends:` and `influences:` with the keys the design lists: dates `YYYY-MM`, `row`, `phases`, the optional `peak-end`, `trough-end` and `slope-end` only when dragged, `tags` as a flow sequence, and influence ends as phase, edge and a two-decimal `at`. Each entry keeps its line range for `LineSplice`. **The parser never throws**: an unknown key or malformed entry is passed over and survives, and a YAML error yields an empty model with the text kept and a problem with its line. The factory writes the header with `trends: []` and `influences: []`.
  - Guard: a byte-identical round trip over fixtures with CRLF, LF, no final newline, comments and blank lines, with `*.ghg -text` added first and checked by `git ls-files --eol`; an edit rewrites only its entry's lines; a malformed document yields a model and a problem; the factory's document parses with no problems.
  - Seen to fail against: a writer that re-serialises rather than splices (quoting and key order change), and `*.ghg` left without `-text`.
  - _Waits on: task 13_
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

- [x] 15. The time scale and the phase boundaries, computed once
  - File: `.../GhgScale.cs`, `.../GhgPhases.cs` (new), tests, and a checked-in scale fixture shared with task 20
  - `GhgScale`: `UNITS_PER_MONTH = 4`, origin 1900-01, `TREND_HEIGHT = 32`, `ROW_STEP = 56`, month ↔ x. `GhgPhases`: the four phase names, their full Gartner names, and `BoundariesOf(trend)` as the design's three rules. A move shifts `start`, `stop` and every stored boundary by the same months; a resize scales stored offsets by new span over old, rounds to a month and keeps each phase at least a month. Every trend shares one height, so width is the only size a trend has.
  - Guard: `BoundariesOf` over even, one-dragged, two-dragged and dragged-beyond-the-last-phase cases; a move keeps boundaries exact; a resize scales and snaps them; x of a trend's left and right edge equals its start and stop through the fixture.
  - Seen to fail against: even spreading that ignores dragged neighbours (the one-dragged case), and a resize that keeps absolute boundaries (a boundary then falls outside the span).
  - _Waits on: task 14_
  - _Requirements: 3.1, 3.3, 3.4, 3.5, 3.6_

- [x] 16. The rules and the validator
  - File: `.../GhgRuleSet.cs`, `.../GhgValidator.cs` (new), tests
  - Report each breach under the design's rule ids: `ghg.duplicate-influence`, `ghg.self-influence`, `ghg.stop-before-start`, `ghg.phase-count`, `ghg.boundary-order`, `ghg.bad-attachment`, `ghg.dangling-reference`, `ghg.duplicate-id`, `ghg.unreadable-entry`. An influence hidden by a phase count counts like any other.
  - Guard: one fixture per rule id carrying exactly that breach; **A → B plus B → A is NOT reported**; a duplicate whose first copy is attached to a hidden phase IS reported; each breaching document still opens.
  - Seen to fail against: a duplicate check that treats the pair as unordered (the A → B plus B → A fixture is then reported).
  - _Waits on: task 14_
  - _Requirements: 2.4, 6.4, 7.3_

- [x] 17. Store, session, mapper and the backend definition
  - File: `.../GhgDocumentStore.cs`, `.../IGhgDocumentStore.cs`, `.../GhgDocumentReloader.cs`, `.../GhgSession.cs`, `.../GhgSessionFactory.cs`, `.../GhgElementMapper.cs`, `.../GhgContextSourceResolver.cs`, `.../Diagram.cs`, `.../ServiceCollection.AddGartnerHypeCycleGraph.cs`, `src/diagrams/gartner-hypecycle-graph/api/gartner-hypecycle-graph.proto` (all new), tests
  - As FDG's, on the shared store lifecycle. The mapper sends `GhgTrendPayload { name, phases, boundaries[], tags[], width }` with boundaries as fractions from `BoundariesOf`, and `GhgInfluencePayload { from_element_id, to_element_id, source_attachment, target_attachment }`. **A Description is never sent.** Influences on hidden phases are sent unchanged; hiding them is the canvas's job (task 5). `Diagram.cs` declares `public static DiagramDefinition HypeCycleGraph` with its `Build`. Lands with task 21's example, because registering the session factory makes the host require a seeded example.
  - Guard: a session over the example delivers every trend with its computed boundaries; a Description set through the property grid never appears in a delta; an influence on a hidden phase is still in the delta; the definition is discovered into the catalog.
  - Seen to fail against: a mapper that packs `description` into the payload, and one that drops influences on hidden phases.
  - _Waits on: tasks 14, 15 and 16_
  - _Requirements: 12.3, 7.2_

- [x] 18. The commands, each with its inverse
  - File: `.../Commands/*.cs` (new), tests
  - The design's table: `AddGhgTrendCommand`, `RemoveGhgTrendCommand` (with its influences, one edit), `SetGhgPlacementCommand`, `SetGhgSpanCommand`, `SetGhgBoundaryCommand`, `ClearGhgBoundariesCommand`, `SetGhgPhasesCommand`, `RenameGhgTrendCommand`, `SetGhgTagsCommand`, `SetGhgDescriptionCommand`, `AddGhgInfluenceCommand`, `SetGhgAttachmentCommand`, `RemoveGhgInfluenceCommand`. Each inverse is `RestoreDocumentCommand<IGhgDocumentStore>`. Span and boundary edits snap to a month and keep every phase at least a month. The connect command refuses a self-influence and a second influence in the same direction, hidden or not. Each refusal returns the writer's sentence and leaves the document unchanged.
  - Guard: every edit then its undo gives the original bytes; deleting a trend with two influences restores all three on undo; a boundary drag that would make a phase shorter than a month is refused or clamped; a scripted duplicate A → B is refused with a sentence, and B → A is accepted; each refusal leaves the bytes unchanged.
  - Seen to fail against: a connect command that trusts the client (the scripted duplicate lands).
  - _Waits on: tasks 14, 15 and 16_
  - _Requirements: 11.2, 11.3, 6.4, 6.6, 7.4, 3.2, 3.5, 4.6, 8.1_

- [x] 19. Toolbox, action and property providers
  - File: `.../GhgToolboxProvider.cs`, `.../GhgContextActionProvider.cs`, `.../GhgContextPropertyProvider.cs` (new), tests
  - The toolbox offers one **Trend** in a group **Hype cycle**; a drop creates a trend one year long from the snapped drop month with all four phases. A trend's properties: Name, Start and Stop (`YYYY-MM`), Phases as a SLIDER with the four candidates, Tags as a comma-separated LINE, Description. An influence's: Description, and read-only From and To ("Steam engine · Plateau"). Actions include *Even phases*. In read-only mode nothing that edits is offered.
  - Guard: the toolbox item is data from the backend; a drop at a mid-month x creates a trend starting at that month's start with `phases: 4`; the Phases row is a SLIDER with exactly the four candidates; an influence's From and To are read-only; read-only mode offers no editing action or writable property.
  - Seen to fail against: a Phases row offered as CHOICE (the editor assertion fails).
  - _Waits on: task 18_
  - _Requirements: 11.1, 11.4, 12.1, 12.2, 5.2, 7.1, 8.1_

- [x] 20. The client module: registration, definition, handlers
  - File: `src/diagrams/gartner-hypecycle-graph/client/register.ts`, `GhgCanvas.tsx`, `ghgIds.ts`, `ghgModel.ts`, `ghg.css`, `readme.md`, `package.json` (all new), tests
  - The design's definition: element type `trend` on `arrow-banner`, width-only `sizing: "user"`, `segments` (max 4, count and boundaries bound to the payload, chevron divider, draggable boundaries, the four class names, the four full Gartner names as tooltips); one editable label on `payload.name` placed `before`; anchors `{ kind: "along", edges: ["top", "bottom"], regions: "segments" }` with `visible: false`. Relation type `influence`: cubic bezier, arrow end marker only, `allowSelf: false`, `perPair: "ordered"`, `hideWhenAttachmentHidden: true`. Snap `x: { step: 4, origin: 0 }`, `y: { step: 56 }`. One bottom ruler; the filter on `tags`. Layout `manual`. Handlers turn each library event into its one route and nothing else. No selection, gesture or label code.
  - Guard: the definition passes `validateDiagramDefinition`; `declarativeModules` and the other module guards pass against it unchanged; each library event produces its one route; over the example, A → B is allowed, a second A → B is not highlighted, B → A is, a self-influence is not, and an influence hidden by a phase still blocks its duplicate; a vertical drop snaps the trend's middle to a row; the client's month ↔ x agrees with task 15's fixture.
  - Seen to fail against: a module that answers a gesture itself (the `declarativeModules` guard reports it).
  - _Waits on: tasks 1 to 11 and 17_
  - _Requirements: 1.1, 1.3, 3.6, 5.1, 5.3, 6.1, 7.3, 13.1_

- [x] 21. The technology-trends example
  - File: `src/diagrams/gartner-hypecycle-graph/examples/technology-trends/technology-trends.ghg`, `technology-trends.adp`, `readme.md`, the seeded copy under `src/examples/`, tests
  - About 200 hand-authored trends from roughly 1760 to today in the design's clusters, with plausible dates, related trends on nearby rows, and influences attached to the phase in which they acted. It exercises every phase count, influences on each phase's top and bottom edges, at least one influence hidden by a lowered phase count, dragged boundaries on some trends, Descriptions on trends and influences, and tags that make at least the three filters `energy`, `communication and computing` and `transport or energy`. The readme carries the requirements' purpose statement, says why the example is hand-authored, that its dates and influences are illustrative and not a historical claim, and what it does not demonstrate.
  - Guard: the validator reports nothing; `ExampleRegistration.Tests.cs` opens it against the deployed catalog; asserted from the parsed model, not by eye: each phase count 1 to 4 occurs, each phase has an influence on its top and on its bottom edge, at least one influence is hidden, and each of the three filters matches at least one trend and excludes at least one.
  - Seen to fail against: an example missing an influence on one phase's bottom edge (the per-phase assertion reports it).
  - _Waits on: task 17 (lands with it)_
  - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

- [x] 22. Catalog and authoring documentation
  - File: `docs/diagrams.md`, `docs/creating-a-diagram-module.md`, the Notion Diagrams entry
  - When this specification's tasks are approved, move the `gartner/hypecycle-graph` row from 💡 Identified to 📝 Specified naming this specification, and move it with the type's state after that; keep the Notion entry in step. Document the library capabilities of tasks 1 to 10 where module authors are pointed for a definition, and update any touch point the module moves.
  - Guard: the documentation-links test passes; the row's origin matches the `.adp` registration and the client registration's mime, asserted rather than read.
  - _Requirements: 15.1, 15.2_

- [x] 23. The coverage diff, before the tasks card and again after implementing
  - File: the implementation log only
  - Run the criterion-to-claim diff over this document and the approved requirements: 71 criteria, each claimed, and no claim naming a criterion that does not exist. Run it again when the work is done, traced to files and strings rather than to a task's promise.
  - _Requirements: 15.3_

- [ ] 24. The browser pass
  - File: `tests.md`
  - An entry covering, in a real browser, everything Requirement 16.1 lists: the banner and chevrons at each phase count in both themes; the phase tooltips; the name before the banner, right-aligned; month snapping on drag and resize and row snapping; phases even until a boundary is dragged, the dragged boundary staying put and scaling with a resize; influences on each phase's top and bottom edges keeping their place on resize; a same-direction duplicate refused and the opposite direction accepted; influences hiding and reappearing with the phase count and present in the saved file while hidden; tag filtering with and, or and parentheses; the axis pinned to the bottom while scrolling and zooming; and pan, zoom, drag and a filter change on the 200-trend example without visible lag.
  - **jsdom is not evidence for any of these**: it applies no CSS and lays out no text.
  - _Requirements: 16.1, 16.2_
