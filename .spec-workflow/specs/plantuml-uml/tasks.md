# Tasks Document

> **The corpus comes first, and it is real files.** Requirement 2.1's byte-identical round trip is this module's headline correctness property, and it is only as good as the corpus it round-trips. Task 2 builds that corpus from real-world `.puml` documents, not hand-written approximations. The C4 spec learned this the expensive way: its corpus found six defects on the day it landed, and two of them were in the fixtures rather than the parser.
>
> **`.puml` fixtures are byte-compared, so they need a `-text` attribute.** Task 3 is that step, and it is a real step rather than a footnote. Without it git rewrites line endings on checkout and the round-trip tests measure git instead of the writer — a failure that reads exactly like a parser bug and has cost this repository time before. The reasoning is already written in `.gitattributes`; add `.puml` to the by-extension list beside `.mm` and `.dsl`.
>
> **The document is the only writer.** Nothing below writes a `.puml` except a command handler acting on `PlantumlDocument`. If a task seems to need the model serialised back to a file, the task is wrong, not the design — regenerating text is how a writer loses a human's arrow spellings, indentation and blank lines.
>
> **There is no position anywhere, and no sidecar.** PlantUML cannot express a coordinate, so nothing computes one into the document and nothing writes an ADP file beside it. If a task seems to need a position stored, re-read Requirement 6.6 — the absence is the design, and it is deliberate because a `.puml` is rendered by other tools from the same bytes.
>
> **`dry_run` on `DrawConnector` is one message on purpose.** The canvas asks the same question while the pointer moves that it asks on release, so the live feedback and the final refusal cannot disagree. Splitting it into a validate RPC and a draw RPC would be a simplification that reintroduces exactly the drift it was designed to prevent. If that split looks tempting during task 14, it is the thing the design ruled out.
>
> **Refusal is a feature with tasks of its own.** Repositioning drags are refused *with an explanation* (task 20), and the two drags that mean something in the notation are ordinary commands (task 19). A happy-path-only implementation produces a silent no-op, which reads to a user as a bug in the canvas rather than as a property of the notation.
>
> **Determinism is not something a passing test run demonstrates.** Task 12 pins it explicitly, because ranking that accidentally depends on set enumeration order passes every ordinary test and reorders the diagram on a different runtime.
>
> **Worktree.** Per CLAUDE.md, implement in one dedicated worktree with a SHORT name — `.claude/worktrees/puml/`. A long name pushes the deepest project paths past `MAX_PATH`, and the symptom is `dotnet test` reporting `Zero tests ran` rather than failing the build. For a manual pass use a free port pair and revert `vite.config.ts` and `appsettings.developer.json` before merging.
>
> **Recent steering rules apply.** An agent commits under its own name, never the repository owner's; a commit pathspec is no broader than the change itself; `_Model/` for POCOs, one entity per file; Serilog through a `private static readonly ILogger` per class; every test carries `// Arrange.` / `// Act.` / `// Assert.`.
>
> **Bugs.** Per CLAUDE.md, any bug found while implementing or verifying a task leaves a guard behind — a test where one can express it, an entry in `tests.md` where only a running app can.

## Phase A — the document, the corpus, and the guard around them

- [ ] 1. `PlantumlDocument`: the file as the lines it was read as
  - File: `src/diagrams/plantuml-uml/backend/EtAlii.Adp.Diagram.PlantumlUml/_Model/PlantumlDocument.cs` (new)
  - Lines each carrying their own terminator, plus `Parse`, `ToText`, `ReplaceLine`, `InsertLine`, `RemoveLines`, `Newline`, `EndsWithNewline`
  - Purpose: Requirement 2.1's byte-identical guarantee, held by one type
  - _Leverage: `C4Document` — the same shape, already proven against mixed terminators_
  - _Requirements: 2.1, 2.2, 2.3_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Port the per-line terminator model to PlantUML per Requirement 2.1 | Restrictions: a line this type did not edit must come back byte-identical, including in a document mixing CRLF and LF and one ending without a newline; a new line takes the document's prevailing terminator, not the platform's | Success: round trips are byte-identical for every terminator combination. Mark in progress, log when done, then mark complete._

- [ ] 2. The corpus: real `.puml` documents, both notations
  - File: `src/diagrams/plantuml-uml/backend/EtAlii.Adp.Diagram.PlantumlUml.Tests/Fixtures/` (new)
  - Real-world class and sequence documents, hand-written indentation styles, CRLF and LF, documents mixing modelled and unmodelled constructs, a multi-block file, and a file whose only block is an unsupported notation
  - Purpose: the Reliability requirement — the round-trip guarantee is worth exactly what the corpus is worth
  - _Leverage: the c4 module's `Fixtures/readme.md`, which records what a fixture is and is not_
  - _Requirements: 2.1, 3.3, 12.1_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer assembling test data | Task: Build the round-trip corpus from real documents | Restrictions: do not hand-write approximations of PlantUML and call them a corpus - take real files and say in a readme where each came from; include the two worked examples from Requirements 4.8 and 5.8 verbatim | Success: every fixture round-trips byte-identically before any parsing work exists. Mark in progress, log when done, then mark complete._

- [ ] 3. Give `.puml` the `-text` attribute
  - File: `.gitattributes` (modify)
  - Add `.puml` to the by-extension list beside `.mm` and `.dsl`
  - Purpose: the corpus is byte-compared, so git must not rewrite it
  - _Leverage: the reasoning already written in `.gitattributes` for the existing entries_
  - _Requirements: 2.1_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Add the `.puml` extension to the byte-exact list | Restrictions: by extension, not by directory - a directory rule also catches readmes and generated output beside the fixtures, which is recorded in that file as tried and withdrawn; verify with `git check-attr` and confirm `git add --renormalize .` rewrites nothing | Success: a CRLF fixture and an LF fixture both survive a checkout unchanged. Mark in progress, log when done, then mark complete._

- [ ] 4. `PlantumlBlockScanner` and `PlantumlNotation`
  - File: `.../PlantumlBlockScanner.cs`, `.../PlantumlNotation.cs` (new)
  - Find the block pairs and their optional names; infer the notation from content
  - Purpose: Requirements 12.1 and 12.2
  - _Requirements: 12.1, 12.2, 12.3_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Address blocks and infer notation | Restrictions: inference reads content as PlantUML does, never the file extension; an unsupported notation is reported by name and leaves the file openable and unmodified per Requirement 12.3 | Success: a multi-block file addresses each block, and a use-case block opens unavailable naming the notation. Mark in progress, log when done, then mark complete._

## Phase B — the class notation

- [ ] 5. `PlantumlClassParser`: types, members, stereotypes
  - File: `.../PlantumlClassParser.cs`, `.../_Model/` records (new)
  - Every kind in Requirement 4.1, members with visibility per 4.2, stereotypes per 4.7
  - _Requirements: 4.1, 4.2, 4.7_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Parse class-diagram declarations into the model | Restrictions: every element records the line range it came from - that back-reference is what makes a minimal edit possible later, and adding it afterwards means re-walking the document | Success: Requirement 4.8's worked example parses with every element present. Mark in progress, log when done, then mark complete._

- [ ] 6. Relationships, containment and notes
  - File: `.../PlantumlClassParser.cs` (modify)
  - The six kinds in 4.3 with labels, multiplicities and direction markers; `package`/`namespace` containment per 4.5; notes per 4.6
  - _Requirements: 4.3, 4.4, 4.5, 4.6, 4.9_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Parse relationships, containment and notes | Restrictions: a label's direction marker is modelled, since it is what tells a reader which way to read the label; a class referenced by a relationship but never declared is implicitly declared, exactly as PlantUML treats it, and is not an error (Requirement 4.9) | Success: Requirement 4.8's example draws `OrderLine` without reporting it. Mark in progress, log when done, then mark complete._

## Phase C — the sequence notation

- [ ] 7. `PlantumlSequenceParser`: participants and messages
  - File: `.../PlantumlSequenceParser.cs` (new)
  - The kinds in 5.1 with aliases and display names; messages per 5.2; participant ordering per 5.9
  - _Requirements: 5.1, 5.2, 5.9_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Parse participants and messages | Restrictions: participant order is declaration order then order of first appearance - it is the diagram's horizontal layout, and getting it wrong redraws the whole picture | Success: Requirement 5.8's worked example orders its four participants correctly. Mark in progress, log when done, then mark complete._

- [ ] 8. Activations, frames, notes and ordered items
  - File: `.../PlantumlSequenceParser.cs` (modify)
  - Activation spans per 5.3, the frames in 5.4 with nesting, notes per 5.6, dividers/delays/spaces per 5.7
  - _Requirements: 5.3, 5.4, 5.6, 5.7_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Parse the lifeline's ordered items | Restrictions: a divider, a delay and a space each occupy a position in the sequence - removing one changes the diagram, so each is a modelled ordered item rather than a preserved line | Success: the nested alt/else in Requirement 5.8 parses with its branches. Mark in progress, log when done, then mark complete._

- [ ] 9. `autonumber`, maintained by the module
  - File: `.../PlantumlSequenceParser.cs`, `.../Commands/` (modify)
  - Numbering as PlantUML numbers it, maintained as messages are inserted, reordered or removed
  - _Requirements: 5.5_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Maintain autonumbering across edits | Restrictions: never hand-maintained by the user - an insert in the middle renumbers what follows | Success: inserting a message into a numbered sequence renumbers correctly and undo restores the original numbering. Mark in progress, log when done, then mark complete._

## Phase D — the subset, and honesty about the rest

- [ ] 10. Per-line verdicts and `PlantumlEditGuard`
  - File: `.../PlantumlEditGuard.cs` (new)
  - Record per line whether it was modelled, preserved-but-not-modelled, or structure-bearing-but-unresolved; refuse unsafe writes with a reason
  - _Requirements: 3.1, 3.3, 3.4, 3.5_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Classify every line and gate writes on it | Restrictions: the guard is consulted by the property provider, the action provider and every command, so the same element gets the same answer in all three places - a second implementation of the rule is how they drift | Success: an element declared inside an unevaluated preprocessor branch is refused identically wherever it is reached. Mark in progress, log when done, then mark complete._

- [ ] 11. The preserved-verbatim proof
  - File: `.../PlantumlDocument.Tests.cs` (modify)
  - Every construct in Requirement 3.3's list surviving a round trip **that edits something else in the same document**
  - _Requirements: 3.3, 3.6_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA-minded C# developer | Task: Prove the preserved list survives a real edit | Restrictions: a round trip with no edit proves nothing here - the case a naive writer breaks is an edit elsewhere in the same document, so every test in this task edits something | Success: skinparam, a style block, an include and a theme all survive a rename in the same file. Mark in progress, log when done, then mark complete._

## Phase E — arrangement

- [ ] 12. `PlantumlClassLayout`, and determinism pinned
  - File: `.../PlantumlClassLayout.cs` (new)
  - Layered arrangement, ranks from inheritance and realization edges, ties broken by declaration order
  - _Requirements: 6.1, 6.2, 6.4_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Arrange a class diagram as a pure function | Restrictions: ties break on declaration order in the document and never on a set's enumeration order - the difference is invisible in an ordinary passing test and reorders the diagram on another runtime; no clock, no randomness | Success: no overlaps, containers sized around contents with a margin, relationships not routed through boxes, and arranging the same document twice yields identical positions - the last asserted explicitly, not assumed. Mark in progress, log when done, then mark complete._

- [ ] 13. `PlantumlSequenceLayout`
  - File: `.../PlantumlSequenceLayout.cs` (new)
  - Participant order as the horizontal axis, document order as the vertical
  - _Requirements: 6.1, 6.3, 6.4_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Arrange a sequence diagram | Restrictions: this is a rendering of the document's order rather than a graph layout, and nothing about it is free for the module to choose (Requirement 6.3); activation bars are spans over the axis and frames are boxes around a contiguous run of it | Success: reordering two messages in the text moves exactly those two on the canvas. Mark in progress, log when done, then mark complete._

## Phase F — the core change

- [ ] 14. The connector gesture in core
  - File: `src/api/diagrams.proto` (modify), `src/backend/EtAlii.Adp.Diagram/IDiagramConnectorProvider.cs` (new)
  - `DescribeConnectors`, `DrawConnector` with `dry_run`, `ConnectorKind`, and the provider seam
  - Purpose: the one core change this spec asks for
  - _Leverage: `IDiagramToolboxProvider` and `DescribeToolbox`, whose shape this mirrors deliberately_
  - _Requirements: 8.1, 8.2_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working in core | Task: Add the connector gesture seam | Restrictions: core carries two element ids and an opaque kind id and learns nothing else - if core needs to know what a kind means, the seam is in the wrong place; keep `dry_run` as a flag on the one message rather than splitting into a validate RPC and a draw RPC, because one message is what stops the hover verdict and the release verdict from drifting apart; this is the ONLY core change - anything else that seems to need one is a finding to report, not a licence | Success: a type contributing no connectors answers with an empty list and the gesture is not offered. Mark in progress, log when done, then mark complete._

- [ ] 15. `PlantumlConnectorProvider`
  - File: `.../PlantumlConnectorProvider.cs` (new)
  - The six class kinds, the three sequence kinds, and accept-or-reason for probes
  - _Requirements: 8.3, 8.4, 8.6, 8.7, 8.8_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Describe and draw the module's connector kinds | Restrictions: a second relationship between the same two elements is permitted, because the notation permits it (Requirement 8.7); a gesture ending somewhere invalid cancels with no document change and a reason (8.6); nothing is offered in read-only mode (8.8) | Success: the dry-run verdict and the real verdict agree for every case, asserted by running both. Mark in progress, log when done, then mark complete._

## Phase G — toolbox, properties, actions

- [ ] 16. `PlantumlToolboxProvider`
  - File: `.../PlantumlToolboxProvider.cs` (new)
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Contribute the toolbox as data | Restrictions: items depend on the ACTIVE block's inferred notation, so a sequence diagram offers no Class and a class diagram no Loop; a drop position is a position in the TEXT, not a coordinate | Success: a new element arrives with a unique placeholder name offered for rename in place. Mark in progress, log when done, then mark complete._

- [ ] 17. `PlantumlContextPropertyProvider`
  - File: `.../PlantumlContextPropertyProvider.cs` (new)
  - _Requirements: 9.1 through 9.10_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Contribute properties for every selectable kind | Restrictions: this module is the property grid's fourth provider and introduces no panel change; an enumeration is a validated `Line` until the `Choice` editor exists, rejecting an unknown value with a reason rather than writing it (Requirement 9.8); an uneditable property is contributed WITH a read-only reason, never omitted (9.10) | Success: every kind in 9.2 through 9.7 contributes its rows, and a note contributes `Text` rather than `Line`. Mark in progress, log when done, then mark complete._

- [ ] 18. `PlantumlContextActionProvider` and the command set
  - File: `.../PlantumlContextActionProvider.cs`, `.../Commands/` (new)
  - Every edit in Requirement 10.1, each with an inverse
  - _Requirements: 10.1, 10.2, 10.3, 10.5, 10.6, 10.7, 10.8_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the actions and their commands | Restrictions: every command is a line operation on the document, which is what makes the inverse cheap - the inverse of an insertion is a removal of that range; deleting an element deletes the relationships that referenced it AND says how many before it runs (10.5); an unsafe edit is offered as unavailable with the guard's reason rather than hidden (10.8) | Success: every command round-trips through its inverse back to byte-identical text. Mark in progress, log when done, then mark complete._

- [ ] 19. The drags that mean something
  - File: `.../Commands/` (modify)
  - Dragging a type into a `package` or `namespace`; dragging a participant to reorder
  - _Requirements: 6.8_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Honour the two meaningful drags as ordinary commands | Restrictions: both are edits to the document and both are one undo away; neither stores a coordinate | Success: dragging a class into a package moves its declaration inside that block in the text. Mark in progress, log when done, then mark complete._

- [ ] 20. The refusal, with an explanation
  - File: `.../PlantumlContextActionProvider.cs` (modify), client (modify)
  - Requirement 6.5's refusal to reposition, naming what is available instead
  - _Requirements: 6.5, 6.7_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Refuse a repositioning drag with an explanation and offer the alternatives | Restrictions: this must read as an explanation of the notation rather than as a failure of ADP - a user arriving from a mouse-driven UML tool expects dragging to work, and this is the moment to say why this format differs; a silent no-op is the failure mode to avoid | Success: the refusal names direction hints, `together` grouping and hidden links, and each of those is an ordinary command with an inverse (Requirement 10.4). Mark in progress, log when done, then mark complete._

## Phase H — diagnostics, client, and the seam test

- [ ] 21. `PlantumlRuleSet` and `PlantumlValidator`
  - File: `.../PlantumlRuleSet.cs`, `.../PlantumlValidator.cs` (new)
  - _Requirements: 11.1 through 11.6_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Report what ADP could not do with the file | Restrictions: a preserved-but-unmodelled construct is reported at most ONCE PER KIND PER DOCUMENT as information - forty skinparam lines are one fact worth stating, not forty; an unresolved include is a warning because the canvas is showing less than the file means; an unparseable document is an error carrying the same message the unavailable state shows, so panel and canvas never disagree | Success: a document with forty skinparam lines produces one diagnostic. Mark in progress, log when done, then mark complete._

- [ ] 22. The client renderers
  - File: `src/diagrams/plantuml-uml/client/` (new)
  - Class and sequence renderers, the connector gesture's rubber band and validity indication
  - _Requirements: 8.5_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Render both notations and the in-progress gesture | Restrictions: the client draws what the backend sent and computes no positions; the gesture shows what would be created and whether the element under the pointer is a valid target, so the result is never a surprise | Success: both worked examples render recognisably as the diagram PlantUML draws from the same file. Mark in progress, log when done, then mark complete._

- [ ] 23. Exercise Requirement 12.4's seam claim
  - File: `.../PlantumlSeam.Tests.cs` (new)
  - Add a third notation far enough to prove it needs no change to the parser core, the arrangement layer or core
  - Purpose: 12.4 is a claim about the seams, and a claim is worth testing rather than asserting
  - _Requirements: 12.4_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Prove a new notation joins without touching the shared layers | Restrictions: take a third notation only as far as inference plus a minimal model and layout - the point is the seam, not the notation; if the parser core, `PlantumlDocument`, the mapper's vocabulary or core need editing, that is the finding and it is worth more than the notation | Success: the third notation registers and arranges with no diff in the shared layers. Mark in progress, log when done, then mark complete._

- [ ] 24. Update the catalog and verify the whole
  - File: `docs/diagrams.md` (modify)
  - Move `plantuml/uml` to its new state per CLAUDE.md's rule
  - _Requirements: 12.5, and the Non-Functional verification clauses_
  - _Prompt: Implement the task for spec plantuml-uml, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Update the diagram catalog and run the gates | Restrictions: `dotnet test --solution EtAlii.Adp.slnx` checked BY EXIT CODE, since a zero-test run exits 5 while printing no failures; `dotnet format style --verify-no-changes --severity info` exits zero before the worktree merges back | Success: both gates pass and the catalog row states the reached state. Mark in progress, log when done, then mark complete._
