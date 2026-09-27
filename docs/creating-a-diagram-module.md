# Creating a diagram module

How to give ADP a new diagram type. This is a walkthrough of a path that already exists — the repository ships dozens of diagram modules under [`src/diagrams/`](../src/diagrams/) — traced against one real, complete module: **timeline** (`src/diagrams/timeline/`, the `generic/timeline` type). Every section names timeline's actual files, so every abstract statement has a concrete neighbour you can open.

Two companion documents: [creating an editor module](creating-an-editor-module.md) covers the second plugin family, which shares most of this mechanism; [the diagram catalog](diagrams.md) tracks every type ADP could support and where each one stands.

The rules this document leans on live where they already live — [CLAUDE.md](../CLAUDE.md) for the repository's working rules, [`tech.md`](../.spec-workflow/steering/tech.md) and [`structure.md`](../.spec-workflow/steering/structure.md) for the architecture — and are linked rather than restated, because two copies of one rule drift.

## What a module is

A diagram type is a folder under `src/diagrams/<diagram>/` with up to four subfolders — `backend/`, `api/`, `client/`, `examples/` — as [`src/diagrams/readme.md`](../src/diagrams/readme.md) and [`structure.md`](../.spec-workflow/steering/structure.md) define. Timeline has all four:

- `backend/` — `EtAlii.Adp.Diagram.Timeline` and `EtAlii.Adp.Diagram.Timeline.Tests`, both listed in `src/backend/EtAlii.Adp.slnx`.
- `api/` — `timeline.proto`, the type's own wire payloads.
- `client/` — the canvas, its registration, and a small `package.json`.
- `examples/` — documents a reader can open and click around in.

The dependency direction is absolute: a module depends on core abstractions (`EtAlii.Adp.Backend`, `EtAlii.Adp.Diagram`, the shared contracts), and **nothing in core depends on any module**. Core compiles and runs with your module's assembly absent. Everything below is a consequence of that rule.

## Specify first

A diagram type gets a spec before it gets code. What that spec must decide — the file format and its round-trip guarantee, who owns positions, what the toolbox contributes, which context actions exist with which inverses — is defined once in tech.md's [Specifying a diagram type](../.spec-workflow/steering/tech.md#specifying-a-diagram-type) section. This document owns the implementation path only; read that section before starting, and settle its questions in a `.spec-workflow/` spec of your own.

## The definition

The module's identity is one static class, `Diagram`, in the backend project — timeline's is [`Diagram.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/Diagram.cs). It carries:

- **The origin** — `new DiagramOrigin("generic", "timeline")`. This `vendor/type` pair is the type's MIME identity everywhere: the first line of an `.adp` registration file, the catalog's Origin tag, the key core resolves every seam by, and the mime the client canvas matches on. Choose it per CLAUDE.md's catalog convention and never change it.
- **The document extension** — `DocumentExtension = ".tml"`. A diagram's body lives in a sibling file named by this extension, beside the `.adp` that registers it. A type that owns its extension outright (no other tool writes `.tml`) also routes a *bare* body file to itself, with no `.adp` needed. A format with a second serialization may declare it as `AlternateExtension` (the RDF family's `.nt` beside `.ttl`): the alternate routes and is claimed like the primary, but derives no registration sibling — a registered body carrying it names itself with an explicit `body:` header, which Add writes anyway.
- **The named definition** — a `public static DiagramDefinition Timeline { get; }` property carrying title, description, icon, extension and the `Build` delegate. Named, so the module's own registrations and tests write `Diagram.Timeline.Origin` rather than indexing into an array — tech.md's [Declaring a diagram definition](../.spec-workflow/steering/tech.md#declaring-a-diagram-definition) defines the two permitted shapes (stub and implemented) and when a module moves between them.
- **`Definitions`** — the array startup discovery reads: `[Timeline]`.

**Why adding a module edits no core file:** at startup, `DiagramDefinitionDiscovery` ([`src/backend/EtAlii.Adp.Diagram/DiagramDefinitionDiscovery.cs`](../src/backend/EtAlii.Adp.Diagram/DiagramDefinitionDiscovery.cs)) scans the application's assemblies for `Diagram.Definitions` arrays, and `Program.cs` hands every found definition to `AddDiagramDefinitions`, which invokes each definition's `Build` delegate. Timeline's delegate is one line — `builder => builder.Services.AddTimeline()` — and that is the entire hookup. The host names no module; deleting the folder removes the type.

## The registration surface

`Build` calls one extension method that registers everything the module contributes — timeline's is [`ServiceCollection.AddTimeline.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/ServiceCollection.AddTimeline.cs), and it is deliberately the module's whole integration surface in one screen. The seams, each one line:

| Seam | Timeline's implementation | Needed when |
|---|---|---|
| `IDiagramDocumentFactory` | `TimelineDocumentFactory` | **Mandatory once you declare an extension** — startup fails, naming your type, if the factory that writes an empty body is missing. |
| `IDiagramSessionFactory` | `TimelineSessionFactory` | Mandatory to open the diagram at all. |
| `ICommandHandler<T>` per command | the `Commands/` folder | For every edit the type supports. |
| `IDiagramDocumentReloader` | `TimelineDocumentReloader` | So an external write to the body (another editor, a `git pull`) reaches every open session. |
| `IContextSourceResolver` | `TimelineContextSourceResolver` | To make elements selectable. |
| `IContextActionProvider` | `TimelineContextActionProvider` | To offer verbs — reaching the ribbon, right-click menu and keyboard through one path. |
| `IContextPropertyProvider` | `TimelineContextPropertyProvider` | To fill the property grid. |
| `IDiagramToolboxProvider` | `TimelineToolboxProvider` | To offer palette entries. A type that registers none simply has an empty toolbox. |
| `IDiagramValidator` | `TimelineValidator` | To report problems to the Errors and Warnings panel. A type with no rules registers nothing. |

Everything below `IDiagramSessionFactory` is optional in the mechanical sense — the app runs without it — but a type without selection, actions and properties is a picture, not an editor. Register the document store with `TryAddSingleton` (one store shared by every connection; a test that registers its own keeps it). Splitting a second `AddTimelineCommands` method out is done only when a concrete consumer needs the handlers alone — tech.md's [Registering a diagram module's services](../.spec-workflow/steering/tech.md#registering-a-diagram-modules-services) has the rule.

## The document path

Build bottom-up, per tech.md's [Implementation order](../.spec-workflow/steering/tech.md#implementation-order): the document first, because everything above it is written against something that already exists and already passes its tests.

The shape every shipped module converges on, in timeline's files:

- **A document that preserves what it read** — [`LineDocument.cs`](../src/backend/EtAlii.Adp.Documents/LineDocument.cs) holds the file's lines with their own line terminators, so what came in goes out. **You do not write this one**: it is core's, and every line-splicing module shares it. Timeline and dependency-graph each held their own copy until the two turned out to be identical in every line of code, differing only in the type's name. Databricks, rdf and azure-pipeline followed later, and azure-pipeline's copy had drifted twice - a file with as many LF endings as CRLF gave a newly inserted line LF rather than CRLF, and a range ending the line before it started was spliced in as an insertion rather than refused - which is the cost of a copy: a fix to one reaches none of the others.
- **A parser that never throws** — [`TimelineParser.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineParser.cs) turns the document into a model (`_Model/TimelineModel.cs`), reading what it understands and passing over what it does not. A construct the module does not model survives by never being touched; a semantically broken document still opens, and the validator reports it rather than the parser refusing it.
- **A writer that splices** — [`TimelineWriter.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineWriter.cs) edits the lines a change touches and re-serialises nothing. **This one you do write**, but only the half that is yours. *How* a line is found and replaced — locating a key inside a range, matching the indentation already in the file, deciding where a new entry goes, quoting a value only when YAML needs it — is core's [`LineSplice.cs`](../src/backend/EtAlii.Adp.Documents/LineSplice.cs). *What* gets written — which keys an element has, in what order, with what values — is yours, because that is your notation rather than your file handling. Timeline's writer keeps `SetLabel`, `SetRow`, `InsertElement` and `RemoveElement` for exactly that reason: a timeline element carries `begin`, `end` and `row`, where a dependency-graph element carries `x` and `row`, so those four share a name between the two modules and nothing else.
- **One store owning documents** — [`TimelineDocumentStore.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineDocumentStore.cs) behind `ITimelineDocumentStore`: loads a document once, shares the instance across every connection, saves atomically, and raises a changed event sessions subscribe to.

Around these four, a module grows whatever domain helpers its notation needs, in the same project and namespace — timeline has a `TimelineScale` for its time axis, `TimelineRows` for row packing, `TimelineInstants` for date arithmetic — and the client canvas does the same (timeline's ruler and tick calculation are its own files beside the canvas; its scrollbars are the shared ones, placed by a class). Those are ordinary internals, not seams: nothing outside the module knows them, and this walkthrough names only what the outside world touches.

All of it serves one promise: **a document ADP did not change comes back byte-identical** — comments, blank lines, odd indentation, line endings, a missing final newline. The round-trip tests compare bytes, which is also why the fixtures need a `.gitattributes` line (below). The model layer is deliberately usable without gRPC, the filesystem or a browser: text in, model out.

## The session, and the layout fork

A session is one open diagram on one connection: [`TimelineSession.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineSession.cs), opened by [`TimelineSessionFactory.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineSessionFactory.cs), which core resolves by origin. It answers a baseline (the whole diagram as element adds), reacts to the store's changed event by sending deltas (the difference against what this connection last saw — computed by core's shared [`DiagramDiff.cs`](../src/backend/EtAlii.Adp.Diagram/DiagramDiff.cs) over what [`TimelineElementMapper.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineElementMapper.cs) renders), and handles moves.

Before writing it, decide the single biggest fork in any diagram type's design, and write the decision down: **who owns position?**

- **Authored in the document** — a position is the author's claim, and dragging is a *document edit* through a command. Timeline (x is authored time, y an authored row) and wardley-map (both coordinates are the statement) are the shipped examples.
- **Computed by the module** — layout is derived and never written back; dragging re-parents or is refused. Mindmap is the shipped example.
- **Computed with authored overrides in a sidecar** — c4 stores dragged positions in a `.layout.json` beside the body, never inside a file another tool owns.

Core is indifferent to the choice; the two move gestures (positional move, re-parenting move) are both on the session seam, and a session refuses the one that means nothing for its type.

## Commands

Every edit is an `ICommand` with a matching handler, dispatched through the history stack — never an inline mutation. Timeline's [`Commands/`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/Commands/) folder is the worked example: add, remove, rename, connect, disconnect, relabel, placement and end-date commands, each handler validating its own preconditions against *current* state (undo and redo dispatch it again later) and reporting the command that reverses it. A rejected command returns a `CommandResult` with a sentence for the user, never an exception. The full rules are tech.md's [Commands](../.spec-workflow/steering/tech.md#commands) section; the payoff is that undo, redo, validation and multi-client delivery come from core, not from your module.

## The wire

The core contract carries every diagram the same way: elements with an id, a position, a mime-style element type, and a `google.protobuf.Any` payload core never interprets. Your module defines those payloads in its own `api/` folder — timeline's [`timeline.proto`](../src/diagrams/timeline/api/timeline.proto) — and packs them in its element mapper. Core's `.proto` files in [`src/api/`](../src/api/) are not edited.

Each element type is a public constant on the module's mapper, shaped `vendor/type+kind` — timeline's `PeriodType` in [`TimelineElementMapper.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineElementMapper.cs). Name every one in [`ElementTypeCatalog.cs`](../src/backend/EtAlii.Adp.Backend.Tests/Unit%20Tests/ElementTypeCatalog.cs), under the module's folder name and as an element or a relation: `ElementTypesTests` fails on a constant of that shape the catalog does not name, and writes [`src/fixtures/cross-tier/element-types.json`](../src/fixtures/cross-tier/element-types.json) from the catalog, which the client suite reads against the type ids your client declares. Commit the file it rewrites.

A module with its own proto joins two generation paths:

- Backend: the `.csproj` includes the proto through `Grpc.Tools`, compiled at build time.
- Client: the `generate` script in [`src/client/package.json`](../src/client/package.json) gains one `buf generate ../diagrams/<diagram>/api` step, which every client build and test run executes first.

## The client

The client half mirrors the backend's discovery, so the shell holds no list of diagram types either.
**What a module client may reach for, entry by entry, is [docs/diagram-module-client-api.md](diagram-module-client-api.md)** —
registration and discovery, the stream and the model, every member of the definition, the canvas and its
props, events, selection as the library owns it, the context channel, the toolbox, the view report,
geometry, styling, the guards a module is walked by, and the library-internal names that are not for
modules at all. It carries a nine-step walkthrough from an empty `client/` folder to a mounted canvas.

Three things belong here rather than there, because they are steps in THIS walkthrough:

- **`client/register.ts`** — timeline's ([`register.ts`](../src/diagrams/timeline/client/register.ts)) exports `registrations`: a predicate on the mime type (match exactly, not by prefix) and the canvas component to mount. The shell discovers every module's file through `import.meta.glob` in [`diagramCanvases.ts`](../src/client/src/shell/panels/diagramCanvases.ts); adding the file is the whole hookup.
- **`client/package.json`** — a small workspace package ([timeline's](../src/diagrams/timeline/client/package.json)) declaring what the module's client code imports. It is picked up by the `diagrams/*/client` workspaces glob in [`src/package.json`](../src/package.json); run `npm install` from `src/` after adding it so the lock file learns the new package — `npm ci`, which CI runs first, refuses a lock that missed it.
- **The context channel resolves and never rejects**: a value-returning method answers with the failure in its value, a void one is advisory and says so. So a call site needs no catch, and reaching for one is a sign the method's shape is wrong rather than your handling. [`channelResolvesRatherThanRejects.test.tsx`](../src/client/src/shell/context/channelResolvesRatherThanRejects.test.tsx) drives every value-returning method over a rejecting transport and fails naming any it cannot classify. The entry is [the context channel](diagram-module-client-api.md#the-context-channel).
- **A module writes a *definition*, not a canvas.** Its `MyCanvas.tsx` keeps a stream hook, maps the stream's model into elements and connections, and hands a `DiagramDefinition`, a `DiagramModel`, its `DiagramEventHandlers` and `source` to the shared [`DiagramCanvas`](../src/client/src/canvas/library/DiagramCanvas.tsx). Pan, zoom, drags, connects, drops, selection, the context menu, scrollbars and the inline label editor are the library's, and each has a guard that fails a module doing it privately.
- **Phases, time axes and tags are declarations too.** A type drawn as a band divided into phases declares `segments` on the `arrow-banner` shape, with draggable boundaries and a tooltip per phase; a connection that may attach anywhere along an edge declares `along` anchors; one connection per direction is `perPair` on the relation's cardinality; a month axis is a bottom ruler with a `MonthScale`; and a tag filter box is the definition's `filter`. Each is described in [the client API](diagram-module-client-api.md#element-types-and-shapes), and the [Gartner hype cycle graph](../src/diagrams/gartner-hypecycle-graph/client/GhgCanvas.tsx) declares all of them.

## Renaming in place

**Optional, per module, and two lines of work.** A rename opens a modal dialog unless the module
says the value it is asking for *is* the text the reader can see. The client cannot work that out
for itself: fifty-odd actions answer with the same input prompt, and the same providers ask
through it for run-if values, cluster keys, prefix declarations and predicate IRIs. Reading an
action id for the word "rename" would put your module's vocabulary into shared code, which the
family rulings forbid.

So the backend says it. Pass the element's id as the `InlineLabelElementId` argument of
[`ContextInputRequest`](../src/backend/EtAlii.Adp.Context/_Model/ContextDialogRequest.cs)
on exactly the prompts whose value is the label, and leave every other prompt alone — the
argument is defaulted, so nothing you already wrote changes.
[`MindmapContextActionProvider`](../src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapContextActionProvider.cs)
is the clearest example: four of its actions ask for text, and the notes prompt is the one left
unmarked, because notes are not the label. Rename marks the node it renames; add-child and
add-sibling create the node first, named from its siblings, and then mark the new node's id, so
the new name is edited in place.

The client half is definition data and nothing else. Mark the label editable in the definition —
`label: { placement: "inside", editable: true }` on the element type, an `inset` rule for one
named line of a composite card, `{ placement: "midpoint", editable: true }` on a relation — and the
library does the rest: given the canvas's `source`, it reads the backend's inline-edit prompt itself
(there is no `editing` prop to pass since client-centralization task 7). The library places the shared
[`InlineLabelEditor`](../src/client/src/canvas/label/InlineLabelEditor.tsx) over the drawn label
from the rule alone, commits it as a `label-commit-requested` event through the prompt flow, and
ends an open edit before any gesture begins. A guard,
[`noPrivateLabelEditors.test.ts`](../src/client/src/canvas/label/noPrivateLabelEditors.test.ts),
fails naming any module that renders a text field of its own instead; the placement helpers in
[`labelPlacement.ts`](../src/client/src/canvas/label/labelPlacement.ts) remain the shared
geometry underneath and are not called by modules any more.

Three things are worth knowing before you mark a rule editable.

**Give the label's rectangle, not the element's.** A C4 box carries a name, a type line and a
description; an editor over the whole box sits on three lines of text to edit one of them.

**A derived label is not editable and should not be marked.** An RDF edge shows a predicate's
prefixed name computed from an IRI, and changing it renames that term across the whole document —
a different act, with collision and prefix rules whose refusals need more room than a textbox has.
The test to write is the contrast: assert that your marked prompts carry the id *and that your
unmarked ones do not*, in one test, because a marker on the right action proves nothing if a
neighbouring one quietly acquired one too.

**A decorated label still has one authored value underneath.** C4 draws a relationship as
`description [technology]`, sometimes numbered; the editor replaces that whole string on screen
while editing the description alone. Decoration around a single authored value is chrome. A label
with no single value beneath it — type badges, a computed summary — is not markable at all.

## The view-delta loop

**Every diagram module implements this, and the test is behavioural: a view *change* produces deltas.** A module that reports its viewport once when the diagram opens has implemented the first frame of the loop, not the loop; so has one whose `UpdateView` accepts a viewport and returns `[]`. Twelve of the thirteen shipped modules implement it, so a new one that skips it is the odd one out rather than the norm. The thirteenth, dotnet-dependency-graph, answers every view report with the whole graph, deliberately and with a comment on its `UpdateView` saying why. It is an exception on the record, not a precedent.

The mechanism is two correlated legs, and neither is yours to build: `Open` streams a baseline and every later change, while `UpdateView` is a unary call saying what the reader can currently see, correlated to the stream by `watch_id` and path. The backend answers a view report with the deltas that bring the connection into line.

**The client half is written once and is not yours to build**: `viewReportOf` in your stream hook,
`useViewReport` in your canvas, your viewport in your own units and nothing else. The entry is
[the view report](diagram-module-client-api.md#the-view-report); the mechanism is
[`src/client/src/diagrams/readme.md`](../src/client/src/diagrams/readme.md).

**The backend half is yours**, because deciding what a viewport admits is the one part that is genuinely per-module. Store the reported viewport, render, diff against what this connection last held, and return Remove for what left and Add for what appeared or changed — **Remove before Add**, the one order for every module (backend-centralization R4.5). An Add is an upsert keyed on id, so removing first can never delete what the same batch just added; the other order is safe only while the two id sets happen to be disjoint, which nothing enforces. Diff with the shared `DiagramDiff.Between` in `EtAlii.Adp.Diagram`, which produces exactly that order and counts an element as changed when its position, type or payload bytes differ (R4.4). Every shipped session sends that order. All but mindmap diff with `DiagramDiff.Between`; mindmap reacts to the kind of structural change instead (backend-centralization R5.3) and builds its own deltas, but removes first like the rest. What a viewport admits stays yours; the shipped sessions still differ in how they get to the two element lists: some call `DiagramDiff.Between` themselves, some go through the shared `DiagramDocumentChangeHandler`, some diff over expansion state as well as viewport, and some consult a document store first.

Four things about writing that filter cost this specification real time, and none of them is obvious in advance.

**Lay out the whole document, then filter.** Project and position everything whatever the viewport says, and cull only afterwards. A layout computed over the *visible* set repacks as the reader pans — the diagram crawls under them — and it is the only way an element somebody pans to can have a position at all. Cache the projection and layout per session and drop it when the document changes; a pan is not a document change, and re-projecting a large document on every settled pan is work with no new answer.

**Ask what your module emits at a default position before you write the filter.** Four modules hit this independently — wardley's links and evolution axis, databricks' edges, sparql's edges and unplaced nodes and regions, rdf's registration header. An element the layout never placed carries `(0, 0)` or `default`, and a plain point-in-rectangle test keeps it only while the reader happens to be looking at the top-left corner, then culls it on the first pan. It reads as sensible code and passes any test that merely checks the element appeared. The working shape: a frame is always kept; an edge is kept structurally, on whether its endpoints survived, never on its own position; a visible node pulls in what it links to, so no edge is delivered with one end missing; and anything spanning a region is judged on the rectangle it spans rather than on its corner. Pin it with a test.

The scale of what that convention holds up is worth knowing before deciding it is fussy. One vendored thesaurus alone, `business-economics.ttl`, carries **725 `skos:related` links, every one of them packed at the origin** — not by oversight but because an edge has no position of its own. They are delivered correctly today only because all four RDF readings filter edges structurally; the first author to judge an edge by its own coordinate would cull all 725 on the first pan. This is not a bug waiting to be found, it is a correctness property currently held by convention across every reading that exists and every one that comes later, which is why it is stated here rather than left to be rediscovered.

**One filter for every path that decides what to send.** `Baseline`, the document-changed handler and `UpdateView` must render through the same method. A change path that renders unfiltered while `UpdateView` renders filtered leaves the two disagreeing about what the client holds, and an ordinary edit silently re-delivers everything the viewport just culled. It is invisible until somebody edits a document while zoomed in. The same rule caught a second form from the other side: an `UpdateView` that computes its own before-and-after set without updating what the connection is recorded as holding leaves the change path diffing against the whole diagram. Both are the same lesson — **use the same seam, not a variant of it.**

**An unbounded viewport is not "everything".** It is tempting to collapse an unfiltered rendering into `Visible(..., Unbounded)` and delete one of them. Two modules were measured and it is not safe in either: helm admits a node only when the layout gave it a box, and timeline withholds a connection unless both endpoints were sent — and its context resolver looks selections up in the unfiltered method, so the collapse would have made a dangling connection silently unselectable. On well-formed input the two agree exactly; that is a property of the layout placing every node, not of the mapper. Both modules now carry a test pinning the agreement *and* a second naming the deliberate divergence.

## Tests, fixtures and examples

- **The test project** — `EtAlii.Adp.Diagram.Timeline.Tests`, xUnit v3, `<OutputType>Exe</OutputType>` (a v3 test project hosts its own tests). Run the suite with the exact command CLAUDE.md's [Running the backend tests](../CLAUDE.md#running-the-backend-tests) section defines, and read its warnings about `Zero tests ran` before trusting any green.
- **Byte-compared fixtures** — the round-trip corpus in [`Fixtures/`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline.Tests/Fixtures/): a `crlf-line-endings` / `lf-line-endings` pair, `no-trailing-newline`, comments, unmodelled constructs. Because their *bytes* are the test subject, the type's extension gets one `-text` line in [`.gitattributes`](../.gitattributes) (`*.tml -text` is timeline's) — the reasoning is written out in that file so it does not have to be rediscovered; without the line, git's line-ending conversion makes the tests measure git instead of your writer.
- **Examples** — `examples/` holds documents a reader opens and clicks around in, distinct from fixtures, which pin the parser one construct at a time. Every module seeds its examples into the combined [`src/examples/`](../src/examples/) project — the one folder that shows every type in a single explorer tree — and that is how a new type joins the showcase. **The two copies are not held in sync.** They were, byte-for-byte, under a guard that was removed on 2026-09-04: the combined project is a working surface, so arranging a diagram in the running app writes a `layout:` block into the showcase copy while the module copy stays put, and the guard read ordinary product use as drift. A module may still hold its own copies in sync: dependency-graph's `Examples.Tests` requires a byte-identical replica of every example, as both editor modules do. When such a guard fires, the showcase copy is the one to propagate. What still holds for both trees is that every example registration opens against the deployed catalog, which [`ExampleRegistrationTests`](../src/backend/EtAlii.Adp.Backend.Tests/Integration%20Tests/ExampleRegistration.Tests.cs) walks in both places.

## The catalog, and staying true

`docs/diagrams.md` is how "which diagram types does ADP have?" is answered, and CLAUDE.md's [Diagram type catalog](../CLAUDE.md#diagram-type-catalog) rule requires its row to move with your module's state — add or update the row as the type is identified, specified, implemented. And this walkthrough carries the same duty in return: a change that moves a touch point named here updates this document in the same change.
