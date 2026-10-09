# Tasks Document

**The order follows the design: the definition first, then what this host's core gains, then the module.** Tasks 1 to 4 are delivered in etalii.adp; tasks 5 to 15 are core, the canvas library and the two language runtimes; tasks 16 to 24 are the module; tasks 25 to 29 are its examples, tests, documentation and checks. Each task names the tasks it waits on.

**The rulings this is written against.** The requirements were approved on 2026-10-09 with Q1 to Q7 at their defaults, and the design on 2026-10-09 with language decisions L1 to L8 at theirs: `groupBy` on a compartment (L1), a stored state only while it differs from its default (L2), a kept canvas filter (L3), pin and unpin as standard entries (L4), `tiers` on the force layout with the reference algorithm in the `.md` (L5), an `open` action and `itemLink` (L6), view data bound onto entries of the body with FBL unchanged (L7), and a relation as a key on an element (L8). L9 was ruled in chat on 2026-10-09 in place of its default: tasks and pull requests are ordered by when they were last updated, most recent first, and never by hand. The same ruling applied four amendments to the requirements, which this document reads in their amended form.

**One task can change what follows it, and says so to the user as a selection before anything is built on the answer:** task 1, which tries the file shape against the languages. If a reference cannot source a drawn relation, L8 falls back to a `relations:` list, and tasks 3, 4, 16, 19 and 22 change with it.

**Tasks 1 to 4 are pull requests into etalii.adp's `develop`**, under that repository's own rules; it plans with Spec Kit, so whoever takes them opens the feature there that its constitution asks for and cites this document from it. No task from 16 on starts before the definition it implements is merged there (Requirement 1.1), and each such task names the etalii.adp commit it read.

**For every task.** Run `spec-workflow-guide` first. Mark the task `[-]` in this file when starting it. Re-read the seams the task names before changing them, since the design's names come from a survey on one day. Write the guard first and **see it fail against the planted defect the task names** before trusting it (Requirement 11.4, which every task holds in that line). Record the work with `log-implementation`, then mark the task `[x]` once its commit is on `develop`. One Developer owns this specification until every task is done.

**Coverage, run before this card was raised** (task 29): **89 of 89 acceptance criteria are claimed**, and no task claims a criterion that does not exist. Every claim was then read back against its criterion's text, which moved two that the count could not see: 1.5 and 10.7 had each been claimed by a task whose work was another criterion's. Four are rules about the documents and the evidence, not work a task builds, and are held by the task named for each: Requirement 1.5 (everything for etalii.adp is a pull request into its `develop`) by task 4, the last of those pull requests; Requirement 1.6 (the definition files win over this specification) by task 4, which records the rule in `agent-activity-diagram.md`; Requirement 11.3 (jsdom is not evidence) by task 28, the only task whose evidence is a browser; and Requirement 11.4 by every task's *Seen to fail against* line, claimed once by task 29, which checks that no task lacks one.

## The definition, in etalii.adp

- [ ] 1. Try the file shape against FBL and DISL
  - File (etalii.adp): `definitions/diagrams/agent-activity-diagram.fbl` (draft), `specifications/fbl/fixtures/agent-activity/` (first fixture), a draft `agent-activity-diagram.dis` holding the metamodel only
  - Write the YAML binding for the file in the design (*The activity file*, *Data Models*) and run the repository's validator over it and one read fixture.
  - Settle, by the specifications' text and the validator: whether a relation can be derived from a reference attribute and drawn, with connect and disconnect mapped onto setting and clearing it (L8); how a placement and a group-state entry are identified when they carry no id key of their own; and whether a rule at `/` can bind `view.showArchived`.
  - If an answer is no: write up what the file or the language would have to change, as a selection with the cost of each option, for the user (Requirement 1.4). Do not extend a language without the ruling.
  - Guard: the validator passes on the draft binding and fixture.
  - Seen to fail against: a rule naming a `parent` rule that does not exist, which proves the validator reads this file at all.
  - _Leverage: `specifications/fbl/FBL-specification.md` sections 5 to 8, `definitions/designers/knowledge.fbl`, `definitions/diagrams/agent-behavior-modelling.dis` (its derived relation), `.github/scripts/validate-examples.py`_
  - _Requirements: 1.4_

- [ ] 2. DISL 0.4
  - File (etalii.adp): `specifications/disl/DISL-specification.md`, `specifications/disl/disl.schema.json`, one worked example per construct beside them
  - Add, each named for what it does and none for this diagram: `groupBy` on a compartment with a collapse default per value (L1); how a declared collapse default and a stored state meet (L2); `persist` on a canvas filter (L3); `pin`, `unpin` and `unpinAll` as standard context entries, `pinned` on the `view` action, and "a drag pins" under `respect: "pinned"` (L4); `tiers` on `force` with the properties a result must have (L5); the `open` action, `itemLink`, and a relative path in `uri` (L6); `persistence.view.bind` (L7); and whatever task 1 showed L8 needs. List them under *Changes from 0.3*.
  - Waits on: 1.
  - Guard: the schema validates every worked example, and the repository's example validator passes.
  - Seen to fail against: a worked example using `groupBy` on an attribute that is not an enum, which the schema or the validator must refuse.
  - _Leverage: DISL sections 6.9, 6.13.1, 7.3, 9.4, 10, 11.6 and Appendix D.2_
  - _Requirements: 1.2_

- [ ] 3. The binding and its fixtures
  - File (etalii.adp): `definitions/diagrams/agent-activity-diagram.fbl`, `specifications/fbl/fixtures/agent-activity/`
  - Complete the binding `aad`: the five element lists, tasks and pull requests as nested entries, the references, the `view` part, the header, the empty-file template, `empty: remove` on optional keys, and `remove.cascade` from an element to its references and its view entries.
  - Fixtures: one read fixture covering every key; one per kind of edit (add, set, clear and remove for an element, a row, a reference, a placement, a group state and the switch), each asserting that comments, key order and untouched lines stay byte for byte.
  - Waits on: 1.
  - Guard: the fixtures pass in the repository's validator.
  - Seen to fail against: a fixture whose expected bytes drop a comment line.
  - _Leverage: `definitions/designers/knowledge.fbl`, the hype cycle graph's binding in this host_
  - _Requirements: 1.1_

- [ ] 4. `agent-activity-diagram.dis`, `.md`, the schema and the Notion page
  - File (etalii.adp): `definitions/diagrams/agent-activity-diagram.dis`, `agent-activity-diagram.md`, `agent-activity-diagram.schema.json`; the tool's page in the Notion "Tools" database
  - Write the specification as the design outlines it (*The DISL specification*), with origin `etalii/agent-activity-diagram`, language id `net.etalii.adp.etalii.agent-activity-diagram` and extension `aad`.
  - Write the companion under its siblings' headings: what the diagram is for; the file in full with one complete example, enough to write one by hand; the reference layout algorithm with its numbers; how a host opens each kind of link; the instruction text for agents; the examples; what DISL 0.4 still cannot state; and the rule that these files win over this specification.
  - Publish the JSON Schema for the file. Bring the Notion page in line: it still marks five points Open that are now ruled, and says nothing of `updated`.
  - Waits on: 2, 3.
  - Guard: the repository's validators pass on the `.dis`; the schema validates task 3's read fixture and the companion's example.
  - Seen to fail against: the companion's example with a status that is not one of the values, which the schema must refuse.
  - _Leverage: `definitions/diagrams/gartner-hype-cycle-graph.dis` and `.md`, `definitions/diagrams/README.md`_
  - _Requirements: 1.1, 1.3, 1.5, 1.6, 1.7, 1.8, 8.2_

## What this host's core gains

- [-] 5. A write that does not overwrite a change it has not seen
  - File: `src/backend/EtAlii.Adp.Diagram` (`WritableDocumentLifecycle` and its tests)
  - Before writing, compare the file on disk with the text the edit was planned against. If they differ, read it again, plan the same change against the fresh reading and write that; if the change no longer applies, refuse it with a reason the session shows.
  - Guard: a test that changes the file on disk between a command's plan and its save, and asserts both changes are in the result; and one where the edited entry was removed outside, asserting a refusal and an untouched file.
  - Seen to fail against: the comparison removed, so the outside change is overwritten.
  - _Leverage: `SelfWriteGuard`, `GhgEdits.Run`, `OpenBody.Plan`_
  - _Requirements: 8.6_

- [-] 6. Undo never restores a file older than an outside change
  - File: `src/backend/EtAlii.Adp.History`, `src/backend/EtAlii.Adp.Diagram` (`DiagramDocumentReloadBridge`)
  - When a reload reads a change this host did not write, clear that document's undo and redo history. A reload of the host's own write clears nothing.
  - Guard: edit, change the file outside, undo: the outside change is still in the file and undo is unavailable. Edit, reload of the own write, undo: the edit is undone.
  - Seen to fail against: the clearing removed, so undo rewrites the pre-edit text over the outside change.
  - _Leverage: `RestoreDocumentCommand`, `SelfWriteGuard`_
  - _Requirements: 9.6_

- [ ] 7. The wire: locked, group states and the switch
  - File: `src/api/elements.proto`, `src/api/deltas.proto`, `src/api/diagrams.proto`, their mappers in `EtAlii.Adp.Diagram`
  - Carry on the core contract, for any module: whether an element is locked; which of an element's groups are collapsed; and the value of a canvas switch. A change to any of them reaches an open canvas as a delta on the existing `Watch` stream.
  - Guard: a `DiagramDiff` test for each: a change in the field alone produces a delta for that element and no other.
  - Seen to fail against: the field left out of the diff's comparison, so no delta is raised.
  - _Leverage: `DiagramDiff.Between`, `DiagramStreamMessage`_
  - _Requirements: 10.3_

- [ ] 8. Canvas library: lists inside an element
  - File: `src/client/src/canvas/library/definition`, `surface`, `DiagramCanvas.tsx`, a shipped library example
  - A compartment declaration on an element type: rows bound to child elements, optional grouping by a field in a declared order, a heading per group with its name and count, groups with no row left out, a collapse state per heading from the stream, a declared order of rows within a group, one line per row with an ellipsis and the full text on hover. The element's height is computed from declared line heights and its relations follow. Clicking a heading raises an event.
  - Guard: the height of an element with two expanded groups and one collapsed equals the declared sum; a collapsed group's rows are absent; rows come in the declared order.
  - Seen to fail against: the collapsed group's rows still counted in the height.
  - _Leverage: `CollectionBinding`, `elementBounds`, `labels.ts`_
  - _Requirements: 4.1, 4.2, 4.3, 7.6_

- [ ] 9. Canvas library: a row can be selected
  - File: `src/client/src/canvas/library/librarySelection.ts`, `surface`, the context connection
  - A row is hit-testable and takes part in the library's selection under its own id, so the property grid and the context menu serve it as they serve an element. A keyboard path reaches a row and a heading.
  - Waits on: 8.
  - Guard: selecting a row reports its id through the selection the library already reports; the menu key on a focused row opens the row's menu.
  - Seen to fail against: the row's hit area removed, so the click selects the element.
  - _Leverage: `librarySelection.test.tsx`, `noModuleSelection.test.ts`_
  - _Requirements: 9.4_

- [ ] 10. Canvas library and shell: a symbol that can be activated, and opening a link
  - File: `src/client/src/canvas/library/definition`, `surface`; `src/client/src/shell` (a link handler and its dialog)
  - A declaration separate from `DecorationDeclaration`: a symbol on an element or at the end of a row, drawn only when its binding has a value, with a tooltip saying where it leads, a focus stop and a press that raises a library event.
  - The shell handles the event: `http` and `https` through `window.open` with `noopener` and `noreferrer`; a path inside the project through `revealPath`; any other path in a dialog that shows it, says why it was not opened and copies it; anything else is not opened.
  - Guard: a press on the symbol raises the event with the link; no symbol is drawn for an empty value; the handler opens only the two schemes and passes `noopener`.
  - Seen to fail against: a `javascript:` value handed to the handler, which must not reach `window.open`.
  - _Leverage: `revealPath`, `AppHeader.tsx` (the one external link today), `everyCanvasHasOneRefusalSurface`_
  - _Requirements: 5.2, 5.3, 5.4, 5.5, 5.7_

- [ ] 11. Canvas library: locked elements under an automatic layout
  - File: `src/client/src/canvas/library/layout/layoutAlgorithm.ts`, `definition/diagramDefinition.ts`, `DiagramCanvas.tsx`
  - `LayoutElement` gains whether it is locked; a layout leaves a locked element at its stored position. `dragUnderAutomaticLayout` gains a value under which a drag raises `element-moved` for that element alone and the mode stays automatic. A locked element can show a declared symbol.
  - Guard: under an automatic mode, a locked element's drawn position is its stored one and an unlocked one's is the layout's; a drag raises `element-moved` once, for the dragged element.
  - Seen to fail against: the layout's position applied to every element, as today.
  - _Leverage: `DiagramCanvas.layoutModes.test.tsx`_
  - _Requirements: 6.6_

- [ ] 12. Canvas library: the radiating layout
  - File: `src/client/src/canvas/library/layout/` (a new algorithm and its tests), `definition/diagramDefinition.ts` (a new `LayoutMode` and its settings)
  - The algorithm of the design (*The layout*) and of `agent-activity-diagram.md`: springs along relations, repulsion, a pull to the circle of each element's tier, a final pass separating rectangles, locked elements fixed, starting positions from the tier and the order of entries, a fixed number of steps, nothing random. Written by hand, with no new dependency.
  - Waits on: 11.
  - Guard: on a generated diagram, mean distance from the centre rises tier by tier; no two rectangles overlap, with elements of very different heights; an element with no relation sits on its tier; two runs give equal positions.
  - Seen to fail against: the separating pass removed, so two tall elements overlap.
  - _Leverage: `rowPackedLayout.ts` as the shape of an algorithm and its tests_
  - _Requirements: 6.5, 6.7, 6.9, 6.10_

- [ ] 13. Canvas library: a change moves little, and moves visibly
  - File: `src/client/src/canvas/library/layout/`, `DiagramCanvas.tsx`, the library stylesheet
  - When the document changes while open, the layout starts from the positions on screen. Elements move from old to new positions over a short fixed time, and at once under reduced motion.
  - Waits on: 12.
  - Guard: after adding one element to a settled diagram of fifty, the elements not related to it move less than a stated bound; under reduced motion no transition is applied.
  - Seen to fail against: the warm start removed, so the same addition moves unrelated elements beyond the bound.
  - _Leverage: `connectionsFollowTheDrag.test.tsx`_
  - _Requirements: 6.8_

- [ ] 14. Canvas library: a switch on the canvas
  - File: `src/client/src/canvas/library/definition/chrome.ts`, `surface`
  - A switch declared in chrome with a caption, its value from the stream, raising an event when changed, drawn whether or not a filter is declared, with its role and state exposed.
  - Guard: a definition with a switch and no filter draws it; toggling raises the event; the drawn state follows the stream's value.
  - Seen to fail against: the switch drawn only inside the filter block, as the one toggle is today.
  - _Leverage: `LayoutToggleDeclaration`, `DiagramCanvas.filter.test.tsx`_
  - _Requirements: 4.6_

- [ ] 15. The two DISL readers learn DISL 0.4
  - File: `src/backend/EtAlii.Adp.Specification.Disl`, `src/client/src/canvas/library/disl` (`compileNotation.ts`, `disTypes.ts`)
  - Backend: read the constructs of task 2; derive menu entries for pin, unpin and unpin all and for rows; evaluate the declared gesture constraint a connect needs. Client: compile compartments with `groupBy`, activatable symbols, `itemLink`, the kept filter, `tiers` and `respect: "pinned"` into the declarations of tasks 8 to 14.
  - Waits on: 2, 8, 10, 11, 12, 14.
  - Guard: each of task 2's worked examples compiles to a definition that passes `assertValidDiagramDefinition`; the backend derives the pin entries for a specification that declares them and none for one that does not.
  - Seen to fail against: a specification with `groupBy` compiled without it, so one flat list is declared.
  - _Leverage: `ContextMenuDerivation`, `GestureConstraintEvaluator`, `ghgCompiledDefinition.test.ts`_
  - _Requirements: 10.3_

## The module

- [ ] 16. The module opens an activity file
  - File: `src/diagrams/agent-activity-diagram/` (`api/agent-activity-diagram.proto`, `backend/EtAlii.Adp.Diagram.AgentActivityDiagram` and its `.Tests`, `definition/`), `.gitattributes`, `src/backend/EtAlii.Adp.slnx`
  - Bundle the definition with `bundle-disl.sh`. Add `Diagram`, `AadBody`, `AadDefinition`, `AadParser` and `AadModel`, `AadDocumentStore`, `AadDocumentReloader`, `AadSession` and its factory, `AadElementMapper` and the registration, after the hype cycle graph's. Read every field of the design's *Data Models*, with `folder` defaulting to `Default`; a newer header opens read-only; unknown keys are kept. Add `*.aad -text`.
  - Waits on: 4, 7, 15.
  - Guard: `BundledDefinition.Tests` (the embedded `.dis` matches its provenance); a read fixture gives the expected model; an unchanged document writes back byte-identical; deleting the module folder leaves a solution that builds with no core file naming it.
  - Seen to fail against: one byte changed in the bundled `.dis`.
  - _Leverage: `src/diagrams/gartner-hype-cycle-graph`, `docs/creating-a-diagram-module.md`_
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.8, 2.9, 10.1, 10.2, 10.4, 10.7_

- [ ] 17. Adding an activity file
  - File: `backend/…/AadDocumentFactory.cs`
  - `CreateEmptyDocument`: the header and five empty lists, no `view` key, CRLF. Offered where every other tool type is.
  - Waits on: 16.
  - Guard: Add creates the registration with the origin on its first line and the body together; the new body opens with no finding.
  - Seen to fail against: the factory left unregistered, which `DiagramDocumentFactories.Verify` must refuse at startup.
  - _Leverage: `GhgDocumentFactory`, `CreateDiagramFileCommandHandler`_
  - _Requirements: 9.7_

- [ ] 18. Element commands
  - File: `backend/…/Commands/`, `AadContextActionProvider`, `AadContextPropertyProvider`, `AadToolboxProvider`
  - Add from the toolbox (five entries, none for a row), rename in place by F2 or a double-click, set a field or a link from the property grid, and remove with the relations that name the element in one undoable step, telling how many. New ids are ShortGuids; an edit splices only its own lines.
  - Waits on: 16.
  - Guard: each command against a fixture with comments, asserting the model and that every other byte is unchanged; removing an agent clears its locations' `agent` key in the same step.
  - Seen to fail against: the cascade left out, so a location keeps naming a removed agent.
  - _Leverage: `GhgEdits.Run`, `AadDefinition.Apply`, `FormDerivation`_
  - _Requirements: 2.6, 2.7, 3.9, 5.8, 9.1, 9.5_

- [ ] 19. Connecting and its refusals
  - File: `backend/…/Commands/`, `AadGestures`
  - Connect and disconnect set and clear the reference key. The relation is inferred from the pair. Any pair but the four is refused; an agent that has a specification is refused a second; a location is refused a second agent or environment; each refusal names its rule where the gesture ended. An environment may be named by any number of locations and a specification by any number of agents.
  - Waits on: 18.
  - Guard: one test per allowed pair and per refusal, asserting the file and the refusal's text.
  - Seen to fail against: the declared constraint removed from the bundled test double, so the second specification replaces the first silently.
  - _Leverage: `GestureConstraintEvaluator`, `connectOnRightDrag`, `relationOnto`_
  - _Requirements: 3.1, 3.2, 3.4, 3.5, 3.6, 9.2_

- [ ] 20. Row commands
  - File: `backend/…/Commands/`, the context providers
  - Add, rename and remove a task and a pull request, and set a task's status, from the element's menu, the row's menu and the property grid. Every add and change writes `updated` as that moment, with its offset. No command reorders rows. A status change moves the row to its group without expanding a collapsed one.
  - Waits on: 18, 9.
  - Guard: each command against a fixture; `updated` is written on add and on change and nowhere else; after a status change the group states are unchanged.
  - Seen to fail against: `updated` not written on a rename, so the row keeps its old place.
  - _Leverage: `OperationInterpreter`, the hype cycle graph's nested entries_
  - _Requirements: 4.9, 9.3_

- [ ] 21. View commands: lock, unlock, groups and the switch
  - File: `backend/…/Commands/`, `AadSession.MoveElementToAsync`
  - A drag stores a placement and locks; *Lock position* stores the layout's position, which the client supplies; *Unlock position* removes the placement; *Unlock all positions* removes them all and is offered only when there is one; an element added from the toolbox is locked where it was dropped and one written into the file is not. A heading click stores the group's state while it differs from its default and removes the entry when it returns; defaults are Progressing and Input Required expanded, Pending, Finished and pull requests collapsed. The switch is stored under `view`, off when absent; hiding archived specifications changes nothing in the model.
  - Waits on: 18.
  - Guard: each command against a fixture, asserting only the `view` part changes; collapse then expand leaves the file byte-identical to before; with the switch off the mapper leaves out archived specifications and their relations and nothing else.
  - Seen to fail against: a group state written even when it equals its default.
  - _Leverage: `GhgSession`, `AadElementMapper`_
  - _Requirements: 4.4, 4.5, 4.7, 4.8, 6.1, 6.2, 6.3, 6.4, 6.11_

- [ ] 22. Findings
  - File: `backend/…/AadValidator.cs`, `Fixtures/rule-*.aad`
  - One fixture and one test per row of the design's *Error Scenarios*, with the severities the requirements give: info for an agent with no specification or no location, a specification with no project, a project with no specification, a location with no agent or no environment, an entry without an id, and unknown keys; warning for a link that is neither a web address nor a path; error for a reference to nothing, a reference to the wrong type, an id used twice and a key written twice. Nothing is removed on the next write. A file that cannot be read keeps the last picture and accepts no edit. An entry without an id gets one on the first edit in ADP. A `view` entry for a missing element is removed on the next write and not reported.
  - Waits on: 16.
  - Guard: each fixture yields exactly its finding at its line, and a clean fixture none; each fixture written back unchanged is byte-identical.
  - Seen to fail against: `std.references` suppressed, so a dangling `agent:` reports nothing.
  - _Leverage: `GhgValidator`, `ConstraintEvaluator`, the functional decomposition graph's `rule-*.fdg` fixtures_
  - _Requirements: 3.3, 3.7, 3.8, 5.6, 8.4, 8.5, 8.7, 8.8_

- [ ] 23. The module's client
  - File: `src/diagrams/agent-activity-diagram/client/` (`register.ts`, `AadCanvas.tsx`, `aadBindings.ts`, ids, model, stylesheet)
  - `assertValidDiagramDefinition(compileNotation(SPEC, BINDINGS))` and event handlers; no drawing of its own. The notation as specified: a shape and a colour pair per type, recognisable without colour; the location as two fields split by a line with its pull requests beneath; the specification with its name, its status in words and colour, and its groups; relations as plain lines; link symbols, one per field on a location; theme tokens in both themes.
  - Waits on: 16, 15.
  - Guard: the compiled definition test; the library's guard tests for a module pass for this one; a stylesheet rule exists for every class a shipped example draws.
  - Seen to fail against: a selector on an SVG element type added to the module's stylesheet, which `noModuleReachesLibraryShapes` must refuse.
  - _Leverage: `GhgCanvas.tsx`, `ghgBindings.ts`, `docs/diagram-module-client-api.md` ("Tests a module writes")_
  - _Requirements: 4.10, 5.1, 7.1, 7.2, 7.3, 7.4, 7.5_

- [ ] 24. An outside change, end to end
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/AgentActivityDiagramFlow.Tests.cs`
  - Open a diagram over gRPC, write the file from outside as an agent would (a task's status, a new location, a removed agent), and assert the deltas on the `Watch` stream: only what changed, with a locked element's position unchanged and the view untouched.
  - Waits on: 5, 6, 21.
  - Guard: the test itself.
  - Seen to fail against: the reloader left unregistered, so no delta arrives.
  - _Leverage: `AnsibleStructureFlow.Tests.cs`, `DiagramDocumentReloadBridge`_
  - _Requirements: 8.1_

## Examples, tests, documentation and checks

- [ ] 25. Examples and the instruction text
  - File: `src/diagrams/agent-activity-diagram/examples/` (`adp-one-day`, `every-state`, `two-projects`, each with a readme; `updating-this-file.md`), seeded into `src/examples/diagrams/agent-activity-diagram`
  - Written for ADP, since no published corpus exists; each readme says so and what its set does not demonstrate. Between them they show everything Requirement 10.5 lists. `updating-this-file.md` tells an agent when to update the file and how to make each kind of change, including writing `updated` and leaving `view` alone.
  - Waits on: 22.
  - Guard: every example opens with no finding of severity error and writes back byte-identical; `ExampleRegistrationTests` opens each registration; a test walks Requirement 10.5's list and finds each item in some example.
  - Seen to fail against: the archived specification removed from `every-state`, which the walking test must report.
  - _Leverage: `src/diagrams/gartner-hype-cycle-graph/examples`, `CLAUDE.md` (*Vendored example data*)_
  - _Requirements: 8.3, 10.5, 10.6_

- [ ] 26. The layout at size
  - File: `src/client/src/canvas/library/layout/` (a measurement test), a generated diagram never committed
  - Two hundred elements and a thousand rows: assert no overlap and equal results on two runs, measure the time of a cold layout and of one change, and fix the number of steps from the measurement. Report the numbers in the implementation log.
  - Waits on: 12, 13.
  - Guard: the two assertions, and a bound on the time of one change that the measurement justifies.
  - Seen to fail against: the step count raised tenfold, which the time bound must catch.
  - _Leverage: `dragCost.test.tsx`, `scale-fixture.json` of the hype cycle graph_
  - _Requirements: 11.2_

- [ ] 27. Documentation
  - File: `docs/tools.md`, `docs/diagram-module-client-api.md`, `docs/creating-a-diagram-module.md`, `docs/architecture.md`, `docs/solution-structure.md`
  - Add the row for `etalii/agent-activity-diagram`, kind Diagram, and move it with the state, in `docs/tools.md` and in the Notion "Tools" database. Describe the library capabilities of tasks 8 to 14, what a module with rows and view state in its body declares, and the lifecycle's behaviour on a write and on a reload.
  - Waits on: 23.
  - Guard: `ArchitecturePages.Tests` and the terminology check pass.
  - Seen to fail against: the project counts on the structure page left as they were, which `ArchitecturePages.Tests` must report.
  - _Leverage: the rows for `etalii/functional-decomposition-graph` and `gartner/hypecycle-graph`_
  - _Requirements: 10.8_

- [ ] 28. The browser pass
  - File: `tests.md`
  - Write and run the entry Requirement 11.1 lists, in a real browser and both themes, against a build from a fresh worktree. Nothing here is taken from jsdom.
  - Waits on: 24, 25.
  - Guard: the entry, run, with each step's result recorded.
  - Seen to fail against: not a guard that code can fail; the entry is accepted on being run, and any defect it finds leaves its own guard behind.
  - _Leverage: `tests.md`, `CLAUDE.md` (*Bugs found during implementation or verification*)_
  - _Requirements: 11.1, 11.3_

- [ ] 29. Coverage, before and after
  - File: this document, the requirements
  - Before the card: extract every requirement reference from this document, list every acceptance criterion in the requirements, and diff the two sets. After implementing: trace each criterion to files and strings, and read two traces back to their artefacts to ask whether the evidence is that criterion's. Check that no task lacks a *Seen to fail against* line.
  - Guard: the diff is empty in both directions.
  - Seen to fail against: one `_Requirements` entry deleted, which the diff must report.
  - _Leverage: `CLAUDE.md` (*Checking that a specification's tasks cover its requirements*)_
  - _Requirements: 11.4, 11.5_
