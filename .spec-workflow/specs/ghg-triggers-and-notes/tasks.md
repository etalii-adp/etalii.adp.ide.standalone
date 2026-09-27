# Tasks Document

**The order follows the design: the library first, then the backend, then the client, then the examples and the checks.** Tasks 1 to 3 are library work that waits on nothing outside this document. **Task 4 and task 8 wait on ghg-compact-mode (PR #94) being merged into `develop`**, because they extend `layout/rowPackedLayout.ts` and the hype cycle's compact declaration, which exist only on that branch; neither is started on that branch or in its worktree. Every other task can land before PR #94.

**Every task's guard is seen to fail before it is trusted** (CLAUDE.md, *Bugs found during implementation or verification*), and each task names the planted defect its guard must fail against. **No library declaration or test names this diagram** (Requirement 8.4).

**The open questions stand at their defaults** (no ruling to the contrary by 2026-09-27): the trigger's name is the text drawn left of it (Q1), its date is written in the diagram's unit (Q2), an influence always ends at a trend (Q3), notes are outside the filter (Q4), notes are placed in compact mode (Q5), and a trigger has no kind (Q6). A ruling that differs is a small amendment to the tasks naming those criteria.

**Coverage, run before this card was raised** (task 12): **53 of 53 acceptance criteria are claimed**, and no task claims a criterion that does not exist. Several are claimed twice, once where the library or backend makes a behaviour possible and once where the module declares it or the browser shows it; both claims are real work.

## The library

- [ ] 1. A filter scoped to element types
  - File: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.test.ts`, `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/DiagramCanvas.filter.test.tsx`
  - Add `FilterDeclaration.elementTypes?: readonly string[]`. In `tagsByElement`, skip elements whose type is not listed, so `filteredOut` never holds them and their tags are not suggested. Omitted means every type. The validator rejects an entry naming no declared type.
  - Guard: with a filter scoped to type `a`, an untagged element of type `b` stays drawn while a tag is chosen, and so does a connection between two `b` elements; an unmatched `a` element is hidden; `b`'s tags are not among the suggestions; without `elementTypes` an untagged element is hidden as today; the validator rejection.
  - Seen to fail against: today's `tagsByElement` (the `b` element is hidden).
  - _Requirements: 8.1, 8.4, 5.3_

- [ ] 2. A named-anchor source whose drawn end attaches by edge
  - File: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.test.ts`, `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/DiagramCanvas.attachment.test.tsx`
  - Add `AnchorEnablement.attachDrawnBy?: "anchor" | "edge"`. For a type declaring `"edge"`, a connection end on one of its elements is drawn at the edge intersection towards the other end, whatever anchor started the gesture, and `connection-drawn` carries no anchor name for that end. A connection from such a source to an `along`-anchored target still carries the target's `EdgeAttachment`. The validator rejects `attachDrawnBy` on `along` anchors.
  - Guard: an `ellipse` type with compass anchors and `attachDrawnBy: "edge"` offers start handles; a connection drawn from its `n` anchor to an element directly to its right leaves the ellipse's right side; `onConnectionDrawn` receives no source anchor and the target's attachment; `perPair: "ordered"` refuses a second such connection and allows the reverse type pairing where the relation admits it; an end handle is drawn on the target end only when `movableEnds` is on; the validator rejection.
  - Seen to fail against: a canvas that ignores `attachDrawnBy` (the line leaves the `n` anchor and the event names it).
  - _Requirements: 8.3, 8.4, 3.1, 3.3, 3.6_

- [ ] 3. The editor opens on the element a toolbox drop created
  - File: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/DiagramCanvas.tsx`, `src/client/src/canvas/library/DiagramCanvas.editOnDrop.test.tsx` (new)
  - Add `ElementTypeDefinition.editOnDrop?: boolean`. On a toolbox drop, remember the drop point (through the active layout's inverse where one exists) and the time. The first model update bringing a new element of a type declaring `editOnDrop` whose bounds contain the point selects it and opens its editable label's editor. Clear the memory on that match, on any other gesture, or after five seconds.
  - Guard: drop, then a model with a new element of an `editOnDrop` type at the point: its editor is open and it is selected; the same with the new element elsewhere: no editor; the same for a type without `editOnDrop`: no editor; a model arriving after five seconds: no editor; a pointer-down between drop and model: no editor.
  - Seen to fail against: a canvas without the memory (no editor opens), and one matching any new element (the elsewhere assertion fails).
  - _Requirements: 7.1, 8.4, 4.2_

- [ ] 4. Row-packed width per type, and elements covering several rows
  - File: `src/client/src/canvas/library/layout/rowPackedLayout.ts`, `src/client/src/canvas/library/layout/rowPackedLayout.test.ts`, `src/client/src/canvas/library/definition/diagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.ts`, `src/client/src/canvas/library/definition/validateDiagramDefinition.test.ts`
  - **Waits on ghg-compact-mode (PR #94) merged into `develop`.** Add `rowPacked.types?: readonly string[]` and `rowPacked.rowStep?: number`. The declared width applies to listed types only; others keep their width. With `rowStep`, an element covers every row line from `floor(top / rowStep)` to `floor((bottom - 1) / rowStep)` and clears `rowEnd` on each; without it, rows are keyed by centre as before. `inverse` is unchanged. The validator rejects a `types` entry naming no declared type.
  - Guard: the existing property tests (time order, no overlap, leftmost, determinism) re-run on a population mixing a listed type, an unlisted narrow type and an unlisted element two rows tall; the unlisted elements keep their widths; nothing overlaps the tall element on either row; a drop between a listed and an unlisted element inverts to a manual x between theirs; without `types`, every element takes the width as before.
  - Seen to fail against: today's `place` (the unlisted width assertion fails), and one keying the tall element by its centre only (the overlap assertion on its first row fails).
  - _Requirements: 8.2, 8.4, 6.1, 6.3, 6.4_

## The backend

- [ ] 5. Triggers and notes in the document
  - File: `src/diagrams/gartner-hypecycle-graph/backend/EtAlii.Adp.Diagram.GartnerHypeCycleGraph/_Model/GhgTrigger.cs` (new), `_Model/GhgNote.cs` (new), `_Model/GhgModel.cs`, `GhgParser.cs`, `GhgWriter.cs`, `GhgRuleSet.cs`, `GhgValidator.cs`; tests `GhgDocument.Tests.cs` and new fixtures `triggers-and-notes.ghg`, `rule-influence-into-trigger.ghg`, `rule-trigger-date.ghg`, `rule-note-position.ghg` in the module's `Fixtures/`
  - Read and write the `triggers` and `notes` lists in the key orders the design gives, a note's text as a literal block when it holds a line break, and an influence from a trigger with an empty `FromEnd` and no `from-*` keys. `AddTrigger` and `AddNote` insert a missing list where the design says. The version stays 1. Add the rules `influence-into-trigger`, `trigger-date` and `note-position`; widen `duplicate-id` and `dangling-reference`; stop `bad-attachment` reporting a trigger's missing `from-*` end.
  - Guard: each new fixture round-trips byte-identical, as does every existing fixture; a document with neither list writes exactly as before; the parser never throws on a malformed trigger or note, keeps it, and reports it; each rule fixture reports exactly its rule; a multi-line note keeps its line breaks through a round trip; an id shared by a trigger and a trend is reported.
  - Seen to fail against: a writer that always writes both list headers (the neither-list assertion fails), and a `bad-attachment` still checking trigger sources (the clean fixture reports it).
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 3.2_

- [ ] 6. Commands, properties, toolbox and the wire
  - File: `src/diagrams/gartner-hypecycle-graph/api/gartner-hypecycle-graph.proto`, `GhgElementMapper.cs`, `GhgToolboxProvider.cs`, `GhgContextActionProvider.cs`, `GhgContextPropertyProvider.cs`, `GhgContextSourceResolver.cs`, `Commands/` (`AddGhgTrigger*`, `AddGhgNote*`, `SetGhgNoteSize*` new; `RemoveGhgTrend*` to `RemoveGhgElement*`, `RenameGhgTrend*` to `RenameGhgElement*`; `SetGhgPlacement*`, `SetGhgSpan*`, `SetGhgTags*`, `SetGhgDescription*`, `AddGhgInfluence*` widened), `ServiceCollection.AddGartnerHypeCycleGraph.cs`; tests `GhgCommands.Tests.cs`, `GhgContextSourceResolver.Tests.cs`
  - Add `GhgTriggerPayload` and `GhgNotePayload` and the trend payload's `snap_x` and `snap_y`, and map triggers and notes at their centres. Format `when` and `when_long` in the diagram's unit (Q2). Offer the Trigger and Note toolbox items with actions `ghg.add.trigger` and `ghg.add.note`. Implement and widen the commands as the design's table says, each with its inverse; refuse an influence into a trigger with *"An influence cannot end at a trigger."*; refuse every edit on a read-only diagram. The property grid offers a trigger's Name, Date, Tags and Description and a note's Text.
  - Guard: every command applied then inverted leaves the document byte-identical; removing a trigger removes its influences and undo restores them; an influence into a trigger and a second from one trigger to one trend are refused with their sentences and change nothing; a trigger's `when` reads `Dec 1947` in a month diagram and `1947` in a year diagram, and `when_long` `December 1947`; the toolbox lists three items; the property groups for each type; a read-only diagram refuses each new command.
  - Seen to fail against: an `AddGhgInfluence` refusal that checks only that `to` exists (the into-trigger assertion fails), and a remove that leaves influences (the undo assertion fails).
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 2.2, 2.3, 2.6, 3.2, 3.3, 3.5, 4.2, 4.3, 5.1_

## The client

- [ ] 7. The hype cycle declares triggers and notes
  - File: `src/diagrams/gartner-hypecycle-graph/client/ghgIds.ts`, `ghgModel.ts`, `GhgCanvas.tsx`, `ghg.css`, `src/client/src/index.css`, `src/client/src/theme.contrast.test.ts`; tests `ghgDefinition.test.ts`, `ghgHandlers.test.tsx`, `ghgConnect.test.tsx`, `GhgCanvas.test.tsx`
  - **Waits on tasks 1, 2, 3 and 6.** Declare the trigger and note types as the design gives them; widen the influence's source to trend and trigger; bind the snap origins; scope the filter to trends and triggers; add the three colour tokens in both themes with their contrast checks. Add the `triggers` and `notes` maps to `ghgModel.ts`. Extend the handlers for the two new drops, each element's own half-size on a move, and a note's resize.
  - Guard: the definition validates; a trigger is 16 by 16 and draws its name and date before it and its tooltip; a dragged trigger's centre lands on a step line and a row's middle; a trigger offers no resize; a trigger is offered as a source and never highlighted as a target; an influence from a trigger hides when its target's phase is hidden; a note draws wrapped text, resizes both ways and has no anchors; a chosen tag hides an unmatched trigger and keeps a note; each drop sends its action; `applyDelta` upserts and removes both new types; every guard that walks module clients passes unchanged.
  - Seen to fail against: a definition with plain snap origins (the centre assertion fails by half a circle), and one with an unscoped filter (the note assertion fails).
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.4, 3.6, 4.1, 4.2, 4.3, 4.4, 4.5, 5.2, 5.3, 7.1, 7.6, 9.1, 9.2_

- [ ] 8. Triggers and notes in compact mode
  - File: `src/diagrams/gartner-hypecycle-graph/client/GhgCanvas.tsx`, `ghgDefinition.test.ts`, `GhgCanvas.test.tsx`
  - **Waits on ghg-compact-mode (PR #94) merged into `develop`, and on tasks 4 and 7.** Declare `rowPacked.types: [trend]` and `rowStep`, and list the trigger (`draggable: false`) and the note (`sizing: "model"`, `draggable: false`) in `modeOverrides["row-packed"].elementTypes`.
  - Guard: in compact mode a trigger is placed between the trends starting before and after its date on its row, at its own size; a note keeps its size and no element overlaps it on either row it covers; neither drags, and the note offers no resize; an influence can still be drawn from a trigger; a trigger dropped between two trends gets a date between theirs; toggling issues no command and leaves the document unchanged.
  - Seen to fail against: an override that omits the trigger (it is drawn at the compact width and stays draggable).
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

## The examples

- [ ] 9. Real triggers in all nine examples
  - File: the nine examples, each in both `src/examples/diagrams/gartner-hypecycle-graph/<name>/` and `src/diagrams/gartner-hypecycle-graph/examples/<name>/` (coal-technologies, digital-trends, electric-vehicles, energy-breakthroughs, eras-of-innovation, internet-evolution, llms-and-agents, technology-trends, warfare-in-ukraine), their readmes, and the example tests on both sides (`GhgExample*.Tests.cs`, `GhgDigitalExample.Tests.cs`, `GhgErasExample.Tests.cs`, `GhgEnergyAndAgentsExamples.Tests.cs` and the client `ghg*Example*.test.tsx`)
  - **Waits on tasks 5 and 7; the compact-mode check in step 4 waits on task 8.** For each example follow the design's four steps: rewrite trends that are moments as triggers, add real dated triggers with influences, re-lay out the rows so related elements sit together and no trigger label runs into a trend, and check it in the browser in true-time and compact mode. Give at least one example a note. Keep each example's two copies identical.
  - Guard: every example validates with nothing reported; each holds at least one trigger with an influence; the two copies of each are byte-identical; each readme lists the trends that became triggers and says the dates are illustrative; the counts in the tests match.
  - Seen to fail against: one example left without a trigger (its at-least-one assertion fails).
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

## Documentation and checks

- [ ] 10. Documentation
  - File: `docs/creating-a-diagram-module.md`, `docs/diagrams.md`, `src/diagrams/gartner-hypecycle-graph/client/readme.md`
  - Document the four library additions where the definition is described; mention triggers and notes on the `gartner/hypecycle-graph` row, naming this specification; describe both types in the module's readme. After the pull request merges, send the coordinator the note that the Notion *Gartner HypeCycle Graph* entry's implementation details, examples and purpose need updating, with what changed, for the thread that owns that page.
  - Guard: `ArchitecturePages.Tests` and every documentation guard pass.
  - _Requirements: 11.1, 11.2_

- [ ] 11. The browser pass
  - File: `tests.md`
  - **Waits on tasks 7, 8 and 9.** Write and run the entry Requirement 11.3 lists, in a real browser in both themes, signed in with the checked-in placeholder, and record the result of each step.
  - Guard: every step recorded as run, with its outcome; none recorded `pending`.
  - _Requirements: 11.3, 11.4, 2.1, 2.3, 4.1_

- [ ] 12. Coverage, before and after
  - File: this document
  - Diff the requirement references in this document against the acceptance criteria in `requirements.md`, before raising the tasks card and again after implementing, tracing the second time to files and strings; read two traces back to their artefacts.
  - _Requirements: 11.5_
