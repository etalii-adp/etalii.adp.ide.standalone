# Tasks Document

> **The corpus comes first, or Requirement 2 is untested.** The byte-identical round trip is this module's headline correctness property, and it is only ever as good as the documents it round-trips. Task 1 exists so that every later claim has something real to fail against. `c4-diagrams` learned this the expensive way: its corpus found six defects on the day it landed.
>
> **One conversion, in one place.** `TimelineScale` and `TimelineRows` are the only code permitted to convert between a time and an x, or a row and a y. If a task tempts you to do the arithmetic in a parser, a command, a session or a canvas, the task is being read wrongly — the design names this as the module's most error-prone surface for the same reason `wardley-map` names its axis flip.
>
> **The document is the only writer.** Nothing serialises a model back to a file. Every write is a splice through `TimelineDocument`, dispatched by a command handler. A task that tempts you to regenerate YAML is a task you have misread.
>
> **No core change.** The Introduction claims this type needs none, and the design tested that claim against every seam. If implementation finds one genuinely required, that is a **finding to report** — write it down and raise it — not a change to make quietly.
>
> **Worktree, with a short name.** Per CLAUDE.md, implement in one dedicated worktree for this spec, and keep the directory name short: `.claude/worktrees/timeline/`. A long worktree name pushes the deepest project paths past Windows' 260-character limit, and the symptom is `Zero tests ran` rather than a build failure. For the manual pass use a free port pair and revert `vite.config.ts` and `appsettings.developer.json` before merging.
>
> **Regenerate the client stubs.** Task 8 adds `timeline.proto`. `src/client/src/generated/*_pb.ts` is gitignored for new protos, so a stale copy fails silently and `vitest` will not catch it — only `tsc` will. Run `npm run generate` in `src/client/`, and run `npm run typecheck` alongside `npm test`.
>
> **Steering rules in force.** No nested types — lift anything you are tempted to nest into `_Model/`. Every test carries `// Arrange.` / `// Act.` / `// Assert.`. The module registers through one `AddTimeline` extension method rather than lines in `Program.cs`. Loggers are `private static readonly ILogger _logger = Log.ForContext<T>();`. **Commits are authored under your own agent session name**, per tech.md's *Naming & Identity*, set per invocation and never through `git config`.
>
> **Bugs.** Per CLAUDE.md, any bug found while implementing or verifying a task leaves a guard behind: a unit or integration test that fails before the fix, or — if it is only reproducible through the running app — a step-by-step entry in `tests.md` naming this spec and task.

## Phase A — the corpus

- [x] 1. Assemble the timeline round-trip corpus
  - File: `src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline.Tests/Fixtures/*.tml` (new), `Fixtures/readme.md` (new)
  - Commit documents covering: periods and moments together; several elements sharing a row; rows out of order and non-contiguous; date-only and date-time values in the same document but never in the same element; connections including two between the same pair; comments in every position; blank lines; both CRLF and LF; a file with no trailing newline; unusual but valid indentation; quoted and unquoted scalars; and **keys this module does not model, at every level**
  - The readme records what each file exists to prove. A file written to match the parser is worthless as a fixture — write the document first, make the parser meet it
  - Purpose: every guarantee in Requirement 2 rests on these files, and the no-trailing-newline case is the one that has bitten a sibling module before
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/Fixtures/readme.md (the provenance convention this follows)_
  - _Requirements: 2.1, 2.2, 2.3, 3.2, 3.5_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer with a careful eye for text-format edge cases | Task: Assemble a round-trip corpus of .tml documents covering line endings, comments, blank lines, unmodelled keys, shared rows, moments and a missing trailing newline, and document what each file guards | Restrictions: do not write a fixture to match an implementation that does not exist yet; do not normalise indentation, quoting or comments; keep CRLF and LF samples byte-exact | Success: the corpus covers every case the readme claims, and each file's purpose is stated_

## Phase B — the document, read and written

- [x] 2. `TimelineDocument`: the file as read
  - File: `src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/_Model/TimelineDocument.cs` (new), tests
  - Lines, the dominant line ending, and splices by range: `Parse`, `Insert`, `Remove`, `Replace`, `ToString`. **Two cases the design calls out because a sibling module got them wrong first:** appending to a file whose last line has no terminator must not gain one, and removing the last line must move its ending to the new last line
  - Purpose: makes Requirement 2.1 structural rather than a thing to be careful about
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineDocument.cs_
  - _Requirements: 2.1, 2.2_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement a line-based concrete syntax tree with splice operations that preserves line endings exactly | Restrictions: never normalise a line ending; never add or remove a trailing newline as a side effect of an edit | Success: every corpus file round-trips byte-identically, and the append-at-EOF and remove-the-last-line cases each have a test that fails without their guard_

- [x] 3. `TimelineParser`: YAML in, model out, with line ranges
  - File: `.../TimelineParser.cs` (new), `_Model/TimelineModel.cs`, `TimelineElement.cs`, `TimelineConnection.cs`, `TimelineInstant.cs`, `TimelinePrecision.cs`, `LineRange.cs` (new), tests
  - Read with YamlDotNet — **reading only**. Record a line range for every element and connection, taken from node marks, so a diagnostic and a writer both know which lines belong to what. Parse `begin`/`end` into a `TimelineInstant` carrying its precision, so a date-only value is distinguishable from a midnight date-time (Requirement 3.2)
  - Purpose: the model everything downstream is a pure function over
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineParser.cs_
  - _Requirements: 3.1, 3.2, 3.3, 3.5, 3.7, 3.8_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Parse a .tml document into a model, recording each element's and connection's line range and each instant's precision | Restrictions: YamlDotNet is for reading only; never serialise through it; take times at face value with no timezone conversion or calendar arithmetic | Success: every corpus document parses, ranges are correct against the source lines, and a date-only value is not silently promoted to a date-time_

- [x] 4. `TimelineWriter`: an edit is a splice
  - File: `.../TimelineWriter.cs` (new), tests
  - `SetLabel`, `SetBegin`, `SetEnd`, `SetRow`, `InsertElement`, `RemoveElement`, `InsertConnection`, `RemoveConnection`, `SetConnectionLabel`. Match on the element's recorded range and its exact indentation; removing an element removes its connections in the same splice, so the count Requirement 2.5 wants to report is known before the write
  - Purpose: Requirements 2.2 and 2.3, by construction
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineWriter.cs_
  - _Requirements: 2.2, 2.3, 2.5_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement splice-based edits that touch only the lines an edit affects | Restrictions: never rewrite a key the edit did not change; never reorder or re-indent; never drop an unmodelled key | Success: each edit changes exactly the expected lines against a corpus fixture, and a document with unmodelled keys survives every edit unchanged apart from the target_

## Phase C — the one dangerous conversion

- [x] 5. `TimelineScale`: time to x, and back
  - File: `.../TimelineScale.cs` (new, static), tests
  - `ToSeconds(DateTimeOffset)` and `ToTime(double, TimelinePrecision)`, seconds since the Unix epoch as a double. A date-only value converts to midnight and converts back **date-only** — the precision travels with the value, not with the caller
  - Purpose: the design's central decision, isolated so it can be tested without a canvas
  - _Leverage: src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap/WardleyElementMapper.cs (the same discipline, applied to an axis flip)_
  - _Requirements: 3.2, 4.1, 4.2, 6.7_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the sole conversion between a time and a horizontal coordinate, carrying precision through the round trip | Restrictions: no other type may perform this conversion; no timezone conversion; no calendar arithmetic | Success: a round-trip property test over a wide range of instants holds, and a date-only value never comes back as a date-time_

- [x] 6. `TimelineRows`: row to y, and the nearest row back
  - File: `.../TimelineRows.cs` (new, static), tests
  - `ToY(int)`, `ToNearestRow(double)`, `Height`. **Test at the midpoint between two rows**, in both directions, because an off-by-one there is the mistake this function exists to make impossible
  - Purpose: Requirement 6.2's snapping, as a pure function
  - _Requirements: 3.5, 4.3, 6.2, 9.3_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the sole conversion between a row index and a vertical coordinate, with a nearest-row inverse | Restrictions: no other type may perform this conversion; a negative row is as valid as a positive one | Success: round-trip tests hold, and the midpoint between two rows resolves deterministically and is covered by a test that would fail on an off-by-one_

## Phase D — the type, its payloads and its mapping

- [x] 7. Declare the diagram type
  - File: `.../Diagram.cs` (new), `EtAlii.Adp.Diagram.Timeline.csproj` (new), tests
  - One `DiagramDefinition` with origin `generic/timeline`, extension `.tml`, **not** shared — so `DiagramFileRouter` routes a bare `.tml` on sight, with no registration step
  - Purpose: Requirement 1.2, using the routing that already exists
  - _Leverage: src/backend/EtAlii.Adp.Diagram/_Model/DiagramDefinition.cs, src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFileRouter.cs_
  - _Requirements: 1.1, 1.2_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Declare the timeline diagram type with its own distinctive extension | Restrictions: do not set SharedExtension; do not add an Add-on-a-file path; core must not learn the extension | Success: a bare .tml routes to this type, discovery finds the definition, and a test pins the extension and the origin_

- [x] 8. `timeline.proto` and the generated stubs
  - File: `src/diagrams/timeline/api/timeline.proto` (new), `src/diagrams/timeline/api/readme.md` (new), `src/client/package.json` (edited); run `npm run generate`
  - `TimelineElementPayload` (begin, end, row, date_only) and `TimelineConnectionPayload` (from, to, label). Add the new api folder to the `generate` script
  - Purpose: what travels in `Element.payload`
  - _Leverage: src/diagrams/wardley-map/api/wardley-map.proto_
  - _Requirements: 3.1, 3.3, 3.8_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer familiar with protobuf | Task: Define the payload messages and wire the new api folder into the client generate script | Restrictions: do not change any core proto; carry the times as written rather than as parsed values | Success: npm run generate produces the stubs, npm run typecheck passes, and the client compiles against them_

- [ ] 9. `TimelineElementMapper`
  - File: `.../TimelineElementMapper.cs` (new), tests
  - Model to `Element`s and `Delta`s; a received `Point2D` back to a begin, an end and a row. **Does no arithmetic of its own** — it calls `TimelineScale` and `TimelineRows`
  - Purpose: the boundary between the module's vocabulary and core's
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineElementMapper.cs_
  - _Requirements: 4.1, 4.2, 4.3, 4.6, 6.7_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Map the timeline model to core elements and deltas, and map a received position back to a placement | Restrictions: perform no conversion arithmetic here - delegate to TimelineScale and TimelineRows | Success: a model maps to elements whose ids are stable across reloads, and a position round-trips back to the placement it came from_

- [ ] 10. `TimelineDocumentFactory`: a new document that is valid
  - File: `.../TimelineDocumentFactory.cs` (new), tests
  - The schema marker and an empty element list, and nothing else
  - Purpose: Requirement 1.3
  - _Requirements: 1.3_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Produce a minimal valid empty timeline document for the Add flow | Restrictions: add no sample content; the result must parse and validate cleanly | Success: a document created through Add opens as an empty diagram with no problems reported_

## Phase E — the open diagram

- [ ] 11. `TimelineDocumentStore`
  - File: `.../TimelineDocumentStore.cs`, `ITimelineDocumentStore.cs` (new), tests
  - One parsed document per path; `Forget` on file change so a stale parse is never served
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineDocumentStore.cs_
  - _Requirements: 1.5, 2.4_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Cache one parsed document per path and invalidate it when the file changes | Restrictions: never serve a parse older than the file; never write from the store | Success: a change on disk is reflected on the next read, and a test proves the cache is dropped rather than merely refreshed_

- [ ] 12. `TimelineSession` and `TimelineSessionFactory`
  - File: `.../TimelineSession.cs`, `TimelineSessionFactory.cs` (new), tests
  - Baseline on open, deltas on change. **`MoveElementToAsync` is implemented; `MoveElementAsync` refuses** — a timeline element has no parent to be moved under, and saying so is better than appearing to support a gesture that means nothing here
  - Purpose: Requirements 6.7 and 1.5
  - _Leverage: src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap/WardleySession.cs_
  - _Requirements: 1.5, 4.7, 6.7_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Open a timeline as a session that streams a baseline and deltas, implementing the positional move and refusing the re-parenting move with a reason | Restrictions: the session performs no conversion arithmetic; a missing body opens the unavailable state rather than throwing | Success: opening the same document twice produces identical output, and each move leg behaves as specified_

## Phase F — commands

- [ ] 13. Add and remove an element
  - File: `.../Commands/AddTimelineElementCommand.cs`, `RemoveTimelineElementCommand.cs` (+ handlers) (new), tests
  - Add takes a begin, an optional end and a row. Remove takes the element and its connections together, in one command with one inverse, and reports how many connections it will take before it runs
  - _Requirements: 2.5, 11.1_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement add and remove as commands with inverses, where remove carries the element's connections | Restrictions: handlers must be re-runnable because undo and redo re-dispatch them; never write outside the document | Success: add-then-undo and remove-then-undo each leave the file byte-identical, and the connection count is reported before the removal_

- [ ] 14. Rename an element
  - File: `.../Commands/RenameTimelineElementCommand.cs` (+ handler) (new), tests
  - Rewrites the label line and nothing else. **The id does not change**, so the selection, the history and every pushed element id survive a rename — the benefit the design attributes to owning the schema
  - _Requirements: 11.1_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement rename as a single-line edit that leaves identity untouched | Restrictions: never rewrite the id; never touch a connection because a label changed | Success: a rename changes one line, the selection survives it, and one undo restores the file exactly_

- [ ] 15. `SetTimelinePlacementCommand`: the one command behind drag, resize and grid
  - File: `.../Commands/SetTimelinePlacementCommand.cs` (+ handler) (new), tests
  - Carries begin, end and row; its inverse carries the previous three. **Clamps so end cannot precede begin** — the second of the two guards behind Requirement 3.4, and the one that holds regardless of which client sent the request. Carries a short description so the history can say what the gesture was
  - Purpose: Requirements 6.4, 7.6 and 10.3 with one handler rather than five
  - _Requirements: 3.4, 6.1, 6.2, 6.4, 7.2, 7.3, 7.4, 7.6, 10.3, 11.1_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement one placement command serving drag, resize and grid edits, clamping so end never precedes begin | Restrictions: the clamp must live in the handler and not only in the client; one command per gesture, never one per pointer move | Success: a drag preserves duration, a resize moves one edge only, an inverting value is refused with a reason, and each produces exactly one history entry_

- [ ] 16. Connect, disconnect and relabel a connection
  - File: `.../Commands/ConnectTimelineElementsCommand.cs`, `DisconnectTimelineElementsCommand.cs`, `RelabelTimelineConnectionCommand.cs` (+ handlers) (new), tests
  - A self-connection is refused with a reason; a second connection between an already-connected pair is **permitted**, because the notation carries a label per connection
  - _Requirements: 8.4, 8.7, 8.8, 11.1, 11.4_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the three connection commands with inverses | Restrictions: refuse a self-connection with a reason; do not refuse a duplicate pair; never write a connection to an element that does not exist | Success: each command undoes to a byte-identical file, and the self-connection refusal and the permitted duplicate each have a test_

## Phase G — the context seams

- [ ] 17. `TimelineContextSourceResolver`
  - File: `.../TimelineContextSourceResolver.cs` (new), tests
  - Resolves an element or connection id within a `.tml`. Note that every module's resolver claims every element id shape, so this must **accept or decline based on the enclosing document**, not on the shape alone
  - _Leverage: src/backend/EtAlii.Adp.Backend/Context/ContextSelectionResolver.cs_
  - _Requirements: 11.2, 12.4_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Resolve selection for timeline elements and connections | Restrictions: decline an id belonging to another diagram type's document rather than claiming it | Success: selection works alongside the other registered types, proven by a test with more than one type registered_

- [ ] 18. `TimelineContextActionProvider`
  - File: `.../TimelineContextActionProvider.cs` (new), tests
  - Element: *Remove*, *Connect*, and *Give it an end* for a moment / *Remove its end* for a period. Connection: *Disconnect*, *Relabel*. Background: *Add element here*, taking its begin and row from the click. Read-only offers nothing mutating — absent or explained, never greyed out and inert
  - _Requirements: 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 7.5_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Offer the timeline context actions per selection, each dispatching a command | Restrictions: never offer a mutating action in read-only mode; an inapplicable action reports unavailable with a reason | Success: each selection kind offers exactly the specified actions, and a test walks the real discover-then-commit path rather than asserting against a cached model_

- [ ] 19. `TimelineContextPropertyProvider`
  - File: `.../TimelineContextPropertyProvider.cs` (new), tests
  - Element: Label, Begin, End, Row. Connection: Label editable; source and target read-only **with a reason** saying reconnecting is done on the canvas. Begin and End use the `Line` editor with commit-time validation, since no date editor exists yet — **and this module does not add a private one**
  - Purpose: Requirement 10, including 10.3's refusal, which is the second path Requirement 3.4 closes
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineContextPropertyProvider.cs_
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Contribute the timeline element and connection properties, rejecting invalid values with reasons | Restrictions: do not add a date editor to the shared seam; every read-only row carries a sentence saying why; every edit travels as a command | Success: a value that would invert an element is rejected with a reason, and a test proves the grid closes that path independently of the canvas_

- [ ] 20. `TimelineToolboxProvider`, and a drop that keeps its position
  - File: `.../TimelineToolboxProvider.cs` (new), tests
  - *Period* and *Moment*, each naming the add action it drops into. The drop encodes its placement in the action's `value`, so the element is created where it was dropped rather than at the viewport centre. **Verify the client's drop handler populates `value`**; if it sends an empty string, fix it in this module's canvas and report it — `wardley-map`'s viewport-centre compromise may be liftable
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineToolboxProvider.cs_
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Describe the toolbox as data and make a drop create its element at the dropped time and row | Restrictions: the entry carries data only; the drop must reuse the add action rather than a second implementation | Success: every entry names an action that exists, and an integration test proves a drop lands at the dropped position rather than the viewport centre_

## Phase H — validation

- [ ] 21. `TimelineRuleSet`
  - File: `.../TimelineRuleSet.cs`, `TimelineRules.cs` (new), tests
  - End before begin; a begin or end that will not parse; a connection naming an absent element; a mixed date/date-time pair on one element; a duplicate id. Each a warning naming the element, with the rest of the diagram still drawing
  - Purpose: Requirement 12.2, as a pure function over a model
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/PipelineRuleSet.cs_
  - _Requirements: 3.2, 3.4, 12.1, 12.2_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the timeline rules as a pure function over the model | Restrictions: a rule must run from a plain string with no file, canvas or connection; a rule that fires on a healthy document is worse than no rule | Success: one test per rule plus one proving a correct document reports nothing_

- [ ] 22. `TimelineValidator`
  - File: `.../TimelineValidator.cs` (new), tests
  - The join: parse, then judge. An unparseable document is **one** problem naming its line, not a pile of consequences from an empty model — and the message is the same one the unavailable state shows, so the panel and the canvas never disagree
  - _Requirements: 2.4, 12.1, 12.3_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the validator seam, reporting an unparseable document as a single located problem | Restrictions: never run graph rules over a model that is empty only because the parse failed; never rewrite an unparseable file | Success: a healthy document reports nothing, a flawed one reports its rule, and an unparseable one reports exactly one problem with a line number_

## Phase I — the client

- [ ] 23. Model decode, registration and the stream
  - File: `src/diagrams/timeline/client/timelineModel.ts`, `register.ts`, `useTimelineStream.ts`, `timeline.css` (new), tests
  - **Check the element type before decoding the payload.** A foreign element on the shared stream must be skipped, not throw and take the whole delta with it — a sibling module shipped that bug once
  - _Leverage: src/diagrams/wardley-map/client/wardleyModel.ts_
  - _Requirements: 4.7_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Decode timeline payloads and register the client-side diagram type | Restrictions: check the element type before decoding; use the generated TypeScript enum spelling, not the C# one | Success: npm run typecheck and npm test both pass, and a foreign element on the stream is skipped rather than throwing_

- [ ] 24. `TimelineCanvas`: laying the diagram out
  - File: `src/diagrams/timeline/client/TimelineCanvas.tsx` (new), tests
  - Periods as boxes from begin to end, moments as markers, each on its row. Overlaps are drawn as authored. Zoom and pan are a view transform held here and **never sent to the backend**
  - _Requirements: 4.1, 4.3, 4.5, 4.6, 4.7_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Render timeline elements along a time axis on their authored rows | Restrictions: never move an element to avoid an overlap; never send viewport state to the backend | Success: the same document renders identically twice, and an overlapping pair is drawn overlapping_

- [ ] 25. `TimelineRuler`: chrome fixed to the view
  - File: `src/diagrams/timeline/client/TimelineRuler.tsx` (new), tests
  - Fixed to the bottom of the view, labels scrolling with the content, on round boundaries of an interval chosen for the visible span. **The ladder is a pure function** — span and width in, unit and step out — so the requirement the spec itself flags as most likely to be got wrong is testable without a browser
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Draw a view-fixed time ruler whose label interval suits the visible range | Restrictions: the ruler lives in this module, never in the shell; labels sit on round boundaries, not viewport offsets; it must be visually subordinate to the diagram | Success: the interval function is tested across the whole zoom range rather than at one sample, and labels enter and leave as the view moves_

- [ ] 26. Dragging: preview, snap, live connections, one command
  - File: `TimelineCanvas.tsx` (continued), tests
  - A preview placement follows the pointer; the row snaps to the nearest as the pointer crosses a midpoint; **the element's connections are redrawn as it moves**; the times and row it would land on are visible before release; `Escape` or a release outside abandons it with nothing dispatched; release sends one `MoveElement` carrying the placement
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 8.6_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Implement dragging with a live preview, row snapping and connections that follow during the gesture | Restrictions: one command on release, never one per pointer move; a cancelled drag dispatches nothing; do no conversion arithmetic outside the shared functions | Success: a horizontal drag preserves duration, a vertical drag lands on a row, connections move during the drag, and Escape leaves the document untouched_

- [ ] 27. Adorners: resizing an edge
  - File: `src/diagrams/timeline/client/TimelineAdorners.tsx` (new), tests
  - Left and right adorners on a selected element; left edits begin, right edits end; the edge **stops at the other rather than crossing it**; a moment offers no right adorner; the landing time is visible before release; read-only offers none
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.7, 7.8_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Implement resize adorners that edit begin and end and refuse to invert the element | Restrictions: a moment has no right adorner; no adorners in read-only mode; the clamp here is in addition to the handler's, never instead of it | Success: each adorner edits one value, the boundary refusal is tested, and a moment offers only a left adorner_

- [ ] 28. Connections: drawing them, and drawing them well
  - File: `TimelineCanvas.tsx` (continued), tests
  - Anchors at the mid-points of left and right sides, curves as horizontal cubic beziers — **by importing `sideAnchorOf` and `horizontalBezierPath`, not by reimplementing either**. Dragging from one side anchor to another creates a connection; a gesture ending anywhere invalid cancels with a reason
  - _Leverage: src/client/src/canvas/connectors.ts_
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Draw and create connections using the shared connector geometry | Restrictions: contribute no new connector geometry; never anchor at a top, bottom or corner; a cancelled gesture changes nothing | Success: connections render as horizontal beziers between side mid-points, and an invalid drop cancels with a reason_

## Phase J — wiring, proof and the closing check

- [ ] 29. `ServiceCollection.AddTimeline` and one line in `Program.cs`
  - File: `.../ServiceCollection.AddTimeline.cs` (new), `src/backend/EtAlii.Adp.Backend.Service/Program.cs` (edited), tests
  - Every seam registered in one extension method; **exactly one line** added to the host. Keep the `using` block in alphabetical order — `dotnet format style --verify-no-changes --severity info` reports it otherwise
  - _Leverage: src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline/ServiceCollection.AddAzurePipeline.cs_
  - _Requirements: 1.1_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Gather every module registration into one AddTimeline extension method and call it once from the host | Restrictions: no per-seam lines in Program.cs; core must name nothing from this module | Success: the host starts, the type is discoverable and openable, Program.cs gained exactly one line, and a test asserts every seam is registered_

- [ ] 30. Integration tests across the whole arc
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/TimelineFlow.Tests.cs` (new)
  - A bare `.tml` routing on sight; open streaming elements and connections; a drag rewriting only the lines it touched and being one undo away; a resize refused at the boundary; a grid edit refused on the same terms; deleting an element taking its connections in one undo; the toolbox palette answering for this type
  - _Requirements: 1.2, 2.2, 2.5, 3.4, 6.4, 7.4, 9.1, 10.3_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Prove the whole arc end to end through the real gRPC surface | Restrictions: use the real service APIs rather than guessed names; one watch id per connection | Success: every listed flow has a test, and each fails if its guard is reverted_

- [ ] 31. Catalog, manual pass, and the closing check
  - File: `docs/diagrams.md` (edited), `tests.md` (edited), this document
  - Move the `generic/timeline` row to ✅ Implemented. Run the app and verify by hand what a unit test cannot express — the ruler staying put while the diagram scrolls, labels changing with zoom, connections following a drag — recording each as a step-by-step entry in `tests.md`. Then check the claim the Introduction makes: **no Azure-Pipelines-style core change crept in**; grep core for anything naming time, rows or this type, and report what you find rather than fixing it quietly. Record the `TextDocument` finding's status: still open, or superseded
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 6.5, 8.6_
  - _Prompt: Implement the task for spec timeline-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer performing a verification pass | Task: Update the catalog, run the manual checks the automated tests cannot express, and verify no core change crept in | Restrictions: record a manual check as reproducible steps, never as a claim that it worked; report a core change as a finding rather than removing it silently | Success: the catalog row is current, tests.md carries each manual check with preconditions and expected results, and the no-core-change claim is either confirmed or reported as broken with specifics_
