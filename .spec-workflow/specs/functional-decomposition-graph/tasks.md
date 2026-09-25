# Tasks Document

**The order is the user's ruling, given as a selection on 2026-09-15: the library half now, the module half after the shared backend and client pieces it would otherwise copy have landed.** Tasks 1 to 7 are the library half and wait on nothing. **Tasks 8 to 19 are the module half, and each names the requirement it waits on** — `backend-centralization` and `client-centralization` (**both requirements and design approved, as of 2026-09-23**; this paragraph said "design in progress" and "requirements in progress" until then). **Neither specification has tasks yet, so the waits are named by requirement**, and the first module task re-states them as a check to run rather than a memory to trust. **Superseded for tasks 11, 12 and 14 by the user's chat ruling of 2026-09-25:** tasks 11 and 12 now name `backend-centralization` by *task number*, because it has tasks and a requirement read literally includes converting nine other modules. Task 14 waits on task 11 rather than on `client-centralization`, which centralizes neither the stream hook (already shared) nor the delta fold (per-module).

**Every task's guard is seen to fail before it is trusted** (CLAUDE.md, *Bugs found during implementation or verification*), and each task below names the planted defect its guard must fail against. **No library declaration or test names this diagram** (Requirement 9.1, 9.6).

**Coverage, run before this card was raised** (the instrument is the criterion-to-claim diff): **49 of 49 acceptance criteria are claimed**, and nothing is claimed that is not a criterion. Requirement 3.3 was the one criterion no task claimed on the first pass, and it is a real claim rather than a silence: it asks that snap NOT be restated, so task 12 carries it as an instruction to declare nothing and read the library's semantics from the code.

## The library half — nothing waits on this

- [x] 1. Three shapes, one outline each, and the region text fits in
  - File: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/shapes/outline.ts` (new), `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/shapes/outline.test.ts` (new)
  - Add `"superellipse"`, `"trapezoid"` and `"diode"` to `BuiltInShape` and `BUILT_IN_SHAPES`; add `outlineOf(shape, bounds)` returning the closed outline and `textRegionOf(shape, bounds, bandHeight)` computed from it. Superellipse exponent 4 at 64 samples; trapezoid corners (0,0) (1,0) (0.85,1) (0.15,1); diode a rectangle closed by a semicircle of radius `height/2` at 24 samples. `diamond`, `hexagon` and `parallelogram` return their existing corner lists, so their drawing does not move.
  - Guard: for each new shape, at the shared height and widths 80, 160 and 400, every corner of the text region lies inside the outline (point in polygon), and each shape renders with its declared class.
  - Seen to fail against: `textRegionOf` returning the bounding box — the trapezoid and diode corners then fall outside.
  - Purpose: one outline per shape, read by the drawing, the text region and the edge point, so those three cannot disagree
  - _Leverage: `canvas/elements/` for the existing shape components, `BUILT_IN_SHAPES`' guard in `declarativeModules.test.ts`_
  - _Requirements: 9.1, 4.1_

- [x] 2. Height resize
  - File: `.../definition/diagramDefinition.ts`, `.../api/diagramEvents.ts`, `.../DiagramCanvas.tsx`, `.../DiagramCanvas.resize.test.tsx` (new)
  - `ElementTypeDefinition.resize?: "width" | "both"`, read only when `sizing: "user"`; **omitted means `"width"`**, today's behaviour. `ElementResized.side` widens to `"left" | "right" | "top" | "bottom"`, and the vertical edge is carried the way the horizontal one is: the far edge stays put and is never crossed.
  - Guard: a `"both"` type raises `element-resized` with `side: "bottom"` and the new height; a type that omits `resize` renders no top or bottom handle; `timeline`'s own suite passes unchanged.
  - Seen to fail against: handles rendered for every `sizing: "user"` type. **The detector is this task's own absence assertion** - that a type omitting `resize` renders no top or bottom handle. **`timeline`'s suite does NOT report it**: measured on 2026-09-23, the plant fails two assertions here and leaves timeline's 50 tests green, because that suite presses `[data-resize="right"]` and never asserts the horizontal handles are absent, and the right handle still exists when two more appear beside it. **A suite that presses a handle cannot report a handle that should not exist**, so *timeline's suite passes unchanged* is a no-regression companion - true, worth keeping, and holding in both worlds - rather than evidence about this change.
  - Purpose: a Comment can be made taller without changing what every other user-sized element offers
  - _Requirements: 9.2, 4.2, 4.3, 3.2_

- [x] 3. Wrapped labels, and `LabelRule.wrap` removed
  - File: `.../definition/labels.ts`, `.../definition/diagramDefinition.ts`, `.../definition/labels.test.ts`
  - `LabelDeclaration.wrap?: boolean`. A wrapped label is laid out inside `textRegionOf`, broken at spaces and at explicit newlines, at the library's own character-width estimate, stacked at the typography's line height. Text that overflows the region's height ends its last visible line with an ellipsis and keeps the full text as the label's tooltip. **Delete `LabelRule.wrap`**, which no module declares and nothing reads (Requirement 9.3's "or removed").
  - Guard: a 60-character label in a 160-wide box yields more than one line and no line exceeds the region; an explicit newline starts a new line; an overflowing label ends in `…`.
  - Seen to fail against: wrapping measured against the bounding box, which puts a trapezoid's text over its slanted edge.
  - Note: when `backend-centralization` R10's shared text metric lands, this reads that fixture's value instead of the local constant. Write the guard against the metric function, not the literal, so the change is one line.
  - _Requirements: 9.3, 4.3, 4.4_

- [x] 4. The shared multiline inline editor
  - File: `src/client/src/canvas/label/InlineLabelEditor.tsx`, `src/client/src/canvas/label/inlineLabelEditor.css`, `.../DiagramCanvas.tsx`, `src/client/src/canvas/label/InlineLabelEditor.test.tsx`
  - A label declaring `wrap: true` opens a **textarea** over the label's text region; Enter inserts a newline, Ctrl+Enter or Cmd+Enter commits, Escape cancels, blur commits as it does today. The commit is today's `label-commit-requested` with newlines in `value` — no new event.
  - Guard: a wrapped label opens a textarea and an unwrapped one an input; Enter does not commit and Ctrl+Enter does; the committed value carries the newline; Escape leaves the model unchanged.
  - Seen to fail against: the single-line editor opened for a wrapped label, where Enter commits and the newline never reaches the model.
  - _Requirements: 9.4, 7.3, 4.3_

- [x] 5. A declared cycle rule
  - File: `.../definition/diagramDefinition.ts`, `.../DiagramCanvas.tsx`, `.../definition/validateDiagramDefinition.ts`, `.../DiagramCanvas.acyclic.test.tsx` (new), `.../definition/validateDiagramDefinition.test.ts`
  - `DiagramDefinition.acyclic?: readonly { relationTypes: readonly string[] }[]`. `connectVerdict` gains a **third independent check**: when the relation being drawn belongs to a set, a target is refused if a directed path already runs from that target to the source through connections whose types are in the same set — a breadth-first walk over `model.connections`, bounded by the connection count. The type check, the cardinality check and this one each refuse on their own, and none is consulted to skip another. `validateDiagramDefinition` rejects an entry naming a relation type the definition does not declare.
  - Guard: A→B→C under a covered type refuses C→A; the same chain under an exempt type allows it; a chain mixing covered and exempt types is not a cycle; a cardinality refusal still fires when the cycle check would pass and the reverse; an unknown relation id fails validation.
  - Seen to fail against: a verdict that skips the walk (C→A is offered), and one that returns early when cardinality passes (the independence case).
  - _Requirements: 9.5, 5.2, 5.4_

- [x] 6. Connectors meet the outline, not the bounding box
  - File: `.../DiagramCanvas.tsx`, `.../DiagramCanvas.attachment.test.tsx` (new)
  - For every shape `outlineOf` covers, the edge point is where the ray from the centre towards the other end meets the **outline**. `box` and `pill` keep today's geometry.
  - Guard: a connector approaching a trapezoid from its lower left lands on the slanted side, inside the bounding box and on the outline to within half a unit; a box's attachment point does not move.
  - Seen to fail against: today's bounding-box point, which lands beside the shape.
  - Purpose: Requirement 6.1's arrowhead meets the element; no declaration can express this, so it is library work under Requirement 1.2
  - _Requirements: 6.1, 1.2_

- [x] 7. The five colour tokens, and the contrast guard
  - File: `src/client/src/index.css`, `src/client/src/theme.contrast.test.ts` (new)
  - Light mode is the user's hex exactly: `#aaed92`, `#ededed`, `#9edcfa`, `#86e6d9`, `#fcf281`. Dark mode is the design's: `#2e7814`, `#696969`, `#086fa1`, `#187569`, `#736a03` — same hue and saturation, lightness lowered to the lightest value reaching 5.0:1.
  - Guard: the test parses `index.css`, computes each token's WCAG contrast against `--color-text` in both modes, and asserts **two lines with different subjects, both unrounded**: at least **4.5:1** for any fill the contract admits, the WCAG AA line; and at least **5.0:1** for the five shipped dark tokens, the margin R4.6 exists to create. Each failure says which line was crossed, and the test states its linearisation formula and that the reference token is `--color-text`.
  - Seen to fail against: **`#857a04`, measured at 4.0078**, below the line from either direction. **Not `#7c7203`**, which this task named until 2026-09-23: it measures **4.4942**, so it fails by 0.006 and passes under the figure the requirements then stated - a plant that tests the arithmetic's rounding rather than the guard's purpose, and precisely the closeness R4.6 exists to forbid. Developer 2 found both, and the requirements' erratum is corrected under its own card.
  - _Requirements: 4.5, 4.6_

## The module half — each task names what it waits on

- [x] 8. Check the waits, before writing any module code
  - File: the implementation log only
  - Establish on `develop`, by measurement rather than memory: `backend-centralization`'s shared document-store lifecycle (R2, including R2.3 self-write suppression and R2.4–2.6 the failed-reload rules), its save result (R3), its change-detecting diff (R4), its document-change handler (R5), its restore-lines edit (R6), its YAML node range (R7) and its `new:`/`rel:` gesture grammar (R11); and `client-centralization`'s shared stream hook and delta fold. For each, name the type or function that exists and the commit that landed it, or record that it does not exist yet.
  - **If a piece is missing, stop and report rather than writing a local copy**: writing one is the fourteenth instance those specifications exist to remove, and the user ruled this order deliberately.
  - Purpose: the ordering the user ruled, checked rather than remembered
  - _Requirements: 1.2_

- [x] 9. The `.fdg` document: parse, splice, round-trip
  - File: `src/diagrams/functional-decomposition-graph/backend/EtAlii.Adp.Diagram.FunctionalDecompositionGraph/FdgParser.cs`, `FdgWriter.cs`, `_Model/*.cs`, `FdgDocumentFactory.cs` (all new), `.gitattributes`, fixtures under `.../Fixtures/`
  - The header line `functional-decomposition-graph: 1`, then `elements:` and `connections:` as the design states them, read into a model carrying each entry's line range, through core's `LineDocument` and `LineSplice`. Positions are the element's top-left; `width` per element; `height` only for a Comment; the other four share `FdgGeometry.SharedHeight` = 48. A Comment's `text` is a block scalar. **The parser never throws**; an unknown key, an unknown type or a malformed entry is passed over and survives.
  - Guard: a byte-identical round trip over every fixture — CRLF, LF, no final newline, comments, blank lines — byte-compared, with `*.fdg -text` added to `.gitattributes` first and checked with `git ls-files --eol`; an edit rewrites only its entry's lines; a malformed document yields a model and a problem rather than an exception.
  - Seen to fail against: a writer that re-serialises the document rather than splicing (the round trip then differs in quoting or key order), and `*.fdg` left without `-text` (the fixtures then compare git's rewriting rather than the writer's output).
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [x] 10. The rules table, as data, and the validator
  - File: `.../FdgRelations.cs`, `.../FdgRuleSet.cs`, `.../FdgOwnership.cs`, `.../FdgValidator.cs` (new), tests
  - `FdgRelations` states Requirement 5's table once on the backend: per relation id, its allowed source and target element types and its limits. `FdgRuleSet` reports each breach with the elements involved, under the rule ids the design lists: `fdg.forbidden-link`, `fdg.self-link`, `fdg.second-parent`, `fdg.second-shows`, `fdg.ownership-cycle`, `fdg.dangling-reference`, `fdg.duplicate-id`, `fdg.unreadable-entry`. `FdgOwnership.CyclesIn` is the one cycle implementation.
  - Guard: one fixture per rule id carrying exactly that breach, plus the example carrying none; **a Shows loop is NOT reported** (the cycle rule covers ownership only).
  - Seen to fail against: a cycle check that includes `shows` — the navigation round trip is then reported, which is the ruling this specification exists to honour.
  - _Requirements: 5.1, 5.5_

- [x] 11. Store, session and mapper, on the shared pieces
  - File: `.../FdgDocumentStore.cs`, `.../FdgDocumentReloader.cs`, `.../FdgSession.cs`, `.../FdgSessionFactory.cs`, `.../FdgElementMapper.cs`, `.../Diagram.cs`, `.../FdgContextSourceResolver.cs`, `.../api/functional-decomposition-graph.proto` (new), tests
  - Lifecycle and view through `backend-centralization`'s shared store lifecycle, save result, diff and change handler. The mapper sends type and centre, and the payloads `FdgElementPayload { name, text, width, height }` and `FdgConnectionPayload { from_element_id, to_element_id, name }`: the core element carries only a position, a type and a payload. **A Description is never sent.**
  - **Lands as one milestone with task 15's registration half and `FdgContextSourceResolver`, brought forward from task 13** (the user's chat ruling of 2026-09-25). Registering this task's session factory makes the host's `DrawnConnections` guard require an FDG example in `src/examples`, and every connection that example draws must resolve to a selection. So the three land together or none of them can.
  - **The module's backend `DiagramDefinition`**, in `Diagram.cs` as every module's is, whose `Build` registers the store, session and session factory. Without it FDG never reaches the diagram catalog, and task 15's example cannot register. No task in 11–19 claimed it until the user's chat ruling of 2026-09-25; Developer 1 found the gap.
  - Guard: a session over the example delivers every element at the shared height; a Description set through the property grid never appears in any delta; **the backend `DiagramDefinition` is discovered into the diagram catalog**.
  - Seen to fail against: a mapper that packs `description` into the payload — the delta assertion then finds it; and a module without the definition — discovery then finds no FDG type (the user's chat ruling of 2026-09-25).
  - _Waits on: `backend-centralization` tasks 2 (the save result), 3 (its discard guard), 5 (the store lifecycle), 11 (the change-detecting diff) and 13 (the change handler), each checked on develop by ancestry (the user's chat ruling of 2026-09-25)_
  - _Requirements: 7.1, 3.1_

- [x] 12. The commands, each with its inverse
  - File: `.../Commands/*.cs` (new), tests
  - Add, remove (with the connections to and from it, in one edit), move, resize, connect, disconnect, rename or edit text, set a Description, set a connection's name. Each inverse is a restore-lines command, as `dependency-graph` and `timeline` undo. The connect command refuses on the same three checks the canvas makes — type, cardinality, cycle — so a stale or scripted request cannot write what the canvas would not offer, and the refusal names which check failed.
  - **Snap: declare nothing.** The library's snap applies as it stands when this is built (Requirement 3.3); read its semantics from `SnapDeclaration` in the code rather than restating them here, since they were in flight when the requirements were written.
  - **FDG's own YAML node range moves onto the shared one here.** Task 9's parser carries a copy of the node range, `FdgParser.Range` and `EndMark`, mirroring `dependency-graph`'s. `backend-centralization` task 18 converts four other modules but not this one, so the parser's range is converted onto task 18's shared piece in this task, where the edits already use it. The guard is task 9's existing byte-compared round-trip fixtures, which must stay byte-identical across the conversion (the user's chat ruling of 2026-09-25).
  - Guard: every edit, then its undo, gives the original bytes; every refusal leaves the bytes unchanged; deleting an element with two connections restores all three on undo.
  - Seen to fail against: a connect command that trusts the client (a cycle-closing request then lands).
  - _Waits on: `backend-centralization` task 17's piece (the restore-lines edit, R6), task 18's piece (the YAML node range, R7, which an edit uses to find its entry's lines and which this line omitted), and task 21 (the gesture grammar, R11) (the user's chat ruling of 2026-09-25)_
  - _Requirements: 3.1, 3.2, 3.3, 6.3, 8.2, 8.3, 7.4_
- [x] 13. Toolbox and property providers
  - File: `.../FdgToolboxProvider.cs`, `.../FdgContextPropertyProvider.cs`, `.../FdgContextActionProvider.cs` (new), tests. `FdgContextSourceResolver` moved to task 11 (the user's chat ruling of 2026-09-25).
  - The toolbox describes the five element types as data. The property grid offers a Description for all five types and every connection, a Name for the four named types, and a connection's Name. Every property change is a command (task 12).
  - Guard: each of the five types and a connection offers a Description row; a Comment offers no Name row; a property set through the grid reaches the document and undoes.
  - Seen to fail against: a provider that omits the Description for one type, which the per-type assertion then reports.
  - _Waits on: task 12 (every property change is one of its commands)_
  - _Requirements: 7.1, 7.2, 7.3, 8.1_

- [x] 14. The client module: registration, definition, handlers
  - File: `src/diagrams/functional-decomposition-graph/client/register.ts`, `FdgCanvas.tsx`, `fdg.css`, `readme.md` (new), tests
  - The definition the design states: five element types on the shapes of task 1, all `sizing: "user"` with `resize: "both"` on the Comment, one editable label each (the Comment's wrapped), a class per type for its fill, and no anchors on the Comment. Five relation types with `route: "cubic-bezier"`, an arrow end marker only, a midpoint editable label, endpoints and cardinality exactly as the rules table, `allowSelf: false`. `acyclic` naming the four ownership relations and **not** `shows`. Layout `manual`; the toolbox derived. Handlers turn each library event into its one route and nothing else: a move through `moveElementTo`, a resize through `setProperty`, and a drop, connection, deletion or rename through a context action or shortcut (the user's chat ruling of 2026-09-25). The ids are defined once, in the client, for tasks 12 and 13 to answer.
  - Guard: the definition passes `validateDiagramDefinition`; `declarativeModules` passes against this module unchanged; each library event produces its one action; a connect gesture over the example highlights an allowed target, refuses a forbidden pair, refuses a second parent, refuses a cycle-closing target, and **does** offer the Shows round trip.
  - Seen to fail against: a module that answers a gesture itself rather than through an action — the `declarativeModules` guard reports it.
  - _Waits on: task 11 (the `.proto` its payload types are generated from). Not on `client-centralization`: the stream hook is already shared on develop, and the delta fold is the module's own `applyDelta`, as in the other 13 modules (the user's chat ruling of 2026-09-25)_
  - _Requirements: 1.1, 1.3, 5.2, 5.3, 6.1, 6.2, 10.1, 10.2_

- [x] 15. The field-service example
  - File: `src/diagrams/functional-decomposition-graph/examples/field-service/field-service.fdg`, `.adp`, `readme.md`, the seeded copy under `src/examples/`, tests
  - The graph the design lists: Planning owning a Task list and Task row; Task detail with a Step list and Step row; Open task, Back to list, Tick step, Complete task; Task with a nested Step and Location; Planning day; Sync queue calling Upload photos; Offline cache; **Shows** Open task → Task detail and Back to list → Task list, the navigation round trip; named connections, Descriptions, two Comments.
  - The readme says **why it is hand-authored** — a new notation, so no published corpus can exist, per the user's ruling — and what it does not demonstrate: a document with breaches, Base36 ids, an empty Name, more than one Shows per Action.
  - Guard: the validator reports nothing for the example; `ExampleRegistrationTests` opens it against the deployed catalog; every element type and every relation appears at least once, asserted from the parsed model rather than by eye.
  - Seen to fail against: an example missing one relation type, which the per-relation assertion reports.
  - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [x] 16. Catalog and authoring documentation
  - File: `docs/diagrams.md`, `docs/creating-a-diagram-module.md`
  - Move the type's row to its new state as the work lands, keeping the `etalii/functional-decomposition-graph` origin. Document the six library capabilities where module authors are pointed for a definition: the three shapes and their text regions, `resize: "both"`, `wrap`, the multiline editor, `acyclic`, and outline attachment.
  - Guard: the documentation-links test passes; the catalog row's origin matches the `.adp` registration and the client registration's mime match, asserted rather than read.
  - _Requirements: 12.1, 12.2_

- [ ] 17. The coverage diff, before the tasks card and again after implementing
  - File: the implementation log only
  - Run the criterion-to-claim diff over this document and the approved requirements: 49 criteria, and every one claimed or explained. Run it again when the work is done, traced to files and strings rather than to a task's promise.
  - **Both directions matter**: a criterion nobody claims is a gap, and a claim naming a criterion that does not exist is a reference to nothing.
  - _Requirements: 12.3_

- [ ] 18. The browser pass
  - File: `.spec-workflow/steering/tests.md`
  - A `tests.md` entry covering, in a real browser: all five shapes in both themes; text inside each shape at the shared height, at a minimum and a generous width, and a Comment with more text than fits; width resize on the four and width and height on a Comment; each allowed link drawn and each forbidden one refused, including a second parent and a cycle-closing target, with no highlight on any refused target; a connection name appearing and disappearing; a Description never appearing.
  - **jsdom is not evidence for any of these**: it applies no CSS and lays out no text.
  - _Requirements: 13.1, 13.2_

- [x] 19. Close the loop on what the library gained
  - File: the implementation log only
  - Record which of the six capabilities any other module could now adopt, and name the modules that would benefit: `wrap` for c4's hand-written description wrapping, `resize: "both"` for anything user-sized, outline attachment for `diamond`, `hexagon` and `parallelogram` once a module declares one.
  - Purpose: the declarative rule pays off only if the next module finds the capability; this is the note that makes it findable
  - _Requirements: 9.6_
