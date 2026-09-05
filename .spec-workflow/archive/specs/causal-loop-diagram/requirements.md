# Requirements — causal-loop-diagram

## Introduction

A causal loop diagram states how the parts of a system feed back on each other. It is the entry notation of system dynamics: variables joined by causal links, each link carrying a **polarity**, and the cycles those links form labelled as **reinforcing** or **balancing**. It is the one diagram in this catalog whose central claim is about *cycles* rather than about hierarchy or containment, and that shapes almost every requirement below.

This specification adds it as a diagram type with extension `.cld` and meta identifier `systems/causal-loop-diagram`.

### The notation, as the world draws it

Requirement 2 adopts the established convention rather than inventing one, so it is worth writing down what that convention is:

- A **causal link** is an arrow from a cause variable to an effect variable.
- **Polarity** marks the arrow. A `+` link means an increase in the cause produces an increase in the effect, and a decrease produces a decrease — "all else equal". A `-` link means the opposite: an increase in the cause produces a decrease in the effect. Some traditions write `s` (same) and `o` (opposite) for the same two meanings.
- A **delay** on a link is drawn as a short line — conventionally two short strokes — across the arrow.
- A **loop** is labelled `R` when reinforcing and `B` when balancing, and numbered in a sequence of its own: `R1`, `B1`, `B2`, with a descriptive name beside the identifier.

And the rule that makes a loop's label checkable rather than a matter of opinion:

> **A loop is reinforcing when it contains an even number of negative links — zero counts as even — and balancing when it contains an odd number.**

That is a mechanical property of the drawn graph. Requirement 3 makes this repository compute it rather than trust it, which is the single most valuable thing this diagram type can do that a drawing tool does not.

### What the established tools do

**Adopted, with reasons.**

- **Polarity on the link, loop identifier in the loop** — universal across the surveyed material, and the notation a reader arrives already knowing.
- **Computed loop polarity.** AutoCLD enumerates a diagram's cycles with **Johnson's elementary cycles algorithm** and derives which are reinforcing and which are balancing. Finding the cycles is the hard half; once found, the even/odd rule decides the label. A tool that draws a CLD and cannot tell you that your `R2` is arithmetically a balancing loop has left its most useful check on the table.
- **A self-organizing layout as an explicitly invoked action**, per the user's request and Requirement 6, rather than as what happens on open.

**Rejected, with reasons.**

- **Stocks and flows.** Converting a causal loop diagram into a stock-and-flow model is a well-documented next step and a different diagram type; this one draws the causal structure and does not simulate it.
- **Simulation of any kind.** No integration, no time series, no equations. A weight on a link (Requirement 8) is an annotation the author writes, never a coefficient this module evaluates.
- **Non-deterministic layout.** See Requirement 6; the ruling is this repository's and it stands.

### Examples: four sources checked, none vendorable

The house vendoring rule requires a permissive licence verified from the data at acquisition, with the licence text itself carried beside it as `LICENSE.md`. Four candidate sources were checked and **all four fail that test**, which is recorded here so the search is not repeated:

- `github.com/nocomplexity/causalloopdiagram` — **GPL-3.0**. Share-alike; rejected by the same rule that rejected the share-alike sources in `shacl-diagram`.
- `github.com/elbazjosh/AutoCLD` — **no licence file at all**, which reserves all rights. Its algorithm is citable, as above; its content is not vendorable.
- The Wikipedia article and its figures — **CC BY-SA**, share-alike again.
- The MetaSD model library — permission stated only as models that "may be freely used and distributed, subject to authors' copyright", which is not a named licence and is per-model rather than per-library; and the models are Vensim stock-and-flow simulations rather than causal loop diagrams.

Requirement 11 therefore leads with the honest-fallback path rather than treating vendoring as the expected outcome.

### The identifier, settled

`systems/causal-loop-diagram`, confirmed by the user on both points a meta identifier is expensive
to get wrong on - an `.adp` registration names the origin on its first line, so a later change
would have to reach every document already carrying it.

1. **`causal`, not `casual`.** The identifier was first given as `casual-loop-diagram`; every
   source and this document's own prose spell the notation **causal**, and the user confirmed the
   correction. Recorded here because the misspelling is a plausible typo for a reader to
   reintroduce, and because a search of the tree for the wrong spelling should find this note and
   nothing else.
2. **The `-diagram` suffix stays, deliberately.** It is a departure from the catalog's convention
   and the departure is intended: no other origin carries a `-diagram` suffix on its type segment,
   and the one origin whose type is literally `diagram` is `d2/diagram`, where `d2` is the vendor
   and `diagram` is what it draws. `systems/causal-loop` would have matched the pattern. The user
   chose the longer form, so it is not to be "tidied" into the short one later by someone who
   notices the inconsistency and assumes it was an oversight.

## Alignment with Product Vision

- **Files are the source of truth.** A `.cld` is a plain, diffable text document reviewed in the same pull request as the code it describes.
- **Don't reinvent, integrate.** The drawn notation is the established one, and Requirement 1 requires an existing text format to be adopted for the body if one fits before a new one is defined.
- **Linkage over illustration.** A causal loop diagram is a claim about a system's feedback structure; the loops this module computes are a check on that claim rather than a decoration of it.

## Requirements

### Requirement 1 — The file, its registration, and its body format

**User Story:** As a user, I want a causal loop diagram to be an ordinary file in my repository, so that it is reviewed, branched and merged like everything else.

#### Acceptance Criteria

1. WHEN the module is registered THEN it SHALL declare the extension `.cld` and the meta identifier `systems/causal-loop-diagram`.
2. WHEN a body format is chosen THEN an existing text-based format SHALL be adopted if one fits, per the steering rule, and the choice SHALL be recorded with its reasons — including why XMILE, the OASIS interchange standard for system dynamics, was or was not adopted, given that it carries stocks, flows and equations this diagram states none of.
3. WHEN a body format is defined THEN it SHALL be diffable line by line, so that adding one link produces a one-line diff rather than a rewritten file.
4. WHEN the diagram is registered THEN the `.adp` registration SHALL carry the origin on its first line and the body beside it, per the file-pair contract.
5. IF `.cld` documents are byte-compared by any test THEN `*.cld -text` SHALL be added to `.gitattributes` **by extension and never by directory**, per the reasoning already recorded in that file.

### Requirement 2 — The drawn notation is the established one

**User Story:** As someone who already reads causal loop diagrams, I want this one to look like the ones I know, so that I do not have to learn a private notation to read my own diagram.

#### Acceptance Criteria

1. WHEN a causal link is drawn THEN it SHALL be an arrow from cause to effect carrying its **polarity**, marked as `+` or `-` in the convention the notation uses.
2. WHERE a diagram states polarity as `s` and `o` rather than `+` and `-` THEN both SHALL be read as the same two meanings, because both traditions are in live use.
3. WHEN a link carries a **delay** THEN it SHALL be drawn with the conventional mark across the arrow rather than with a private symbol or a colour.
4. WHEN a loop is drawn THEN it SHALL carry its identifier — `R` or `B` with its number — and its descriptive name, placed with the loop rather than in a legend.
5. WHEN the diagram is drawn THEN a reader SHALL be able to tell a reinforcing loop from a balancing one without selecting anything.
6. WHEN this requirement is verified THEN it SHALL be checked against how the notation is drawn in the surveyed sources, not against this document's description of them.

### Requirement 3 — Loop polarity is computed, not trusted

**User Story:** As an author, I want to be told when a loop I have labelled reinforcing is arithmetically balancing, so that my diagram cannot quietly claim something its own arrows contradict.

#### Acceptance Criteria

1. WHEN a document is read THEN the module SHALL enumerate the diagram's cycles rather than relying on the author to have found them.
2. WHEN a cycle is found THEN its polarity SHALL be derived by the established rule: **reinforcing where the count of negative links is even, balancing where it is odd**.
3. WHEN a computed polarity disagrees with the label the document states THEN it SHALL be reported as a finding naming the loop, the stated label and the computed one — and the document SHALL NOT be silently corrected, because the author's label may be the intent and the arrows the mistake.
4. WHEN a cycle exists that the document labels not at all THEN that SHALL be reported as a finding, since an unlabelled feedback loop is the thing this notation exists to make visible.
5. WHERE a diagram is large enough that enumerating every elementary cycle is expensive THEN the enumeration SHALL be bounded and the bound SHALL be stated to the reader, in the manner of the drawn-element budget, rather than the check silently doing less than it claims.
6. WHEN a link's polarity is missing THEN the loops through it SHALL be reported as undecidable rather than assumed positive.

### Requirement 4 — Editing loops, links and variables

**User Story:** As an author, I want to add, change and remove the parts of my diagram, so that I can think with it rather than only look at it.

#### Acceptance Criteria

1. WHEN any edit is made THEN it SHALL dispatch as a command with an inverse that restores the document byte for byte, and SHALL be one undo away.
2. WHEN a reinforcing or balancing loop is added THEN the loop's identifier, its name and its member links SHALL be stated in the document by that one command.
3. WHEN a loop is edited THEN its name, its identifier and its membership SHALL each be changeable, and the change SHALL be reflected in the computed polarity of Requirement 3 rather than in a stored answer that can drift.
4. WHEN a loop is removed THEN the links it names SHALL survive unless they are removed by their own gesture, because a link belongs to the diagram and not to the loop that happens to traverse it.
5. WHEN a variable or a link is added or removed THEN it SHALL be the same kind of command with the same undo guarantee.
6. WHEN an edit cannot apply THEN it SHALL be refused with a sentence naming why, and the same sentence SHALL be used wherever that refusal is reachable.

### Requirement 5 — Context actions, the toolbox, and the document factory

**User Story:** As a user, I want to create a causal loop diagram and act on it through the same menus and palette every other diagram type uses.

#### Acceptance Criteria

1. WHEN the module is registered THEN it SHALL register an `IDiagramDocumentFactory`. This is not an implementation detail: a module declaring `HasDocumentSibling` without one ships **unable to create a diagram at all**, because core refuses the creation path, and the startup check cannot see a module still under construction. `sparql` was corrected for exactly this and `plantuml` carries it as an open task.
2. WHEN a new document is created THEN the factory SHALL produce a document that opens and draws — not an empty file that the canvas then reports as unreadable.
3. WHEN an element is selected THEN the context actions of Requirement 4 SHALL be discovered through the standard provider path, and each SHALL be an `ICommand` with an inverse.
4. WHEN an action cannot apply THEN it SHALL be discovered unavailable with its reason rather than silently absent.
5. WHEN the toolbox is shown THEN what this type contributes SHALL be described by the backend as data, and the palette SHALL render it without understanding it.

### Requirement 6 — The self-organizing layout, and the determinism it must not break

**User Story:** As an author whose diagram has grown tangled, I want one action that arranges it sensibly, so that I can go on reading it.

This requirement adopts Meyer's self-organizing graph method — competitive learning extended from Kohonen's self-organizing map, applied to graph layout — as the user asked. It is a **stochastic** method by default, and this repository has ruled against stochastic layout on three separate grounds. The ruling is not waived here; each ground is answered, exactly as `layout-modes` answers them for its Cloud mode, and a layout that clears only one does not ship.

#### Acceptance Criteria

1. WHEN the layout is offered THEN it SHALL be a **diagram-wide context action the user invokes**, never what happens when a document opens.
2. **The determinism ground.** WHEN the layout runs THEN it SHALL be a pure function of the document: a fixed iteration count, initial weights and input presentation order derived from a stable ordering the document itself supplies, a learning-rate and neighbourhood schedule that is a function of the iteration index alone, no wall-clock, and no random source.
3. WHEN the same document is laid out twice, in different processes THEN the positions SHALL be identical — which is what preserves the same-file-opens-the-same-way rule and the authored-position overlay both.
4. **The memory ground.** WHEN the layout runs THEN it SHALL run behind the drawn-element budget, so the "unbounded at scale" failure cannot arise.
5. **The unreadability ground, which is the one that decides whether this layout is real.** WHEN the layout runs at the budget's full size THEN **no two drawn elements' bounding boxes SHALL intersect**. The extent ratio SHALL NOT be relied on to guard this, because a hairball is roughly square: it scores near 1:1 and passes a ratio bound comfortably while being exactly the failure the ruling names.
6. WHEN the layout is verified THEN it SHALL be measured on real documents at real sizes rather than on synthetic fixtures.
7. IF the layout cannot be both deterministic and overlap-free at the budget's size THEN the action SHALL be refused for that document with the size at which it fails, recorded as a finding — an honest refusal is the correct outcome and is not grounds for relaxing the overlap rule.
8. WHEN the layout has run THEN its result SHALL be stored as authored positions through the existing mechanism, so that it is one undo away like every other edit.

### Requirement 7 — Every drawn element has a place, and says so when it does not

**User Story:** As a reader, I want an element the layout could not place to be reported rather than parked in the top-left corner, so that a defect shows up as a defect instead of as an element that vanishes when I pan.

#### Acceptance Criteria

1. WHEN the mapper needs a position for an element the layout did not place THEN it SHALL distinguish that absence from a real position of `(0, 0)`, rather than receiving the origin as a fallback.
2. WHEN an element has no computed position THEN it SHALL either be placed at a stated fallback documented as one, or excluded from the drawn set, and the choice SHALL be visible in code.
3. WHEN a viewport filters the drawn set THEN **links SHALL be filtered structurally** — on whether both of their endpoints survived — and never by a position of their own, because a link has none.
4. WHEN a test asserts that every drawn element has a position THEN it SHALL first assert that the drawn set is not empty, since the claim is vacuously true of a diagram that drew nothing.

### Requirement 8 — The property grid

**User Story:** As an author, I want to set a variable's label and a link's weight where I set everything else, so that the diagram's text is editable without opening the file.

#### Acceptance Criteria

1. WHEN a variable is selected THEN the property grid SHALL show and set its label.
2. WHEN a causal link is selected THEN the grid SHALL show and set its **weight**, for both balancing and reinforcing connections, and its polarity and delay SHALL be readable there.
3. WHEN a weight is set THEN it SHALL be an annotation the document records and this module never evaluates — this diagram type states structure and simulates nothing.
4. WHEN a loop is selected THEN the grid SHALL show its identifier, its name, its computed polarity and the stated one where they differ.
5. WHEN a value cannot be edited THEN the row SHALL state why rather than presenting a disabled box with no explanation.
6. WHEN a grid edit is made THEN it SHALL dispatch as a command with an inverse, like every other edit.

### Requirement 9 — The shared canvas and the shared appearance

**User Story:** As a user, I want this diagram to look and behave like the others, so that what I learned in one canvas holds in the next.

#### Acceptance Criteria

1. WHEN the canvas is built THEN it SHALL compose the shared `canvas-*` classes and import the shared stylesheet, joining the six modules that already do rather than the five carrying private appearance.
2. WHEN thickness, weight or emphasis is drawn THEN the shared appearance and thickness styles SHALL be used rather than module-private ones.
3. WHEN the canvas needs scrollbars THEN it SHALL use the shared ones; a guard enforces this and a private implementation fails it.
4. WHERE this notation needs something the shared appearance does not offer — the delay mark, the loop identifier — THEN it SHALL be added as this module's own on top of the shared base, and the addition SHALL be the exception rather than the starting point.

### Requirement 10 — The view-delta loop

**User Story:** As a reader of a large diagram, I want panning and zooming to bring me the content I have moved to.

#### Acceptance Criteria

1. WHEN the canvas view changes THEN the client SHALL report the new viewport through the shared library, and SHALL NOT build its own report — a guard fails a module that does.
2. WHEN a viewport is reported THEN the backend SHALL answer with what came into view and what left it, and SHALL NOT answer the same regardless of the viewport it was given.
3. WHEN a viewport is reported THEN **the whole document SHALL be laid out and the result then filtered**, never the visible set laid out alone, which makes the diagram crawl under the reader as they pan.
4. WHEN the module is judged complete THEN the test SHALL be behavioural in the backend: a view change produces deltas.

### Requirement 11 — Examples

**User Story:** As someone evaluating ADP, I want to open a real causal loop diagram without building one first.

#### Acceptance Criteria

1. WHEN examples are acquired THEN a permissively licensed source SHALL be preferred, its licence verified from the data at acquisition rather than from a project page, and the licence text vendored beside the data as `LICENSE.md` with a provenance readme.
2. WHEN no permissively licensed source can be verified THEN examples SHALL be **authored for this repository and labelled as authored**, with no attribution to a source that did not license them — the four rejections recorded in the introduction are not to be revisited for convenience.
3. WHEN examples are shipped THEN at least one SHALL exercise the notation's hard cases together: a reinforcing loop, a balancing loop, a delayed link, a variable participating in more than one loop, and a loop whose stated label disagrees with its computed polarity so that Requirement 3's finding is demonstrable.
4. WHEN examples are shipped THEN each SHALL sit beside its own `.adp` registration and SHALL open from the explorer with no setup.
5. WHEN examples are shipped THEN they SHALL be replicated into the central showcase tree, in the folder-per-type arrangement that tree already uses.

### Requirement 12 — The catalog

**User Story:** As someone reading the diagram catalog, I want to find this type there with an honest state.

#### Acceptance Criteria

1. WHEN this specification exists THEN `docs/diagrams.md` SHALL carry a row for the causal loop diagram, which it does not today.
2. WHEN the row is written THEN it SHALL carry the state icon matching its real state and the origin tag `systems/causal-loop-diagram`.
3. WHEN the state changes THEN the row SHALL be updated in place, per the catalog rule.

## Non-goals

- **No simulation.** No integration over time, no equations, no computed behaviour. A weight is an annotation.
- **No stock-and-flow conversion.** A documented and valuable next step, and a different diagram type.
- **No automatic correction of a mislabelled loop.** Requirement 3 reports the disagreement; the author decides which side is wrong.
- **No layout on open.** The self-organizing layout is an invoked action, and the ordinary computed layout is what a document opens with.
- **No private appearance, no private scrollbars, no private view report.** Three guards already enforce these and this module is not an exception to any of them.
- **No new persistence mechanism.** Authored positions use the `.adp` `layout:` block that every other module uses.
