# Tasks Document

> **One gate, and it is a document rather than code.** The approved requirements carry two statements that are no longer true, both named in the design's *Deviations and notes*. **Task 1 corrects them before anything implements against them.** Requirement 12.4 says the spec anticipates no core change, and one has since landed; the `Code style` non-functional clause commits to a gate that currently passes for nobody. Everything else can start today.
>
> **The core change is already in.** Unlike every earlier diagram-type spec, this one needs nothing added to core: `MoveElementRequest.position` and `IDiagramSession.MoveElementToAsync(elementId, x, y, ct)` merged with `claude/c4-drag`, and `C4Session` no longer encodes coordinates into `new_parent_id`. Consume them; do not re-derive a way to carry a drag.
>
> **Task 2 first, or Requirement 3 is untested.** The byte-identical round trip is this module's headline correctness property, and it is only as good as the corpus it round-trips. Real `.owm` files, not hand-written approximations — the C4 spec learned this the expensive way, and its corpus found six defects on the day it landed.
>
> **The document is the only writer.** No component below writes `.owm` except through a command handler acting on `WardleyDocument`. If a task tempts you to serialise the model back to a file, the task is wrong, not the design.
>
> **There is no layout, and there is no task for one.** Positions come from the document and go back to it. If a task seems to need a layout, re-read Requirement 7.1 — the absence is the design.
>
> **The axis flip has exactly one home.** `[visibility, maturity]` becomes `Point2D(x: maturity, y: 1 - visibility)` in `WardleyElementMapper` and nowhere else. A second conversion anywhere in the module is a defect, however local it looks.
>
> **Worktree.** Per CLAUDE.md, implement in one dedicated worktree for this spec (`.claude/worktrees/wardley-map/`). For the manual pass use a free port pair and revert `vite.config.ts` and `appsettings.developer.json` before merging.
>
> **Regenerate the client stubs.** Task 11 adds `wardley-map.proto`. `src/client/src/generated/*_pb.ts` is gitignored for new protos, so a stale copy fails silently. Run `npm run generate` in `src/client/` as part of it.
>
> **Recent steering rules apply.** No nested types (lift anything you are tempted to nest); every test carries `// Arrange.` / `// Act.` / `// Assert.`; the module registers through one `AddWardleyMap` extension method rather than lines in `Program.cs`; Serilog through a `private static readonly ILogger` per class.
>
> **Bugs.** Per CLAUDE.md, any bug found while implementing or verifying a task is covered by a unit or integration test, or recorded in `tests.md`.

## Phase A — the gate, and the corpus

- [x] 1. Correct the two stale statements in the approved requirements
  - File: `.spec-workflow/specs/wardley-map/requirements.md` (edited)
  - Requirement 12.4: replace "This spec anticipates no core change at all" with the truth — one core change was found, reported per Requirement 12.5, accepted, and implemented elsewhere: `MoveElementRequest.position` and `IDiagramSession.MoveElementToAsync`. Say that this module **consumes** it rather than adding it, and that it removed C4's `"x,y"` encoding rather than adding a special case
  - The `Code style` bullet under *Code Architecture and Modularity*: replace "SHALL satisfy `src/.editorconfig` as `dotnet format style --verify-no-changes --severity info` checks it" with "SHALL introduce no new style-diagnostic categories beyond those the repository already reports", citing `quality-gates` Requirement 5 for the gate itself. Measured on an untouched checkout the gate reports 115 `IMPORTS` errors, 131 `IDE0130` and 19 `IDE0046`; `IDE0130` fires on the `_Model`/`Commands` folder convention `tech.md` and `structure.md` mandate, so a module obeying the steering documents cannot avoid it
  - This is an edit to an **approved** document, so it needs a fresh approval request and a dashboard approval before task 3 starts. Requirements 1–11 and 13–15 are untouched, and the numbering other specs cite stays fixed
  - Purpose: nothing should be implemented against a requirement that is known to be false
  - _Leverage: .spec-workflow/specs/wardley-map/design.md (Deviations and notes, which states both corrections)_
  - _Requirements: 12.4, 12.5_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: technical writer maintaining a requirements document | Task: Correct Requirement 12.4 and the Code style non-functional clause to match what is true, then request approval | Restrictions: change only those two statements; do not renumber anything, since c4-diagrams, plantuml-uml and azure-pipeline-diagram cite this document by number; do not proceed past this task on a verbal approval | Success: both statements are accurate, an approval request exists against the edited file, and every other requirement is byte-identical_

- [x] 2. Assemble the `.owm` round-trip corpus
  - File: `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/Fixtures/*.owm` (new), `Fixtures/readme.md` (new)
  - Commit real maps: examples published by onlinewardleymaps.com, maps from the ecosystem's own samples, and at least one substantial map using the strategy vocabulary end to end. Add hand-made edge cases: comments in every position, blank-line runs, both CRLF and LF, no trailing newline, the **legacy** two-coordinate pipeline form beside the nested form, an `annotation` pinned at several coordinates, `label` offsets, a `style` line, and at least two statements this spec does not model at all
  - The readme records each file's provenance and what it exists to prove. A file written by hand is never called canonical
  - Purpose: every byte-identical claim in Requirement 3 rests on these files, and Requirement 3.3's forward compatibility is only demonstrated by statements the parser genuinely does not know
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Fixtures/readme.md (the provenance convention this follows)_
  - _Requirements: 3.1, 3.2, 3.3, 5.4, 6.8_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer familiar with the OnlineWardleyMaps DSL | Task: Assemble a round-trip corpus of real .owm maps plus deliberate edge cases, and document each file's provenance | Restrictions: do not hand-write a file and call it canonical; keep both CRLF and LF samples verbatim; do not normalise comments, blank lines or coordinate formatting; include statements the module will not model | Success: the corpus covers both pipeline forms, multi-coordinate annotations, label offsets and unknown statements, every file's origin is recorded, and the readme states what each edge case guards_

## Phase B — the document

- [x] 3. Declare the document extension
  - File: `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap/Diagram.cs` (edited)
  - Add `public const string DocumentExtension = ".owm";`, name the single definition (`public static DiagramDefinition WardleyMap { get; }`) so the module's own registrations can refer to it without indexing the array, and pass `Extension: DocumentExtension`. The origin, title and description already exist and do not change
  - Purpose: `DiagramFileRouter` and `DiagramFilePair` need nothing else to route both an `.adp` registration and a bare `.owm`
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/Diagram.cs (the exact shape, including the named property)_
  - _Requirements: 2.2, 2.5, 12.1_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Declare the .owm extension on the existing Diagram.Definitions entry, following the mindmap module's shape | Restrictions: do not change the origin, title or description; do not add a second definition; note that declaring an extension makes an IDiagramDocumentFactory mandatory at startup, so task 10 must land before the host runs | Success: the type still appears in DiagramDefinition.All, HasDocumentSibling is true, and DiagramFileRouter.AmbiguousExtensions() does not report .owm_

- [x] 4. `WardleyDocument`: lines in, lines out
  - File: `.../WardleyDocument.cs`, `_Model/WardleyStatement.cs` (new), `.../WardleyDocument.Tests.cs` (new)
  - Parse into a line list preserving content, indentation and line endings exactly. `ToText()` returns the input byte-for-byte for an unedited document, including `Newline` and `EndsWithNewline`. `ReplaceLine`, `InsertLine` and `RemoveLines` splice one range and leave every other line untouched. Line numbers are 1-based `uint`, matching `DiagramProblemLineLocation`
  - Purpose: the round-trip guarantee, by construction rather than by effort
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4Document.cs (the same API, one grammar over)_
  - _Requirements: 3.1, 3.2, 3.3_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer building a concrete syntax tree | Task: Implement a line-preserving .owm document with splice operations, and prove byte-identical round trips over the whole corpus | Restrictions: never re-serialise; never normalise indentation, comments, blank lines or line endings; a splice touches only its own range; do not invent a statement model here, that is task 5 | Success: every fixture round-trips byte-identically including CRLF files and files with no trailing newline, and a splice test proves the neighbouring lines are unchanged_

- [-] 5. Parse the map: components, links and pipelines
  - File: `.../WardleyParser.cs`, `_Model/WardleyMap.cs`, `WardleyComponent.cs`, `WardleyLink.cs`, `WardleyPipeline.cs`, `WardleyElementKind.cs`, `WardleyCoordinate.cs` (new), tests
  - Read `component`, `anchor`, `submap` — the DSL's **three** statement kinds, per the approved Requirement 5.1 — plus links (`->`, `+>`, and the `; context` form), and `pipeline` in **both** the nested and legacy forms, recording for every element the line it was declared on. A pipeline child carries only an evolution position and takes its visibility from the parent. Also read the map-level `title`, `size` and `style`. *(This line previously listed `market` and `ecosystem` as statement kinds. They are decorators — `component X [..] (market)` — and belong to task 6; `market X [..]` is a parse error the task 2 corpus demonstrated.)*
  - Coordinates are stored as the document writes them — `[visibility, maturity]` — and are **not** converted here
  - Purpose: the model, and the line index the splices need
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.6, 5.7_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer writing a recursive-descent parser for a line-oriented DSL | Task: Parse the map's elements into a model, recording each element's declaring line, and remember which pipeline form each was written in | Restrictions: keep coordinates in the document's own visibility/maturity order and do not convert to Point2D here; do not drop statements you do not model; parse from a plain string with no filesystem or gRPC dependency | Success: the corpus parses, both pipeline forms round-trip in the form they were read, a pipeline child's visibility resolves to its parent's, and every element's line points at its own declaration_

- [ ] 6. Parse the strategy vocabulary
  - File: `.../WardleyParser.cs` (edited), `_Model/WardleyEvolveTarget.cs`, `WardleyDecorator.cs`, `WardleyNote.cs`, `WardleyAnnotation.cs`, `WardleyArea.cs` (new), tests
  - `evolve` including the `evolve Name->NewName x` renaming form, `inertia`, the `(build)` / `(buy)` / `(outsource)` decorators, `pioneers` / `settlers` / `townplanners` areas, `accelerator` and `deaccelerator`, `note`, `url`, `submap` targets, `label [dx, dy]` offsets, and `annotation` — which carries `IReadOnlyList<WardleyCoordinate> Positions`, because the DSL permits one numbered annotation pinned in several places and none of them may be lost
  - Purpose: what makes a Wardley map a strategy tool rather than a scatter plot
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Parse the strategy vocabulary into the model, giving an annotation a list of positions rather than one | Restrictions: preserve the label offset in pixels rather than converting it to map coordinates; do not silently normalise a decorator's spelling; an unrecognised statement is preserved, never an error | Success: every construct in Requirement 6 parses, a multi-position annotation keeps all its coordinates, and an evolve with a rename carries the arrival name_

- [ ] 7. The writer: surgical edits only
  - File: `.../WardleyWriter.cs` (new), tests
  - Given a model change and the original document, rewrite **only** the lines the change occupies. A coordinate edit rewrites one statement's coordinate pair and nothing else. A rename rewrites the declaration and every statement referring to the old name — links, `evolve`, pipeline membership — in one pass. A pipeline written in the legacy form is written back in the legacy form
  - Purpose: Requirement 3.2, and the reason a map ADP touched still reviews as a one-line diff
  - _Requirements: 3.2, 4.4, 5.4_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement surgical rewrites over WardleyDocument, including the multi-statement rename | Restrictions: never regenerate the document from the model; never reformat, reorder or re-indent a line the edit does not touch; preserve the pipeline form the file used | Success: editing one coordinate in a map full of comments changes exactly one line, and a rename updates the declaration and every reference while leaving everything else byte-identical_

- [x] 8. `WardleyEvolution`: the constants, pinned
  - File: `.../WardleyEvolution.cs` (new), tests
  - The three boundaries — 0.175, 0.400, 0.700 — the four stage labels (`Genesis`, `Custom Built`, `Product (+rental)`, `Commodity (+utility)`), and `StageOf(double maturity)`. A comment records the derivation from the reference renderer's own `EvoOffsets` (`custom: 3.5`, `product: 8`, `commodity: 14`, each divided by 20), because these numbers are not published in the DSL documentation and nobody should re-derive them from a search result
  - Purpose: one home for numbers that would otherwise be guessed
  - _Requirements: 8.2_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Pin the evolution stage boundaries and labels with a test and a comment citing their derivation | Restrictions: do not round the constants; do not duplicate them anywhere else in the module or the client; state the EvoOffsets derivation in the comment | Success: StageOf is exact at each boundary, the test asserts the derivation arithmetic rather than the literals alone, and the constants appear once in the codebase_

- [-] 9. `WardleyIdentities`: the sidecar
  - File: `.../WardleyIdentities.cs`, `_Model/WardleyIdentityEntry.cs` (new), tests
  - `<name>.identities.json` beside the `.owm`, holding `{ id, kind, key }` entries. Keys: a component-shaped element by its name; a link by source name, target name and kind; a pipeline by its parent's name; a note by its text; an annotation by its number. `Reconcile` matches by key, assigns a fresh `ShortGuid` to an unmatched file element, and discards an unmatched entry. Reads never throw — a missing, unreadable or nonsense sidecar yields no entries
  - A map opened and not edited writes nothing: assigned ids live in memory until the first real save
  - Purpose: identity the format does not provide, without annotating another ecosystem's file
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4LayoutSidecar.cs (PathFor, the never-throw reads, the optimisation-not-dependency posture)_
  - _Requirements: 4.1, 4.2, 4.3, 4.5, 3.7_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the identity sidecar with key-based reconciliation and best-effort reads | Restrictions: never write an ADP id into the .owm itself, not even as a comment; never let a sidecar problem prevent a map from opening; do not write on open, only on the first real save | Success: identities survive a reload, a deleted sidecar re-derives silently, a corrupt sidecar still opens the map, and opening without editing produces no write_

- [x] 10. The document store and the empty-document factory
  - File: `.../IWardleyDocumentStore.cs`, `.../WardleyDocumentStore.cs`, `.../WardleyDocumentFactory.cs` (new), tests
  - One loaded document per path, shared by every connection viewing it, with a `Changed` event for the watcher reload. The factory produces the empty body of Requirement 1.4 — a `title` line carrying the file's base name — and is registered by origin. Saves go through the backend's file-access layer, atomically (temp file in the same folder, then move)
  - Purpose: several sessions on one map must share the instance that holds it, and Add must be able to create one
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4DocumentStore.cs, C4DocumentFactory.cs_
  - _Requirements: 1.3, 1.4, 1.6, 2.3, 2.4, 3.6, 10.7_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the per-path document store, its change notification, and the empty document factory | Restrictions: register the store with TryAddSingleton so several sessions share it; write atomically through the file-access layer only; a missing .owm sibling opens as an empty map rather than failing | Success: two sessions on one path share a document, an external file change raises Changed, a new map's body is a valid minimal document, and a failed write leaves the previous file intact_

## Phase C — the wire

- [ ] 11. `wardley-map.proto` and the element mapper
  - File: `src/diagrams/wardley-map/api/wardley-map.proto` (new), `.../WardleyElementMapper.cs` (new), tests, `npm run generate` in `src/client/`
  - The payloads: `WardleyElement` (name, kind, visibility, maturity, evolve target, inertia, decorator, label offset, url, submap target, resolved evolution stage), `WardleyLinkElement`, and `WardleyEvolutionAxis` carrying the four stages with their boundaries and labels. The mapper emits the baseline, diffs two models into `Add`/`Remove`, expresses a pipeline with `Group`/`Ungroup`, and owns `ToPoint` / `ToCoordinates` — **the only** place `[visibility, maturity]` becomes `Point2D(x: maturity, y: 1 - visibility)`
  - The evolution axis goes out as one map-level element so the client holds no copy of the constants
  - Purpose: the whole map in the existing element and delta vocabulary, with no core contract file touched
  - _Leverage: src/diagrams/c4/api/c4.proto, src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ElementMapper.cs_
  - _Requirements: 5.2, 8.2, 10.2, 10.3, 10.4_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working with protobuf and a delta protocol | Task: Define the module's element payloads and map the model onto core Elements and Deltas, with the axis conversion in exactly one function pair | Restrictions: do not modify any core contract file; add no new Delta action; keep the conversion out of the parser, the commands and the canvas; regenerate the client stubs | Success: a round-trip property test proves ToPoint and ToCoordinates invert each other, an explicit test asserts that [0.9, 0.1] renders top-left, a pipeline emits Group, and the baseline carries one evolution-axis element_

- [ ] 12. `WardleySession` and its factory
  - File: `.../WardleySession.cs`, `.../WardleySessionFactory.cs` (new), tests
  - `Baseline()` returns the whole map. `UpdateView(viewport)` returns the whole map unfiltered — the shortest correct implementation of that method in the repository, and deliberately so. `MoveElementToAsync` converts through the mapper, clamps to `0..1`, narrows a pipeline child to maturity alone, and dispatches `MoveWardleyElementCommand`; `MoveElementAsync` refuses, except for a pipeline child where re-parenting is meaningful. `Changed` carries an external reload as deltas, never onto the history
  - Purpose: the fourth registration seam, and the leg a drag arrives on
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4Session.cs (which now implements both move methods, each refusing the other's gesture)_
  - _Requirements: 7.2, 7.3, 7.4, 7.5, 10.1, 10.5, 10.6, 10.7, 10.8_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the diagram session, answering viewports with the whole map and turning a positional drag into a command | Restrictions: implement no viewport filtering; do not record an external file reload on the history; refuse a drag in read-only mode with a reason; a pipeline child's drag changes maturity only | Success: a viewport change returns the whole map, a drag lands as a command with an inverse, undo restores the previous coordinates, and an external edit arrives as deltas that undo does not step through_

## Phase D — the client

- [ ] 13. The stream hook and the client model
  - File: `src/diagrams/wardley-map/client/useWardleyStream.ts`, `wardleyModel.ts` (new), tests
  - `useWardleyStream` opens the diagram and reports viewports; `applyDelta` folds `Add`/`Remove`/`Group`/`Ungroup` into a client model of elements, links, pipelines and the evolution axis
  - Purpose: the canvas renders a model rather than a delta stream
  - _Leverage: src/diagrams/c4/client/useC4Stream.ts, c4Model.ts_
  - _Requirements: 10.1, 10.3, 10.4_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Implement the stream hook and the delta-folding client model | Restrictions: treat Add as an upsert keyed on element id; hold no evolution constants, read them from the axis element; follow the client editorconfig sections for indentation and quotes | Success: applyDelta tests cover add, replace, remove and group, and the model exposes the axis the canvas needs_

- [ ] 14. The canvas: the space, the axes and the bands
  - File: `.../WardleyCanvas.tsx`, `wardley.css` (new), tests
  - One `<svg>` owning its `viewBox`, pan and zoom. Draw order: bands and axis labels first, then everything else. Visibility is vertical with the user need at the top, maturity horizontal with genesis at the left. The four stages come from the axis element, never from a constant in this file. Chrome scales with the map; stage **labels** may hold a readable size
  - Purpose: a component's position is meaningless without the axes it is claimed against
  - _Leverage: src/diagrams/c4/client/C4Canvas.tsx (surface, viewBox and wheel handling), src/diagrams/mindmap/client/MindmapCanvas.tsx_
  - _Requirements: 8.1, 8.2, 8.4, 8.5_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer working in SVG | Task: Render the bounded coordinate space with its two semantic axes and four evolution bands, beneath everything else | Restrictions: no inline styles, per tech.md; do not hardcode the stage boundaries in the client; do not ask core for a background layer, the module owns its whole surface | Success: an empty map shows its axes and nothing else, the bands sit at the boundaries the backend sent, and the chrome scales with the map under pan and zoom_

- [ ] 15. The canvas: elements, links and what is claimed about them
  - File: `.../WardleyCanvas.tsx` (edited), tests
  - Element kinds visually distinguishable — anchor, component, submap — and a component's decorators (`market`, `ecosystem`, `build`, `buy`, `outsource`) distinguishable on it, per Requirement 6.3. Links and `evolve` movement indicators drawn with `anchorsBetween` and `straightPath` from `@client/canvas/connectors`; an `evolve` indicator is the same call, styled dashed, from current maturity to target at the same visibility. Inertia, the build/buy/outsource decorators, notes, annotations at every one of their positions, areas behind their elements, and accelerators. Problem marks on elements a rule reported
  - `branchAnchorsBetween` and `horizontalBezierPath` are **not** used — they are tree-shaped and this notation has no tree. A pipeline is a bracket the module draws, not a connector
  - Purpose: Requirement 6.9 — the annotations are visible without entering an edit mode
  - _Leverage: src/client/src/canvas/connectors.ts (anchorsBetween, straightPath)_
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.7, 6.8, 8.3, 8.6_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer working in SVG | Task: Render the element kinds, their decorations, the links and the evolve indicators, and mark elements a validator reported | Restrictions: use the shared connector geometry for links rather than writing edge intersection maths; draw every position of a multi-position annotation; no inline styles | Success: each kind is distinguishable, an evolving component shows both positions joined, a multi-position annotation appears at each place, and a dangling link's endpoints are marked_

- [ ] 16. Dragging, which is an edit
  - File: `.../WardleyCanvas.tsx` (edited), tests
  - A drag clamps to `0..1` on both axes and sends `MoveElement` with the new `position`. Read-only maps do not offer it. The moved component returns as an ordinary `Add` delta rather than being moved optimistically and reconciled
  - Purpose: the type's central interaction, on the leg that now exists to carry it
  - _Leverage: the merged MoveElementRequest.position field and IDiagramSession.MoveElementToAsync_
  - _Requirements: 7.2, 7.3, 7.5_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Implement dragging a component as a document edit sent over MoveElement with a position | Restrictions: do not encode coordinates into new_parent_id, that workaround has been removed; clamp client-side and rely on the backend clamping again; do not offer dragging in read-only mode | Success: a drag rewrites one line of the .owm, is one undo away, reaches a second connection as deltas, and is refused with a reason on a read-only map_

- [ ] 17. Register the client module
  - File: `.../register.ts` (new), tests
  - One `DiagramCanvasRegistration` matching `wardley/map`, exporting `registrations`, discovered by the shell's `import.meta.glob` scan with no edit to the shell
  - Purpose: the client half of pluggable registration
  - _Leverage: src/diagrams/c4/client/register.ts, src/diagrams/mindmap/client/register.ts_
  - _Requirements: 8.7, 12.3_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Export the module's canvas registration so the shell discovers it | Restrictions: do not edit the shell or its registry; match only wardley/map | Success: opening a .adp naming wardley/map renders the canvas rather than the placeholder, and a registration test asserts the predicate_

## Phase E — commands, selection and actions

- [ ] 18. The commands and their inverses
  - File: `.../Commands/*.cs` (new, one file per command-and-handler pair), tests
  - `AddWardleyElementCommand`, `RemoveWardleyElementCommand`, `MoveWardleyElementCommand`, `RenameWardleyElementCommand`, `SetWardleyLinkCommand`, `SetWardleyEvolveCommand`, `SetWardleyInertiaCommand`, `SetWardleyDecoratorCommand`, `SetWardleyPipelineMembershipCommand`. Each reports its inverse as `CommandResult.Inverse`, validates its own preconditions against current state, and returns a message rather than throwing when it cannot proceed
  - The rename's inverse restores every statement the rename rewrote, so an undo never leaves a half-renamed file
  - Purpose: undo, multi-client delivery and validation come from the system rather than from this module
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/Commands/, src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/Commands/_
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer implementing the command pattern | Task: Implement the module's commands and handlers, each with an inverse and its own precondition checks | Restrictions: nothing writes the .owm outside a handler; a handler returns a CommandResult with a message rather than throwing; validate against current state because undo and redo dispatch again later | Success: every command's inverse restores the prior file byte-for-byte, a rejected command leaves the document unchanged, and an undone rename restores every reference_

- [ ] 19. Make an element selectable
  - File: `.../WardleyContextSourceResolver.cs` (new), tests
  - One `IContextSourceResolver`, resolving an element from the `.adp` path and `element_id`, pushing backend-resolved detail: name, kind, coordinates and evolution stage
  - Purpose: the selection chain, with no change inside the context service
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ContextSourceResolver.cs_
  - _Requirements: 11.1, 11.2, 11.3_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Register a context source resolver that makes a Wardley element selectable and self-describing | Restrictions: reuse ContextSource.element_id and the DIAGRAM_ELEMENT scope, do not add a variant; do not change anything inside the context service | Success: selecting an element on the canvas pushes a nested selection whose innermost level carries the element's name, kind, coordinates and stage_

- [ ] 20. Context actions
  - File: `.../WardleyContextActionProvider.cs` (new), tests
  - The edits of Requirement 9.3 offered as data with their shortcuts, withheld where they do not apply — an unlink on an element with no link, any edit in read-only mode
  - Purpose: one path to the ribbon, the right-click menu and the keyboard
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ContextActionProvider.cs_
  - _Requirements: 11.4, 11.5, 11.6, 9.8_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Contribute the module's context actions as data, dispatching the same commands the canvas uses | Restrictions: the client holds no key-to-action table, so every shortcut is described by the backend; an action that would fail is not offered | Success: the offered set matches what would succeed, each action dispatches its command through the history, and read-only mode offers no edits_

- [ ] 21. The toolbox
  - File: `.../WardleyToolboxProvider.cs` (new), tests
  - Entries for Component, Anchor, Market, Ecosystem, Submap, Pipeline, Note and Annotation, each naming a `DropActionId` the action provider already contributes. A dropped component-shaped entry is created at the centre of the visible viewport and selected, so the user's next drag places it — the drop position is not discarded in favour of a computed one, because there is no computed one. **Link is deliberately not an entry**: it needs two endpoints and is created through a context action
  - Purpose: `tech.md`'s toolbox aspect, implementable and testable before the panel that shows it exists
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ToolboxProvider.cs_
  - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Contribute the module's toolbox entries as data, each reusing an existing action id for its drop | Restrictions: do not add a link entry, and say why in the code comment; a drop in read-only mode is refused with a reason rather than the palette hiding entries; entries are static per type | Success: the entries assert without a UI, each DropActionId resolves to a real action, and a drop creates one element through the same command the menu uses_

- [ ] 22. The property grid
  - File: `.../WardleyContextPropertyProvider.cs` (new), tests
  - One `IContextPropertyProvider` for `ContextScope.DiagramElement`. The rows of Requirements 15.2–15.4, grouped into identity, position, strategy and links. Editable: name, both coordinates, evolve target, inertia, decorator, link context. Read-only with a reason: the derived evolution stage, a pipeline child's visibility, and everything in read-only mode. An absent property is not contributed at all; an empty one is contributed empty
  - Reasons follow the house form — cause, then remedy — as `MindmapContextPropertyProvider` established. They are **user-facing error text**: `ContextPropertyResolver` returns the reason verbatim when it refuses a write
  - Purpose: the numbers behind a position, readable and correctable without a text editor
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ContextPropertyProvider.cs, src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapContextPropertyProvider.cs (the reason phrasing)_
  - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7, 15.8, 15.9, 15.10, 15.11_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Contribute a Wardley element's properties, with derived values shown and refused, dispatching the same commands the canvas uses | Restrictions: every ReadOnlyReason must be non-blank, because both IsEditable and the client's PropertyRow decide editability on length and a whitespace-only reason would silently permit a write; keep reasons to two sentences since the row does not truncate; do not omit a property to express that it is unwritable, omission means absent; commit on Enter or blur, never per keystroke | Success: a test asserts no contributed reason is null or whitespace, editing maturity from the grid and dragging to the same place produce one identical command, a rejected coordinate leaves the document unchanged, and the derived stage refuses a write with a reason naming maturity_

- [ ] 23. Wire the module into the host
  - File: `.../ServiceCollection.AddWardleyMap.cs`, `.../ServiceCollection.AddWardleyMapCommands.cs` (new), `src/backend/EtAlii.Adp.Backend.Service/Program.cs` (edited)
  - One `AddWardleyMap` gathering all eight registrations: document factory, session factory, source resolver, action provider, toolbox provider, validator, property provider and the command handlers
  - Purpose: the whole integration surface in one reviewable method
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/ServiceCollection.AddC4.cs_
  - _Requirements: 12.3_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Gather every registration into one AddWardleyMap extension method and call it from the host | Restrictions: one line per seam; no diagram-type knowledge added to core; the module project is already referenced by the host's wildcard ProjectReference | Success: the host starts, the type is discovered, DiagramDocumentFactories.Verify passes, and every seam resolves by origin_

## Phase F — validation

- [ ] 24. `WardleyRuleSet` and the validator
  - File: `.../WardleyRuleSet.cs`, `.../WardleyValidator.cs` (new), tests
  - Errors: a link, `evolve` or pipeline membership naming a component that does not exist; a coordinate outside `0..1`; two components sharing a name. Warnings: a map with no `anchor`; a `submap` naming a map not in the project, or an unresolvable `url`. Each problem carries a line location where the rule judged text and an element location where it judged the model, and a `RuleId` prefixed `wardley.`
  - The rule set is a pure function from model to problems, testable from a plain string, and bounded in time
  - Purpose: a dangling link is visible where it is, not only in a list
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4RuleSet.cs, C4Validator.cs_
  - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7, 14.8, 14.9, 3.5_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the module's rules as a pure function and register the validator that carries them to the panel | Restrictions: a semantically broken map still opens and still renders; validation takes a model and returns problems with no filesystem, canvas or connection; prefix every RuleId with the module's short name | Success: one test per rule from a plain .owm string, a dangling link reports both a line and an element location, and a map with a broken link opens with the rest of it rendered_

## Phase G — closing

- [ ] 25. Integration tests across the whole arc
  - File: `.../EtAlii.Adp.Diagram.WardleyMap.Tests/*.Integration.Tests.cs` (new)
  - Add a map through the real create-file path and assert both files; open an `.owm` with no `.adp` sibling through the router; open over `DiagramService.Open`, drag, assert the deltas and the rewritten file, undo, assert the file is byte-identical to before the drag; select an element and assert the pushed selection, its actions and its properties; two connections on one map; an external edit arriving as deltas that undo does not step through
  - Purpose: the seams meet, which no unit test shows
  - _Requirements: 1, 2.5, 7.2, 10, 11, 15_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer writing integration tests against a real host | Task: Cover create, open, drag, undo, select, describe properties, multi-connection and external reload | Restrictions: run against the real host rather than mocks; assert file bytes for the undo case; do not add test-only seams to production code | Success: the whole arc passes from a clean checkout with dotnet test --solution EtAlii.Adp.slnx_

- [ ] 26. The catalog, the manual pass and interoperability
  - File: `docs/diagrams.md` (edited), `tests.md` (edited)
  - Move the `wardley/map` row to 🛠️ when implementation starts and ✅ when it lands, and add the `.owm` DSL reference to its Notes. Record in `tests.md` the checks a unit test cannot express: a map authored in onlinewardleymaps.com opens looking like the same map; the four stage labels are legible at default zoom; dragging a component and reopening the file in that tool shows it moved
  - Purpose: the catalog is how "which diagram types does ADP have" is answered, and it must not answer wrongly
  - _Requirements: 12.6_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer maintaining project documentation | Task: Update the diagram catalog row and record the manual interoperability checks | Restrictions: follow CLAUDE.md's state icons and origin tag convention; each tests.md entry names the spec and task it came from and states preconditions, actions and expected result | Success: the row reflects reality at each state, and a later reader can execute each manual check without this conversation_

- [ ] 27. The pluggability check
  - File: none (a review, with findings recorded)
  - Grep core — canvas, storage, sync, context, command code — for any Wardley-specific type check, import or branch, and confirm there is none. Confirm the three shipped notations now differ in who owns position (computed and never written; computed with authored overrides in a sidecar; authored in the document and written back) and that core is unchanged by that difference apart from the positional-move seam, which is type-agnostic and serves two of them
  - Purpose: Requirement 12.5 calls this the acceptance test for the pluggable diagram-type model, and an untested acceptance test is an assertion
  - _Requirements: 12.4, 12.5_
  - _Prompt: Implement the task for spec wardley-map, first run spec-workflow-guide to get the workflow guide then implement the task: Role: reviewer auditing an architectural boundary | Task: Verify that core contains no Wardley-specific code and that the three notations differ in layout ownership without core knowing | Restrictions: report a finding rather than quietly fixing core; a core change discovered here is escalated, not taken | Success: the grep is clean, the three-way difference is demonstrated, and any finding is written down rather than absorbed_
