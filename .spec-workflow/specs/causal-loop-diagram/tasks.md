# Tasks — causal-loop-diagram

Five groups, each gated and merged before the next begins. The sequencing has one deliberate feature worth stating before the list, because a reader who does not know the reason will reorder it:

> **The loop arithmetic is built first, before the canvas.** Computed loop polarity — reinforcing when the count of negative links is even, balancing when odd — needs no layout, no client and no algorithm beyond cycle enumeration, and it is the one thing this diagram type does that a drawing tool cannot. Build the canvas first and what exists at the end of the week is a drawing tool with a label field, and the check that justifies the module is still unwritten. Group 1 ends with a module that has no picture and can already tell you that your `R2` is arithmetically a balancing loop.

A note on guards, since the question will come up: this specification has **no list of offending sites**, because it adds a module rather than converting existing ones. The two-directional tracked-list guard that suits a conversion spec has no subject here. The guards that do apply are the three already in the tree — the private-scrollbar guard, the private-view-report guard, and the non-empty floor discipline — and every group below is answerable to them rather than to a new one.

## Group 1 — The model, and the loop arithmetic

- [ ] 1. A module that computes loop polarity before it draws anything
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 5.1, 5.2_

- [ ] 1.1 The module skeleton, its registration and its document factory
  - Files: `src/diagrams/causal-loop/backend/EtAlii.Adp.Diagram.CausalLoop/` — `Diagram.cs` (the definition: extension `.cld`, origin `systems/causal-loop-diagram`, `HasDocumentSibling`), `CausalLoopDocumentFactory.cs`, `ServiceCollection.AddCausalLoop.cs`; plus the `.Tests` project
  - **Register the `IDiagramDocumentFactory` in this task and not later.** A module declaring `HasDocumentSibling` without one ships unable to create a diagram at all — core refuses the creation path, and the startup check cannot see a module still under construction. sparql was corrected for exactly this and plantuml carries it as an open task. The factory's output must open and draw, not be an empty file the canvas then reports as unreadable
  - _Requirements: 1.1, 1.4, 5.1, 5.2_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer building a diagram module | Task: Create the module skeleton with its definition, service registration and document factory, and a test that the factory's document opens and draws | Restrictions: do not skip the document factory; the origin is `systems/causal-loop-diagram` exactly, `causal` not `casual` and the `-diagram` suffix is deliberate | _Leverage: any recent module's skeleton, and sparql's document-factory correction | Success: a new .cld can be created from the UI path and opens. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 1.2 The body format, the parser and the document
  - Files: `CausalLoopDocument.cs`, `CausalLoopParser.cs`, `_Model/` — `CausalLoopModel`, `CausalLoopVariable`, `CausalLoopLink` (`From`, `To`, `Polarity`, `Delayed`, `Weight`, `Label`), `CausalLoopLoop` (`Identifier`, `Name`, member links, and no position)
  - The design records XMILE as considered and rejected with three reasons; the format is this module's own, line-oriented, one statement per line so a new link is a one-line diff. Preserve line positions in the document so writers splice rather than reserialize
  - `Polarity` has three cases and `Unstated` is a real one — never default an unmarked link to positive, because Requirement 3.6 turns on telling "unknown" from "positive". Read `s` and `o` as the same two meanings as `+` and `-`
  - Add `*.cld -text` to `.gitattributes` **only if** this task's tests byte-compare documents, and then by extension and never by directory, per the reasoning already recorded in that file
  - _Requirements: 1.2, 1.3, 1.5, 2.2_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer designing a small text format | Task: Define the line-oriented .cld body format, write its parser and model, preserving line positions for later splicing | Restrictions: no XMILE, per the design's recorded reasons; an unmarked polarity is Unstated and never Positive; one statement per line | _Leverage: any module whose writer splices rather than reserializes | Success: a document round-trips, an unmarked link reads as Unstated, and s/o read as +/-. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 1.3 Cycle enumeration and computed polarity — the reason this module exists
  - Files: `CycleFinder.cs` (Johnson's elementary cycles over the link graph, bounded), `LoopPolarity.cs`
  - Reinforcing where the count of `Negative` member links is **even** — zero is even, and it is the case readers most often get wrong — balancing where odd, undecidable where any member link is `Unstated`
  - The bound is part of the contract, not a safety valve: when it is reached the count examined is carried out so the reader can be told, rather than the check silently doing less than it claims
  - Test the zero-negatives case explicitly, and a graph with nested and overlapping cycles, and a graph with none
  - _Requirements: 3.1, 3.2, 3.6_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer implementing a graph algorithm | Task: Enumerate the document's elementary cycles with Johnson's algorithm under a stated bound, and derive each cycle's polarity by the even/odd rule | Restrictions: zero negative links is even and therefore reinforcing; a cycle containing an Unstated link is undecidable, never assumed positive; the bound carries the examined count outward | _Leverage: AutoCLD uses Johnson's for this same purpose, cited in the design | Success: nested, overlapping, empty and zero-negative cases all correct; the bound reports rather than truncates silently. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 1.4 The validator: report the disagreement, never correct it
  - Files: `CausalLoopValidator.cs`
  - Four findings: a stated label disagreeing with the computed polarity, naming the loop and **both** labels; a cycle the document labels not at all, which is the finding this notation most exists to produce; a loop made undecidable by an unstated link; the cycle bound reached, with the count examined
  - **The stated label and the computed one stay separate all the way out.** Requirement 3.3 forbids silent correction because the author's label may be the intent and the arrows the mistake; a model that overwrote one with the other would make the finding impossible to express
  - _Requirements: 3.3, 3.4, 3.5_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the validator's four findings over the computed polarity | Restrictions: never rewrite a stated label to match the computed one; both must reach the caller; an unlabelled cycle is a finding, not a silence | _Leverage: the family validators for the finding shape and severities | Success: a document whose R2 is arithmetically balancing produces a finding naming both labels and changes no bytes. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 1.5 Gate and merge group 1
  - The four gates with exit codes captured before any pipe; merge through a fresh scratch worktree with `--no-ff` there and `--ff-only` into the main checkout; never merge in place while anything is staged
  - _Requirements: (gate)_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run all four gates, merge group 1 through a scratch worktree | Restrictions: do not merge on a failing gate; pick a worktree name not already spent | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

## Group 2 — Drawing it

- [ ] 2. The computed layout, the mapper, the session and the canvas
  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6, 7.1, 7.2, 7.3, 7.4, 9.1, 9.2, 9.3, 9.4, 10.1, 10.2, 10.3, 10.4_

- [ ] 2.1 The computed layout, and the unplaced-element contract
  - Files: `CausalLoopLayout.cs`
  - The ordinary layout a document opens with — not the self-organizing one, which is group 4 and is invoked
  - **An element the layout could not place must be distinguishable from one placed at `(0, 0)`.** Four modules shipped the origin as a fallback and a viewport then culled those elements everywhere except the top-left corner. Either place deliberately at a stated fallback documented as one, or exclude from the drawn set, and make the choice visible in code rather than emergent from `default`
  - _Requirements: 7.1, 7.2_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Compute the default layout and make an unplaced element distinguishable from one at the origin | Restrictions: no `? position : default` returning the origin as a silent fallback | _Leverage: diagram-layout's unplaced-element contract | Success: a test asserts every drawn element has a position, and asserts the drawn set is non-empty first. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2.2 The mapper, and the two things that have no position
  - Files: `CausalLoopElementMapper.cs`
  - Variables carry positions; **links and loop labels do not**. A link is drawn between its endpoints, a loop label at the centroid of its members. Both are therefore filtered **structurally** — a link when both endpoints survive, a loop label when enough members do — and never by testing a position they do not have
  - Every sweep this task adds asserts its collection is non-empty before walking it, per the floor discipline, so no guard here can pass vacuously
  - _Requirements: 7.3, 7.4_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Map variables, links and loop labels to core elements, filtering links and loop labels structurally | Restrictions: never give a link or a loop label a position of its own; no vacuous Assert.All without a non-empty floor before it | _Leverage: helm-charts filters edges on both endpoints surviving | Success: a viewport that admits one endpoint drops the link, and the loop label follows its members. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2.3 The session and the view-delta loop, backend half
  - Files: `CausalLoopSession.cs`, `CausalLoopSessionFactory.cs`
  - `UpdateView` answers a changed viewport with what came into view and what left it — **Add for what appeared, then Remove for what left**, the order both reference sessions emit in
  - **Lay out the whole document, then filter.** Laying out only the visible set makes the diagram crawl under the reader as they pan
  - The test that counts is the backend one: a view change produces deltas. A client-side assertion that `reportView` was called passes against a session that answers with nothing
  - _Requirements: 10.2, 10.3, 10.4_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the session's baseline and UpdateView, laying out the whole document and filtering the result | Restrictions: Add then Remove; never lay out only the visible set; the completion test is behavioural and in the backend | _Leverage: the view-delta-adoption reference sessions | Success: a backend test proves a viewport change produces deltas, and fails against a `return []`. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2.4 The canvas, on the shared appearance
  - Files: `src/diagrams/causal-loop/client/` — `CausalLoopCanvas.tsx`, `causal-loop.css`, `causalLoopModel.ts`, `register.ts`
  - Compose the shared `canvas-*` classes and import `canvas.css`; use the shared bezier connections, `.canvas-arrowhead` and the shared `CanvasScrollbars`. Be the sixth module that composes rather than the sixth that carries a private stylesheet
  - The notation, drawn as the world draws it: polarity at the arrowhead end; a delay as the conventional strokes across the link; a loop's `R`/`B` identifier and name at its centroid, not in a legend; a reinforcing loop distinguishable from a balancing one without selecting anything
  - **Link weight maps to a small ordered ladder of shared classes, not to a continuous stroke width.** There is no shared thickness abstraction to reuse — line weight is `stroke-width` in `canvas.css` and it offers two visible widths. If the ladder needs a step the shared stylesheet lacks, **add it to the shared stylesheet**, where every module gets it; a module-private appearance file is what Requirement 9.1 exists to prevent and what five modules already carry 104–261 lines of
  - Verify against how the notation is actually drawn in the surveyed sources, not against the design's description of them
  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6, 9.1, 9.2, 9.3, 9.4_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript/React developer | Task: Build the canvas on the shared classes, drawing polarity, delays and loop identifiers in the established notation | Restrictions: no private scrollbars, no private appearance stylesheet; a needed thickness step goes into the shared stylesheet | _Leverage: the six modules that compose canvas.css | Success: the private-scrollbar guard passes, and the notation matches the sources. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2.5 The stream hook and the view report, client half
  - Files: `useCausalLoopStream.ts`
  - Build `reportView` with the shared `viewReportOf` on the client the hook already holds, and wire `useViewReport` to the canvas's own view state. Add no module-local copy of `Viewport`, `shownRectOf` or the debounce constant — a guard fails a module that declares its own
  - _Requirements: 10.1_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Expose reportView from the stream hook using the shared library and wire useViewReport to the canvas view state | Restrictions: use the shared library, declare none of its names locally | _Leverage: @client/diagrams/viewReport and useViewReport | Success: the no-private-view-reports guard passes. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2.6 Gate and merge group 2
  - _Requirements: (gate)_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run all four gates and merge group 2 through a scratch worktree | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

## Group 3 — Editing it

- [ ] 3. Commands, context actions and the property grid
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.3, 5.4, 5.5, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

- [ ] 3.1 The writers and their commands
  - Files: `CausalLoopWriter.cs`, `Commands/` — add/rename/remove variable; add/remove link and set its polarity, delay and weight; add/remove loop and set its name and membership
  - Each is an `ICommand` with an inverse restoring the document byte for byte, spliced rather than reserialized
  - **Removing a loop removes the loop statement and not its links.** A link belongs to the diagram; a loop is a claim about a path through it. The inverse restores the loop statement alone
  - A refused edit uses one sentence, shared, wherever that refusal is reachable
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Write the edit commands with byte-restoring inverses, splicing into the document | Restrictions: removing a loop must not remove its links; one shared refusal sentence; no reserialization | _Leverage: the family writers' splice discipline | Success: every command round-trips byte for byte, and a minimal-diff test proves the splice touched only the lines it must. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 3.2 Context actions and the toolbox
  - Files: `CausalLoopContextActionProvider.cs`, `CausalLoopToolboxProvider.cs`
  - Every gesture from 3.1 discovered through the standard provider path; an action that cannot apply is discovered **unavailable with its reason** rather than silently absent; the toolbox is described by the backend as data and rendered by a palette that does not understand it
  - _Requirements: 5.3, 5.4, 5.5_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Offer the edit gestures through the context provider and the toolbox | Restrictions: unavailable-with-reason, never silently absent; the toolbox is data from the backend | Success: every command is reachable from the menu and each refusal states why. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 3.3 The property grid
  - Files: `CausalLoopContextPropertyProvider.cs`
  - A variable's label; a link's weight, with its polarity and delay readable; a loop's identifier, name, **computed** polarity and the stated one where they differ — which is where a reader meets Requirement 3.3's disagreement without opening the problems panel
  - A weight is an annotation this module records and never evaluates; this diagram type states structure and simulates nothing
  - A row that cannot be edited states why rather than presenting a disabled box with no explanation
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the property grid for variables, links and loops, dispatching edits as commands | Restrictions: a weight is never evaluated; every read-only row carries a reason; show both polarities on a loop where they disagree | _Leverage: the family property providers | Success: setting a label or a weight lands as an undoable command and the loop rows show both labels. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 3.4 Gate and merge group 3
  - _Requirements: (gate)_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run all four gates and merge group 3 through a scratch worktree | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

## Group 4 — The self-organizing layout

- [ ] 4. Meyer's method, with the randomness removed
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8_

- [ ] 4.1 The self-organizing pass, made deterministic
  - Files: `SelfOrganizingLayout.cs`
  - Meyer's self-organizing graphs — competitive learning from Kohonen's map with the neighbourhood measured in **graph** distance, which is what makes it a graph layout rather than a clustering
  - The three sources of randomness are replaced one for one: initial positions from a phyllotaxis spiral in the document's stable order; stimuli from a fixed-length low-discrepancy sequence computed from the iteration index; tie-breaking by the stable document order. A fixed iteration count, and a learning-rate and radius schedule that is a function of the iteration index alone. No wall-clock, no random source, no settling animation
  - Offered as a **diagram-wide context action the user invokes**, never as what happens when a document opens
  - Runs behind the drawn-element budget, so the unbounded-at-scale failure cannot arise
  - **The determinism test must run in two processes.** A same-process comparison passes against a cached result and proves nothing
  - _Requirements: 6.1, 6.2, 6.3, 6.4_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer implementing a neural layout | Task: Implement Meyer's self-organizing graph layout with every source of randomness replaced by a function of the document and the iteration index | Restrictions: no RNG, no wall-clock, no animation-to-rest; invoked as an action, never on open; behind the drawn-element budget | _Leverage: layout-modes' Cloud requirement, which answers the same three grounds | Success: the same document laid out in two different processes gives identical positions. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.2 The separation pass, and the honest refusal
  - Files: `SelfOrganizingLayout.cs` (second phase)
  - **A converged self-organizing map does not guarantee non-overlap and the requirement does.** A bounded deterministic separation pass pushes overlapping boxes apart in stable order for a fixed number of rounds
  - If overlaps survive the rounds, the action **refuses for that document and reports the size at which it failed**. That is the correct outcome, not a failure of nerve, and not grounds for relaxing the overlap rule
  - **The extent ratio is not the guard here and the code should say so.** A hairball is roughly square: it scores near 1:1 and passes a ratio bound comfortably while being exactly the failure the ruling names. Non-overlap at the budget's full size is what bites
  - Measure on real documents at real sizes rather than on synthetic fixtures
  - _Requirements: 6.5, 6.6, 6.7_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add the deterministic separation pass and the refusal path when it cannot converge | Restrictions: do not relax the overlap rule to make a document pass; do not use the extent ratio as the unreadability guard; measure on real documents | Success: no two boxes intersect at budget size, or the action refuses naming the size. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.3 Persisting the arrangement
  - Files: the action's command path
  - The result is written through `SetRegistrationLayoutCommand` into the `.adp`'s `layout:` block, so the arrangement is **one undo away** like every other edit, and everything above the block survives byte for byte
  - _Requirements: 6.8_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Persist the computed arrangement as authored positions through the existing layout command | Restrictions: no new persistence mechanism; preserve everything above the layout block byte for byte | Success: running the action then undoing restores the registration byte for byte. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.4 Gate and merge group 4
  - _Requirements: (gate)_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run all four gates and merge group 4 through a scratch worktree | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

## Group 5 — Examples, catalog and the manual pass

- [ ] 5. What a reader opens, and where the type is listed
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 12.1, 12.2, 12.3_

- [ ] 5.1 Examples
  - Files: `src/diagrams/causal-loop/examples/`, replicated into `src/examples/diagrams/causal-loop/`
  - **Four candidate sources were checked and all four rejected**, recorded in the requirements so the search is not repeated: nocomplexity/causalloopdiagram is GPL-3.0, AutoCLD carries no licence file at all, Wikipedia's figures are CC BY-SA, and MetaSD offers only per-model author permission on Vensim stock-and-flow models. A permissive source is still preferred **if one can be verified from the data at acquisition**; otherwise the examples are authored for this repository and **labelled as authored**, with no attribution to a source that did not license them
  - At least one example exercises the hard cases together: a reinforcing loop, a balancing loop, a delayed link, a variable in more than one loop, and **a loop whose stated label disagrees with its computed polarity**, so the group 1 finding is demonstrable from a shipped file
  - Each beside its own `.adp`, opening from the explorer with no setup; replicated into the central showcase in its folder-per-type arrangement
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Developer executing the vendoring discipline | Task: Ship examples, preferring a verifiable permissive source and otherwise authoring them under the honest-fallback rule | Restrictions: the four recorded rejections are not revisited for convenience; an authored example is labelled as authored and never attributed to an unlicensed source | Success: examples open with no setup and one demonstrates the label disagreement. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5.2 The catalog row
  - Files: `docs/diagrams.md`
  - A row for the causal loop diagram, which the catalog has none of today, with the state icon matching its real state and the origin tag `systems/causal-loop-diagram`
  - _Requirements: 12.1, 12.2, 12.3_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Developer | Task: Add the catalog row with its state icon and origin tag | Restrictions: edit in place; the origin is `systems/causal-loop-diagram` exactly | Success: the row is present and the catalog coherence test passes. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5.3 Final gate, merge, retire, manual pass
  - The four gates with exit codes checked; merge through a scratch worktree; retire it, reporting a removal failure rather than forcing it; add the design's three `tests.md` entries and run them against the running app
  - _Requirements: (gate + the manual halves of 2, 3 and 6)_
  - _Prompt: Implement the task for spec causal-loop-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run all four gates, merge, retire the worktree, and execute the manual pass | Restrictions: do not merge on a failing gate; report worktree-removal failures rather than forcing them | Success: gates green, merged, worktree retired, manual pass recorded. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
