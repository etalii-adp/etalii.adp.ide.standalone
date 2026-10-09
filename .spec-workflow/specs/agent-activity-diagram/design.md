# Design Document

## Overview

The Agent activity diagram is a diagram module, `src/diagrams/agent-activity-diagram`, built the way the Gartner hype cycle graph is: its type is specified in DISL in etalii.adp and bundled into the module, its file is read and written by this host's FBL runtime through a declared YAML binding, and its canvas is compiled from the bundled specification. The module itself stays thin. Most of the work is in three places it draws on:

1. **The languages** (etalii.adp): seven gaps, each a decision below, then the definition files.
2. **The canvas library and the two language runtimes** (this host's core): lists inside an element with collapsible groups, a link that can be activated, locked and unlocked elements, a kept canvas switch, and a layout that radiates by element type. None exists today.
3. **The document lifecycle** (this host's core): this is the first diagram whose file is routinely written by another program while it is open, and two things the lifecycle does today lose such a write.

The requirements were approved on the dashboard on 2026-10-09 without comment, so Q1 to Q7 stand at their defaults and R1 to R10 as read: a YAML file through FBL, view state in the file, agents edit the file, a second specification for an agent is refused, and each gap is closed in the language.

**This design asks for four small amendments to the approved requirements**, listed under *Amendments asked for*. Each follows from a decision below and none changes what the user sees of the request.

## How this was read

This repository on `develop` at `4e28e5b9` and etalii.adp on `origin/develop` at `da64ff0`, by reading sources, not by running a trial. Names below are symbols and paths as they stood then. Where a claim is an inference it says so, and task 1 is a trial that replaces the inferences about the languages with a working binding and specification before anything else is built on them.

The coordinator pointed to `docs/research/multi-agent-coordination.md` (etalii.adp pull request 103), whose last part lists what a diagram of agent activity would have to show. Three of its rows are what this diagram shows: the specification each agent works from and its state, what waits on a person (Input Required), and agents that share a location or an environment. Four are not in the approved requirements and are not added here: tasks claimed by an agent, instruction files, a branch's state against its base, and leftover locations. They are noted under *Risks* as the likeliest next requests.

## Steering Document Alignment

### Technical Standards (tech.md)

The backend is the only reader and writer of the file and the client speaks gRPC; changes reach an open diagram on the existing `Watch` stream as `Delta` messages. Logging follows the static `ILogger` convention. Nothing in core names this module.

### Project Structure (structure.md)

One module folder with `api`, `backend`, `client`, `definition` and `examples`. Every capability added to `src/client/src/canvas/library`, `EtAlii.Adp.Specification.Disl`, `EtAlii.Adp.Specification.Fbl` and `EtAlii.Adp.Diagram` is named for what it does and is declared against by the module.

## Code Reuse Analysis

### Existing Components to Leverage

- **`src/diagrams/gartner-hype-cycle-graph`** as the template: `GhgBody` over `OpenBody`, `GhgDefinition` over `BundledDefinition.Load`, a store over `WritableDocumentLifecycle`, a reloader, a document factory, a session over `DiagramDocumentChangeHandler`, context providers, and one command per edit. It is the only module with a declared FBL reader on the `yaml` family together with DISL derivation.
- **`EtAlii.Adp.Specification.Fbl`**: `OpenBody.Open`, `Plan`, `Change(ModelChange)` and `Apply`, with nested entries (`Rule.Parent`), references (`ReferenceBinding`), `remove.cascade` and per-attribute `empty` rules. View data in a body is already ordinary bound attributes there: the hype cycle graph keeps a row and a note's size in its file.
- **`EtAlii.Adp.Specification.Disl`**: `ToolboxDerivation`, `ContextMenuDerivation`, `FormDerivation`, `ConstraintEvaluator`, `GestureConstraintEvaluator`, `OperationInterpreter`, with CEL through `EtAlii.Adp.Specification.Cel`.
- **`compileNotation`** in `src/client/src/canvas/library/disl`, which turns a specification and its `NotationBindings` into a `DiagramDefinition`.
- **`DiagramDocumentReloadBridge`, `RootFolderWatcher`, `SelfWriteGuard` and `DiagramDiff.Between`**: an outside change already reaches an open canvas as deltas, and elements that did not change are not redrawn.
- **`AddDiagramContextActionProvider` and `CreateDiagramFileCommandHandler`**: adding a file creates the registration and the body together or neither.
- **`revealPath`** on the context connection, for a link to a file inside the project.
- **`src/diagrams/tools/bundle-disl.sh`** and the `BundledDefinition.Tests` pattern, for bundling the definition and holding it to its provenance.

### What does not exist and is added

| # | What | Where | Requirements |
| --- | --- | --- | --- |
| B1 | Lists inside an element: rows a user can point at, select and open a menu on, under headings that collapse, with the element's height following | canvas library; today a `CollectionBinding` draws lines of text that cannot be hit, and `elementBounds` takes height from the model only | 4.1 to 4.5, 9.3, 9.4 |
| B2 | A symbol on an element or a row that can be activated | canvas library; today a `DecorationDeclaration` is "no hit-testing, no gesture" | 5.2, 5.7 |
| B3 | Opening a link: a web address in a new tab, a project path through `revealPath`, anything else shown with a copy action | shell; no `window.open` exists in the client today | 5.3 to 5.6 |
| B4 | Locked and unlocked elements under an automatic layout: a locked element keeps its stored position, a drag locks | canvas library; today a layout's position replaces every element's, with no element exempt | 6.1 to 6.4, 6.6, 6.11 |
| B5 | A layout that radiates by element type, and movement between two layouts shown as motion | canvas library `layout/`; today manual, tree and row-packed, and no animation | 6.5, 6.7 to 6.10 |
| B6 | A switch on the canvas whose value comes from the document and is written back | canvas library chrome; today the one toggle is drawn only inside the filter block | 4.6 to 4.8 |
| B7 | `compileNotation` and the DISL runtime reading what the languages gain for B1 to B6 | `canvas/library/disl`, `EtAlii.Adp.Specification.Disl` | 10.2, 10.3 |
| B8 | A write that does not overwrite a change it has not seen | `WritableDocumentLifecycle`; today a command edits its cached copy and writes it with no comparison to the disk | 8.6 |
| B9 | Undo that does not put back a file older than an outside change | `EtAlii.Adp.History`; today undo is `RestoreDocumentCommand`, which rewrites the whole earlier text, and nothing reacts to a reload | 8.6, 9.6 |

B8 and B9 are defects in waiting for every diagram; this one makes them daily. They are core fixes with their own guards, not module work.

### Integration Points

- **Catalog and Add**: the module's `Diagram` class declares origin `etalii/agent-activity-diagram` and extension `.aad`; `DiagramDocumentFactories.Verify` requires its factory.
- **Errors and Warnings panel**: findings from the FBL reading and from `ConstraintEvaluator` through the module's `IDiagramValidator`.
- **Property grid and context menus**: derived from the specification, for elements and for rows.
- **`.gitattributes`**: one line, `*.aad -text`, beside the other byte-compared extensions.

## Architecture

### The activity file

One YAML file. Elements are entries in five lists. **A relation is a key on the element at its "one" end**, not an entry of its own (decision L8): a specification names its project, an agent its specification, a location its agent and its environment. The file then cannot say that an agent has two specifications or a location three relations, and an agent changes what it works on by changing one line.

```yaml
agent-activity-diagram: 1
projects:
  - id: p-standalone
    name: etalii.adp.ide.standalone
    link: https://github.com/etalii-adp/etalii.adp.ide.standalone
specifications:
  - id: s-knowledge
    project: p-standalone
    name: Knowledge designer
    status: progressing
    link: .spec-workflow/specs/knowledge-designer/requirements.md
    tasks:
      - id: t-k2
        title: Trial the three bindings
        status: progressing
        updated: 2026-10-08T23:44:00+02:00
      - id: t-k6
        title: Table component
        status: input-required
        updated: 2026-10-08T21:10:00+02:00
        link: https://github.com/etalii-adp/etalii.adp.ide.standalone/pull/150
agents:
  - id: a-dev2
    name: Developer 2
    specification: s-knowledge
locations:
  - id: l-kd
    agent: a-dev2
    environment: e-fractal
    branch: features/knowledge-designer
    branchLink: https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/features/knowledge-designer
    folder: .claude/worktrees/kd
    pullRequests:
      - id: pr-150
        title: "150: Knowledge designer, tasks 2 and 6"
        updated: 2026-10-08T23:50:00+02:00
        link: https://github.com/etalii-adp/etalii.adp.ide.standalone/pull/150
environments:
  - id: e-fractal
    name: Fractal
    kind: local-machine
view:
  showArchived: false
  placements:
    - element: s-knowledge
      x: 240
      y: -80
  groups:
    - element: s-knowledge
      group: pending
      collapsed: false
```

- **Statuses** are written `pending`, `progressing`, `input-required`, `finished`, `archived`; **kinds** `local-machine`, `cloud`, `container`, `wsl`, `other`.
- **`updated`** on a task and a pull request is when it was last changed, as an ISO 8601 moment with its offset. Rows are drawn most recent first within their group; a row without it comes after those that have one, in file order (decision L9).
- **`folder` left out means `Default`.** An empty optional key is removed, not written empty.
- **Ids.** ADP writes a ShortGuid. An agent may write any id matching DISL's default pattern, and readable ones such as those above are encouraged in the published description, since an agent finds its own entries again by them.
- **A link** is an `http` or `https` address, or a path. A relative path is relative to the activity file; an absolute one is taken as written.
- **`view` is the user's part.** `placements` holds locked positions only, so an element without an entry is placed by the layout. `groups` holds only groups whose state differs from their default (decision L2). Agents are told to leave `view` alone; an entry there for an element that no longer exists is removed on ADP's next write (Requirement 8.8).
- **A new file** is the header, five empty lists and no `view` key, written with CRLF.
- **Unknown keys** are kept and reported; a header other than `1` opens read-only.

### Language decisions

Requirement 1.4 asks that each gap be put to the user before a language changes. The option marked **(default)** is what the tasks will be written against. Approving this document rules each at its default unless a comment on the card says otherwise.

| # | Decision | Options |
| --- | --- | --- |
| L1 | Group a list inside an element how? (D1) | **DISL: `groupBy` on a compartment (default):** names an attribute of the items; one heading per value in the enum's declared order, with a count, a collapse default per value, and empty groups left out. Items stay editable because the list is still the plain list. · **Four compartments, each filtered:** no new construct, and DISL would instead have to say that items can be edited through a filter. Four copies of one declaration. · Other. |
| L2 | Say how a declared collapse default and a stored state meet how? (D2) | **DISL: the declared value is the default, a stored value wins, and only a state that differs from its default is stored (default):** the file stays small, and a group nobody touched follows the definition if its default ever changes. · **Store every state once it has been set:** exactly as Requirement 4.4 is worded; the file grows by one entry per group ever clicked. · Other. |
| L3 | Keep a canvas switch's value how? (D3) | **DISL: `persist: true` on a canvas filter (default):** the value becomes view data of the diagram and is stored with the rest. · **A diagram attribute, a viewpoint, and a switch in the diagram's form:** no language change, and the switch is then in the property grid, not on the canvas. · Other. |
| L4 | Declare lock and unlock how? (D4) | **DISL: `pin`, `unpin` and `unpinAll` as standard context entries, `pinned` settable by the `view` action, and "a drag pins" stated for a layout with `respect: "pinned"` (default).** · **Leave it to each runtime:** as today; a second host would learn the gestures from the `.md`. · Other. |
| L5 | Declare the layout how? (D5) | **DISL: `tiers` on the `force` algorithm, with what the result must satisfy; the reference algorithm in the `.md` (default):** `tiers` lists element types from the centre outwards. DISL states the properties (tier order from the centre, no overlap, pinned elements fixed, the same input giving the same result); `agent-activity-diagram.md` gives the algorithm and its numbers so a second host can reproduce the picture closely. · **A fully defined algorithm in DISL**, as `tidyTree` is: every host draws the identical picture, at the cost of specifying a simulation to the last constant in the language. · **`plugin:` and prose only.** · Other. |
| L6 | Declare a link that opens how? (D6) | **DISL: an `open` action taking a link, `itemLink` on a compartment, and the `uri` type accepting a relative path (default):** `open` is defined as: an `http` or `https` address goes to the browser, a path to the host's reveal, anything else is refused. · **A plugin action per host:** as `net.etalii.adp.ide.revealPath` is today; nothing on rows. · Other. |
| L7 | Store view state in the file how? (D7) | **DISL: `persistence.view.bind` maps view data onto entries of the body; FBL unchanged (default):** a locked position is an entry under `view.placements`, a collapse state one under `view.groups`, the switch a key under `view`. FBL binds them as it binds any entry, and FBL 7.3 already covers outside changes. · **FBL: view rules of its own**, beside element rules: cleaner in FBL, and the larger change. · **Plain model attributes**, with no notion that they are view data: no language change; the layout's `respect: "pinned"` then has nothing to read. · Other. |
| L8 | Write a relation in the file how? | **As a key on the element at the "one" end (default):** shown above. Needs DISL to draw a relation derived from a reference attribute and to map connect and disconnect onto setting and clearing it; DISL 0.3 has derived relations (Agent Behavior Modelling uses one), and whether a reference can be their source is what task 1 proves. · **As entries of a `relations:` list with `from` and `to`:** what the functional decomposition graph does and FBL supports today; relations then have ids, and "at most one" is a rule to check, not a shape. The fallback if the trial fails. · Other. |
| L9 | Reorder tasks and pull requests? | **Ruled by the user in chat, 2026-10-09, after this document was approved: they are ordered by when they were last updated, most recent first, and not by hand.** Each row carries `updated`, an ISO 8601 moment with its offset, written by whoever adds or changes the row; ADP writes it on its own edits. The order is computed when drawing, so the file's own order does not matter and nothing has to move in it. The approved default was: **no, they are listed in file order:** this host's FBL runtime refuses `Move` and an indexed `Add` for a declared binding ("only a persistence plugin can"), and the request does not ask for an order. · **Yes:** the FBL runtime gains `Move` for declared YAML bindings first. · Other. |

With the defaults, **FBL does not change**; DISL gains six constructs (L1, L3 to L7) and one clarification (L2), which is DISL 0.4.

### The DISL specification

`agent-activity-diagram.dis`, in outline:

- **Metamodel.** Node types `Project`, `Specification`, `Agent`, `Location`, `Environment`; child types `Task` (in `Specification`) and `PullRequest` (in `Location`), which gives rows ids and makes them editable; enums `SpecificationStatus`, `TaskStatus`, `EnvironmentKind`; reference attributes `Specification.project`, `Agent.specification`, `Location.agent`, `Location.environment`; four derived relations, one per reference; diagram attribute for the switch. `folder` has the default `Default`; `branch` and every name are required.
- **Notation.** One shape and one theme colour pair per type. A specification: name, a status line in words with the status colour, and a `tasks` compartment with `groupBy: status`. A location: branch label, separator, folder label, and a `pullRequests` compartment collapsed by default. Badges: a link symbol where a link is set, a lock symbol where `self.view.pinned`. Rows: `itemText`, `itemLink`. Archived specifications are excluded by the default viewpoint unless the switch is on.
- **Toolbox and menus.** Five tools. Menus: rename, delete, lock or unlock, and for a specification and a location *Add task* and *Add pull request*; for a row, rename, set status, delete. The canvas menu: *Unlock all positions*.
- **Constraints.** `std.endpoints` and `std.references` as built in. Declared invariants for the findings of Requirements 3.3, 3.7 and 5.6. The refusal of a second specification (Q6) is a declared gesture constraint on connect, since the runtime's `GestureConstraintEvaluator` evaluates declared constraints only and has no multiplicity built-in.
- **Layout.** `force` with `tiers`, `respect: "pinned"`, `trigger: "onChange"`.
- **Persistence.** `format: "fbl"`, the binding `agent-activity-diagram.fbl#aad`, and `view.bind` (L7).

`agent-activity-diagram.md` holds, under the headings its siblings use: what the diagram is for; the file format in full with one complete example, enough to write a file by hand; the reference layout algorithm; how each host opens a link; the instruction text for agents; the examples; and whatever the trial shows DISL 0.4 still cannot state. A JSON Schema for the file, `agent-activity-diagram.schema.json`, is published beside it.

### The layout

Computed in the client, because only the client knows each element's size once groups are expanded. It is a `LayoutAlgorithm` named for what it does, with a new `LayoutMode`.

- **Forces.** A spring along every relation; repulsion between all elements; a pull of each element towards the circle of its tier, whose radius grows with the tier's index and with how much the tier holds; and a final pass that separates any two rectangles still overlapping, which is what guarantees Requirement 6.9.
- **Locked elements** take part with their stored position and never move.
- **The same file gives the same picture** (Requirement 6.7): starting positions are computed from the tier and the order of entries in the file, the number of steps is fixed, and nothing is random. This is written by hand, about two hundred lines, rather than taken from a library: it must be reproducible in the hosts that do not run JavaScript, and a dependency's internals cannot be the definition.
- **A change moves little** (Requirement 6.8): when the document changes while open, the layout starts from the positions on screen instead of from the computed start. **A consequence, stated so it is not found later as a defect:** the picture after a run of live changes can differ from the picture the same file gives when reopened. What the user wants kept, they lock.
- **Motion.** The library moves elements from their old to their new positions over a short, fixed time, and not at all when the user has asked the system for reduced motion.
- **Cost.** The pass runs off the main interaction path and the canvas shows the last positions until it finishes; a task measures it at two hundred elements before the number of steps is fixed.

### The canvas library

Each is declared by a module and compiled from DISL by `compileNotation`:

- **Compartments** (B1): on an element type, a list bound to child elements, with optional grouping, headings with counts, a collapse state per heading that comes from the stream, row text, and a row symbol. Rows are hit-testable and take part in the library's selection under their own ids, so the property grid and the context menu work for a row as for an element. Rows are one line each with an ellipsis, so the element's height is the sum of declared line heights and needs no text measurement; the library computes it and relations follow.
- **Activatable symbols** (B2): a declaration separate from `DecorationDeclaration`, with a tooltip, a focus stop and a press, raising a library event. Used for the link and, without a press, for the lock.
- **Pinned elements** (B4): `pinned` on an element in the stream; a layout skips them; `dragUnderAutomaticLayout` gains a value under which a drag raises `element-moved` for that element alone and leaves the mode automatic.
- **A canvas switch** (B6): declared in chrome with a caption, its value from the stream, raising an event when changed; independent of the filter block.
- **Motion** (B5), as above.

The shell handles the link event (B3): `http` and `https` through `window.open` with `noopener` and `noreferrer`; a path inside the project through `revealPath`; any other path in a small dialog that shows it, says why it was not opened, and copies it.

### The module

Backend, after the hype cycle graph's classes with an `Aad` prefix: `Diagram`, `AadBody`, `AadDefinition`, `AadParser` and `AadModel`, `AadDocumentStore`, `AadDocumentReloader`, `AadDocumentFactory`, `AadSession` and its factory, `AadElementMapper`, `AadValidator`, the four context providers, and commands for add, set, remove, connect, disconnect, lock, unlock, unlock all, set group state and set switch, each through `AadDefinition.Apply`. There is no hand-written oracle to hold parity against, as the migrated modules have; the specification is the only statement of the rules, and the module's tests assert behaviour against fixtures.

Client: `register.ts`, `AadCanvas.tsx`, `aadBindings.ts`, ids, model and a stylesheet; `assertValidDiagramDefinition(compileNotation(SPEC, BINDINGS))`. No drawing of its own.

`api/agent-activity-diagram.proto` carries an element's payload: its fields, its rows and its links. `pinned`, group states and the switch travel in the core contract, since any module may use them.

### Writing beside another writer

- **B8.** Before a write, `WritableDocumentLifecycle` compares the file on disk with the text its edit was planned against. If they differ it reads the file again, plans the same model change against the fresh reading, and writes that; if the change no longer applies (its element is gone), the edit is refused and the user is told. An edit is a change to one entry by id, so replanning it is what FBL's planner already does.
- **B9.** When an outside change is read, the undo history of that document is cleared, as FBL 7.3 prescribes ("clears the body's history"). Undo then never restores a text older than an agent's write. It costs the user their undo steps whenever an agent writes; the alternative, undo as an inverse model change, is the better end state and a larger change to `EtAlii.Adp.History`, noted under *Risks*.
- A file that stops being readable keeps the last good picture on screen and blocks writes until it reads again (Requirement 8.5), which `WritableDocumentLifecycle.Reload` already does for the picture.

## Components and Interfaces

### In etalii.adp

- **DISL 0.4**: specification text, schema and one worked example per construct of L1 to L7.
- **`definitions/diagrams/agent-activity-diagram.dis`, `.md`, `.schema.json`**, and the binding `agent-activity-diagram.fbl` with fixtures proving reading and each kind of edit.
- **The Notion "Tools" page**, kept true (Requirement 1.8). It exists since 2026-10-09.

### In this host's core

- `EtAlii.Adp.Specification.Disl`: reads the DISL 0.4 constructs; derives menu entries for pin and for rows.
- `EtAlii.Adp.Diagram` and `EtAlii.Adp.History`: B8, B9.
- `src/api`: `pinned`, group state and switch value in the element and delta contracts.

### In this host's client

- `canvas/library/definition`, `layout`, `surface` and `DiagramCanvas.tsx`: B1, B2, B4, B5, B6.
- `canvas/library/disl`: compiles them.
- Shell: B3.

### The module

As under *The module*, with `examples/` holding three sets (Requirement 10.5): `adp-one-day` (this project's own work on one day), `every-state` (each status, kind and link form, locked elements, a group off its default), and `two-projects` (two projects sharing an environment). Beside them, `updating-this-file.md`: the instruction text for agents (Requirement 8.3).

## Data Models

The model the binding produces, by type:

| Type | Attributes | Parent or reference |
| --- | --- | --- |
| `Project` | `name`, `link` | |
| `Specification` | `name`, `status`, `link` | references `project` |
| `Task` | `title`, `status`, `updated`, `link` | child of `Specification`, slot `tasks` |
| `Agent` | `name`, `link` | references `specification` |
| `Location` | `branch`, `branchLink`, `folder`, `folderLink` | references `agent`, `environment` |
| `PullRequest` | `title`, `updated`, `link` | child of `Location`, slot `pullRequests` |
| `Environment` | `name`, `kind`, `link` | |
| view: placement | `x`, `y` | references any element |
| view: group state | `group`, `collapsed` | references a `Specification` or `Location` |
| view: diagram | `showArchived` | |

## Error Handling

### Error Scenarios

| # | Scenario | Handling | What the user sees |
| --- | --- | --- | --- |
| 1 | The file is not YAML, or has no header | Not opened for writing; last good picture kept | The parser's error and position; no edit accepted |
| 2 | A reference names an element that is not there | Kept; `std.references` | An error finding at its line; no relation drawn |
| 3 | A reference names an element of the wrong type | Kept; `std.endpoints` | An error finding; the relation drawn marked as a breach |
| 4 | An id used twice | Both kept; `std.duplicateId` | An error finding on each |
| 5 | A status or kind that is not one of the values | Kept as written | A warning finding; the element drawn with a neutral look, a task under a group of its own at the end |
| 6 | A link that is neither a web address nor a path | Kept, never opened | A warning finding; the symbol drawn struck through |
| 7 | An entry with no id | Shown under a place id | An info finding; an id is written on the first edit in ADP |
| 8 | Unknown keys | Kept on every write | An info finding |
| 9 | The file changed on disk between planning and writing an edit | B8: replanned against the new text, or refused | Nothing, or "the entry this edit was for is no longer in the file" |
| 10 | An agent wrote while the user had undo steps | B9: history cleared | Undo is unavailable until the next edit |
| 11 | A second specification for an agent, by gesture | Refused by the declared constraint | The reason where the gesture ended |
| 12 | A placement or group entry for a missing element | Ignored; removed on the next write | Nothing |

With L8's default, "an agent with two specifications" and "a location with a third relation" cannot be written, so the breach findings Requirement 3.8 names for criteria 2 and 4 reduce to a duplicate key, which the reading reports as an error, using the first value.

## Testing Strategy

### Unit Testing

- **Binding fixtures in etalii.adp**: reading, and one fixture per kind of edit, including an edit that leaves comments and key order untouched.
- **Backend**: one fixture per row of *Error Scenarios*; every example opens without an error finding and writes back byte-identical; B8 with a file changed between plan and write; B9 with a reload between two edits. Each guard is seen to fail against a planted defect that its task names.
- **Client**: the layout as a pure function, asserting on the shipped examples and on a generated diagram of two hundred elements: tier order from the centre, no two rectangles overlapping, locked elements unmoved, and two runs equal. Compartments: height from rows, a collapsed group's rows absent, a row selected by its id.
- **`compileNotation`**: the bundled specification compiles and the result passes `assertValidDiagramDefinition`.

### Integration Testing

- Open, edit, write from outside, and observe the deltas on the `Watch` stream, with a locked element's position unchanged.
- `ExampleRegistrationTests` opens every example registration against the deployed catalog.
- A fresh worktree is built before any green gate is trusted, since the module embeds a bundled definition.

### End-to-End Testing

The `tests.md` entry of Requirement 11.1, in a real browser and both themes. jsdom is not evidence for layout, collapse or link activation.

## Documentation

`docs/tools.md` gains the row and moves it with the state. `docs/diagram-module-client-api.md` gains B1, B2, B4, B5 and B6. `docs/architecture.md` and `docs/solution-structure.md` change where a sentence becomes false: the document lifecycle's behaviour on a write (B8) and on a reload (B9), and the project counts. `docs/creating-a-diagram-module.md` gains what a module with rows and view state in its body declares.

## Amendments asked for

Each is a small amendment under `CLAUDE.md`: asked in chat as a selection, and applied to the requirements by their owner only after the answer.

| # | Criterion | Change | From |
| --- | --- | --- | --- |
| A1 | 2.6 | A relation has no id of its own; it is a key on an element. | L8 |
| A2 | 4.4 | A group's state is stored while it differs from its default, which shows the same thing on reopening. | L2 |
| A3 | 9.3 | Rows are not reordered; they are listed in file order. | L9 |
| A4 | 9.6 | Undo and redo work as for other diagrams, and the history is cleared when another program changes the file. | B9 |

**Ruled by the user in chat, 2026-10-09:** A1, A2 and A4 as asked. A3 otherwise: rows are not reordered by hand, and are ordered by when they were last updated (decision L9). The requirements carry all four.

## Risks

- **The languages have not been tried.** Everything about DISL and FBL here is from their text. Task 1 writes the binding, a draft specification and fixtures before DISL 0.4 is proposed; L8 in particular falls back to a `relations:` list if a reference cannot source a drawn relation.
- **The core work is larger than the module.** B1 to B6 are five new library capabilities and B7 teaches two runtimes to read them. The tasks land each capability with its own guard and a shipped example before the module depends on it.
- **How view entries are identified in FBL.** A placement has no id key of its own; whether its `element` key can serve, or a computed id is needed, is settled by the trial.
- **B9 costs undo steps** when agents write often. If that is felt, undo as an inverse model change is the remedy, as its own specification.
- **Splices from two writers.** An agent that rewrites the whole file drops the user's `view` part unless told not to; the instruction text says so, and the diagram survives it (elements return to the layout), but locked positions are lost.
- **Not adopted from the research note**: claims on tasks, instruction files, a branch's state against its base, and leftover locations. Each needs facts agents do not write today.
