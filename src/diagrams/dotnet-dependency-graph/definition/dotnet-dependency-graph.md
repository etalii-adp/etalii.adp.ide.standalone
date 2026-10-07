# .NET dependency graph

The companion of [`dotnet-dependency-graph.dis`](dotnet-dependency-graph.dis), the DISL specification of the `dotnet/dependency-graph` diagram type. The `.dis` says what DISL 0.3 can say; this file holds everything it cannot, each point tied to the code or document that shows it. Where the two ever differ, the standalone code is right and both files are corrected to match it.

The diagram shows the projects of a .NET solution, the NuGet packages they consume and every `ProjectReference` and `PackageReference` between them, **read from the files the build already owns rather than authored**. It is read-only in every respect except where a user puts the boxes.

## Sources

Everything here was read from these, on `develop` as of 2026-09-30, and checked against the code again on 2026-10-07 when the `.dis` moved to DISL 0.3 and the standalone module began running it:

- The standalone module, [`src/diagrams/dotnet-dependency-graph/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/src/diagrams/dotnet-dependency-graph) in etalii.adp.ide.standalone: its readmes, the backend (`Diagram.cs`, `SolutionReader.cs`, `ProjectReader.cs`, `PackageDescriptionReader.cs`, `DependencyGraph.cs`, `DependencyGraphStore.cs`, `DotNetDependencyGraphLayout.cs`, `DependencyElementMapper.cs`, `DotNetDependencyGraphSession.cs`, `SolutionWatcher.cs`, `DotNetContextPropertyProvider.cs`, `DotNetContextSourceResolver.cs`, `DotNetSolutionDocumentFactory.cs`), the client (`DotNetDependencyGraphCanvas.tsx`, `dotnetDependencyGraphModel.ts`, `dotnet-dependency-graph.css`), `api/dotnet-dependency-graph.proto`, the tests and fixtures, and `examples/example 1/pipeline-toolkit`.
- Core's `RegistrationLayout.cs` (`src/backend/EtAlii.Adp.Hierarchy/`) and the host's theme colours in `src/client/src/index.css`.
- The archived specification [`.spec-workflow/archive/specs/dotnet-dependency-graph/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/.spec-workflow/archive/specs/dotnet-dependency-graph): requirements, design, tasks and findings. Its requirement numbers are cited below as *R n.m*.
- The standalone catalogue row in `docs/tools.md` (state ⚗️ Prototype) and `docs/creating-a-diagram-module.md`.
- The Notion "Tools" database row ".NET dependency graph": Kind Diagram, focus area Software delivery, rarity "Unique to ADP", one-line purpose "Show the projects of a .NET solution, the NuGet packages they use and every reference between them.", why specialized "References are spread over many project files; one graph reveals cycles, duplicate packages and unexpected couplings."
- This project's conversations: the project timeline holds nothing about this tool beyond the request to write this file. The IntelliJ, VS Code and Eclipse hosts do not implement it.

## How the `.dis` maps the implementation

| Standalone | In the `.dis` |
|---|---|
| `ProjectNode` (`Name`, `RelativePath`, `TargetFrameworks`, `DotNetVersion`) | node type `Project` (`name`, `path`, `targetFrameworks`, `dotNetVersion`) |
| `PackageNode` (`PackageId`, `Versions`, `HasVersionConflict`, `Description`, `DependentProjectCount`, `IsAmbient`) | node type `Package` (`name`, `versions`, `hasVersionConflict`, `dependentProjectCount`, `isAmbient`, `description`) |
| `DependsOnEdge` with `Kind = Project` / `Package` | relation types `ProjectReference` / `PackageReference`, without attributes |
| The readers, the store and the watcher | the required plugin `net.etalii.adp.dotnet.solution` as persistence format |
| `DotNetDependencyGraphLayout` | the optional layout plugin `net.etalii.adp.dotnet.dependencyLayers`, falling back to `layered` |
| `DotNetContextPropertyProvider`, which derives its rows from the forms | the forms `projectInspector` and `packageInspector`, the named reasons `projectFile` and `referencingProjects`, and `behavior.messages` `std.readOnly` as the refusal of a write |
| The canvas' activate gesture and `revealPath` | operation `revealProjectFile`, the double-click of a project |
| The canvas' `showAmbient` state and its two buttons | transient diagram attribute `showAmbientPackages` and two operations |
| `layout:` block of the `.adp` | `persistence.view.store: ["bounds"]` in the view file `{name}.adp` |
| The wire ids: element kinds, the activate action and the property row ids | the `x-dotnet` block |

**The plugin computes the summaries, so they are stored, not derived.** `DependencyGraph.Derive` computes `Versions`, `HasVersionConflict`, `DependentProjectCount` and `IsAmbient` once per reading and carries them on the package node (and on the wire), and `ProjectReader` computes `DotNetVersion`. The `.dis` therefore declares the five as read-only attributes the plugin supplies, with their rules in their `doc`, rather than as CEL derivations over the references. An earlier version derived them in CEL from a `version` attribute on each `PackageReference`; that was not what the code does, and it was not equivalent either: the code compares versions without regard to case where CEL's `distinct` and `sort` do not, and it counts a package's versions over every declaration while counting its dependents by exact id, so a reference spelled in another casing counts towards one and not the other (see *Known gaps*). A reference carries no attribute at all in the code, so neither relation declares one.

## The binding: the `.adp` registration

A diagram of this type is a **registration file** (`.adp`) that names the solution through a `body:` header, beside the solution it names. The shipped example, `examples/example 1/pipeline-toolkit/PipelineToolkit.adp`, reads in full:

```
dotnet/dependency-graph
body: PipelineToolkit.slnx
```

After the user has dragged boxes it gains a `layout:` block, one line per dragged element, `  <element id>: <x> <y>`:

```
dotnet/dependency-graph
body: EtAlii.Adp.slnx
layout:
  package:Serilog: -60 220
  project:src/backend/EtAlii.Adp.Diagram/EtAlii.Adp.Diagram.csproj: 120.5 40
```

- The first line is the origin tag. The `body:` path is relative to the registration. DISL's `persistence.files` can only give name patterns (`{name}.slnx`, `{name}.adp`); it cannot say that the model file is whatever the view file's `body:` header names, nor that the model file may be `.sln` or `.slnx`.
- **Binding is stated, never inferred** (R 2.3): a folder can hold two solutions. This is why the type declares `.sln` and `.slnx` as document extensions (`Diagram.cs`) rather than taking the containing folder as its subject the way `ansible/structure` does.
- **The extension is shared** (`SharedExtension: true`): a solution does not become a diagram by being in the workspace. It becomes one when the user registers it through *Add*, which writes the registration. `.sln` is the primary extension and `.slnx` the alternate; one engine, one catalogue row.
- **Positions are written by core's `RegistrationLayout`**: entries ordered by id (ordinal), coordinates formatted `0.###` in the invariant culture, new lines CRLF, and the block removed entirely when it would be empty. Each write is core's `SetRegistrationLayoutCommand`, so a drag is one undoable step on the project's history (R 6.6). A stored id that matches no element is ignored on read and dropped on the next write (R 6.4). Only the position is stored, never a size.
- **"New .NET dependency graph"** in an empty folder creates the registration plus an empty solution whose whole content is `<Solution>\r\n</Solution>\r\n` (`DotNetSolutionDocumentFactory`). Core refuses to create at a name that already exists, so this can never overwrite a solution. It is the only file of the solution family the type ever creates, and it never writes one afterwards.

## Reading the model

What the `net.etalii.adp.dotnet.solution` plugin does, which DISL has no vocabulary for. Nothing in this section writes a file.

### The solution

- **`.slnx`** is parsed as XML. Every `Project` element at any depth (inside `Folder` elements or not) contributes its `Path` attribute; `File` entries filed beside projects (a `Directory.Packages.props`, an `.editorconfig`) are not projects. Unparseable XML yields no projects and the failure "The solution file could not be parsed."
- **`.sln`** is read line by line with the pattern `Project("{type}") = "name", "path", "{id}"`. A line whose type GUID is `2150E333-8FDC-42A3-9474-1A3956D46DE8` (case-insensitive) is a solution folder and is skipped, not reported missing; `dotnet new sln` files nested projects under solution folders by default, so this is the ordinary shape. The classic format is exercised only by generated fixtures (`Fixtures/solution/Flat.sln`, `Both.sln`); no real `.sln` exists in the standalone repository.
- Declared paths are normalised (both `\` and `/` accepted), resolved against the solution's folder, and must exist. A declared project that is not on disk becomes the failure "The solution names this project, but the file is not there." and the rest of the graph is still built (R 2.4).
- An absent solution fails with "The solution file does not exist."; a locked or unreadable one with "The solution file could not be read: <reason>". The solution is read with shared access, so an IDE saving it at the same moment does not make it unreadable.
- A project's **name** is its file name without extension; its **path** is relative to the solution folder and **forward-slashed on every platform**, because the element id is built from it.

### Each project file

- **Target frameworks**: every `TargetFramework` and `TargetFrameworks` element, split on `;`, trimmed, de-duplicated case-insensitively; a value containing `$` is an unexpanded property and is dropped.
- **Project references**: every `ProjectReference` with a non-empty `Include`. An `Include` containing `*` is **expanded as MSBuild would**, segment by segment, so `..\..\diagrams\*\backend\*\*.csproj` (three wildcards) resolves to every module it matches; a folder that cannot be listed costs only its own branch. An `Exclude` beside a wildcard is **not** applied: the reader reads what a file declares rather than evaluating it, and prefers an honest over-inclusion to a half-evaluated pattern. Reading the wildcard literally cost the standalone repository's own graph seventy-eight real edges.
- A reference is matched to a solution project by absolute path (case-insensitive), so two spellings of one path are one node. A reference to a project **the solution does not contain** is not drawn and becomes the failure "<project> references this project, but the solution does not contain it." An edge is never invented and never silently omitted.
- **Package references**: every `PackageReference`, its id from `Include` or else `Update`. Its version is, in order: a `Version` attribute, a `<Version>` child element, or **central package management** — walking upward from the project's folder to each `Directory.Packages.props` and taking the first `PackageVersion` whose `Include` names the package (case-insensitive), which is MSBuild's nearest-wins rule. A props file that cannot be read costs its own versions, not the walk. When nothing gives a version the reference is still drawn, with its version *not discoverable* (unset, which is different from empty).
- A project file that cannot be read or parsed becomes the failure "The project file could not be read: <reason>" and contributes no node. A project the solution resolved but that was not read becomes "This project was not read, so its references are unknown."

### What is deliberately not resolved

Stated because a missing edge in a derived graph looks exactly like a dependency that is not there (module readme, R 3.6):

- **MSBuild conditions**: a reference inside `Condition="..."` is read as declared, whether or not the condition holds.
- **`Import` elements** and imported props that compute references.
- **Properties inside a version or include**: `$(SomeVersion)` is not expanded; such a version is *not discoverable*.
- **`Exclude` beside a wildcard `Include`**, as above.
- **Transitive packages**: only direct `PackageReference` declarations become edges, the user's recorded decision, so the diagram stays a projection of the files rather than of a restore.

### Package descriptions

- Read from the **local NuGet cache only** — the user chose this over the requirements' recommended cache-plus-feed, to keep "no infrastructure beyond the workspace's own files" absolute (design, *Recorded decision*). **No feed, no network call.**
- The cache folder is `NUGET_PACKAGES` where set, else `~/.nuget/packages`. The file is `<cache>/<id lowercased>/<version lowercased>/<id lowercased>.nuspec`, because NuGet lowercases on restore whatever casing the reference used.
- Every version the package is asked for is tried in order; the first `.nuspec` that answers wins, since a description describes the package rather than the release. The `description` element is matched by local name, so every `.nuspec` schema namespace works; the value is trimmed.
- A package never restored on this machine has **no** description (null); a `.nuspec` with an empty description has an **empty** one. The inspector shows the two differently. A cache entry that cannot be read is treated as absent.
- A `restore` makes descriptions available without touching any watched file, which is why an explicit refresh exists (R 7.4; see *Known gaps* for its reachability).

### Failures

Every failure above travels with the graph as `(path as the solution or project named it, reason)`, and the diagram opens with whatever resolved (R 2.4). **The failures are logged and nowhere shown**: the module registers no validator, so nothing reaches the host's problems panel, although the canvas's empty-solution message says a reason is reported there (see *Known gaps*). They are not DISL constraints: they are about the files the model was read from, not about the model, and DISL has no way for a persistence plugin to report problems. The reason texts quoted above are verbatim.

## Element ids

A stored position is bound to an id, so the id scheme is the module's load-bearing decision (R 6.7, `DependencyGraph.cs`). DISL's `natural` id strategy with prefixes says the node half; the rest is here.

- **Project**: `project:<path relative to the solution>`, forward-slashed. Renaming the assembly keeps its place; moving the file loses it, by choice rather than chance.
- **Package**: `package:<package id>`, **never including the version**. A version bump is the commonest change a solution undergoes, and an id that included the version would silently lose the user's arrangement on it (R 7.2). The price is that a version conflict is one element marked as such rather than two boxes. Package ids are grouped case-insensitively; the element takes the spelling of the first reference read (but see *Known gaps* for references spelled differently).
- **Reference**: `depends:<source id>-><target id>`. The client reads both ends back out of the id rather than from the payload. Relations have no key attribute in the `.dis`, so DISL's `natural` strategy says nothing about them.

## Layout

The `net.etalii.adp.dotnet.dependencyLayers` plugin computes, for every element without a stored position (`DotNetDependencyGraphLayout.Compute`):

1. **Depth.** Over `ProjectReference` edges only, a project that references no other project has depth 0; any other project has one more than the deepest project it references. A cycle cannot hang it: a node reached again while being visited counts as depth 0. (MSBuild refuses cyclic references, but the files may be wrong.)
2. **Layers.** Layers in ascending depth, left to right. Inside a layer, projects are ordered by name (case-insensitive) and stacked 90 apart from y = 0; after twelve, the layer **wraps** into a further column 260 to the right (the 13th project starts column two at the top). Each layer starts 320 to the right of where the previous one began, plus 260 per extra column the previous layer used, so a wrapped layer never draws over the next.
3. **Package band.** All packages in one column at x = (rightmost project x) + 320 + 200, ordered by id (case-insensitive), 90 apart from y = 0.
4. **Stored positions win element by element** (R 6.3): an element with a `layout:` entry is drawn there, every other element at its computed position, recomputed on every render. So a solution that gains a project keeps every arrangement and places only the newcomer. This is closest to DISL's `respect: "pinned"` with a stored position counting as the pin; DISL cannot say that pinning happens implicitly on drag and that unpinned elements are re-laid out on every render.
5. The arrangement is deterministic: the same graph yields the same positions every time.

The positions are the top-left of a 220 × 56 box in canvas units. The wrap point of twelve was measured: the standalone solution's depth distribution was 1/3/7/4/3/2/4/66/14, and one unwrapped layer of 66 was 5,850 units tall against a 3,080-unit-wide diagram. Wrapping rather than a limit, because a derived diagram that silently shows part of its subject is worse than one that is awkward to read.

**Direction of the lines.** Because a project sits to the right of what it references, project references point **leftwards** and package references point **rightwards** into the band. `layout.direction: "right"` in the `.dis` describes the layer order, not a single flow direction, which DISL's `layered` algorithm would assume; hence the plugin.

## Routing

Both reference kinds use one custom route (`dependencyRoute` in the canvas). The `.dis` states it as DISL's `bezier` routing from the right side to the left with `backward: "loop"`, which says its shape but not its numbers:

- Edges attach at the **sides** of a box (left or right, never top or bottom).
- Normally the route is a horizontal Bézier between the two facing side anchors.
- When the target starts left of the source's right edge (`target.x < source.x + source.width`), which is every project reference, the route leaves the source's **right** side and enters the target's **left** side with a forward Bézier, so the curve goes the long way round rather than doubling back through its own source.
- The forward curve's control points both sit at the horizontal midpoint between its ends, with no minimum; DISL's `reach: "auto"` is half the horizontal distance but at least 30. The backward curve's reach is `max(span / 2, 40, -0.6 × span)`, where `span` is the target's x minus the source's, which DISL's `loop` grows without stating by how much.
- A solid arrowhead at the target end only (DISL's `arrowFilled`). No bendpoints, nothing editable, no self-loops, no parallel edges.

## Notation details beyond the `.dis`

- Colours are the module's own tokens, both themes declared (`dotnet-dependency-graph.css`); the edge colours and the conflict colour are the host's `--color-text-muted`, `--color-border` and `--color-warning`, and the font is the one the host sets on its page body, which the canvas text inherits, copied into the `.dis` as tokens with their current light and dark values. The canvas names classes and never colours, and the backend never sends one.
- The subtitle is 0.75 rem (12 px at the default size), offset 14 below the label's centre line, shown only when non-empty: target frameworks for a project, versions for a package.
- Project and package labels truncate rather than wrap.
- A version conflict replaces the package's dashed outline with a solid 2.5-wide outline in the warning colour.
- The node box is the shared "span" shape the authored `generic/dependencies` module also uses, slot for slot, so the two dependency graphs look like one drawing in two colourways (the non-functional *Consistency* requirement).

## Interaction

- **Dragging a box** is the only edit. It stores the position (see *The binding*) and the session pushes the move back through the project's history, so undo and redo of a move are pushed too. Without a history the diagram is read-only and a drag answers "This diagram is read-only."; opened without a registration it answers "This diagram was opened without a registration, so there is nowhere to store a position."
- **An edge cannot be moved**: it follows its ends. "That element is not something this diagram can move."
- **Reparenting** is refused with "A .NET dependency graph is drawn from the solution's own files, so nothing on it can be moved from here. Change a reference in the project file, and the diagram will follow."
- **Nothing can be created, deleted, copied, connected, reconnected or renamed.** The toolbox offers nothing; the `.dis` says so with an empty toolbox, `deletable`/`copyable`/`connectable`/`reconnectable` false, forbidding deletion policies and read-only attributes.
- **There is no context menu of the type's own.** Right-clicking selects the element and opens the host's shared menu, to which this type contributes nothing; the `.dis` declares no `contextMenus`.
- **Selecting** a project, package or reference makes it the current selection for the property grid, ribbon and menu (`DotNetContextSourceResolver`). A project's selection path is its relative path's segments; a package's is its id; a reference's is its id, and its display text is `ProjectReference` or `PackageReference`. A selection is only accepted inside its own diagram and is re-verified against the graph.
- **Activating** a project (double-click or keyboard) reveals its project file in the workspace explorer. Activating a package does nothing: it is not a file in the workspace.
- The canvas is exposed to assistive technology as an application labelled ".NET dependency graph", each box as a focusable button labelled like its tooltip.

## Ambient packages

The scale answer (module readme, `DependencyGraph.AmbientShare`, `withoutAmbientPackages`):

- A package is **ambient** when it is referenced by at least `max(5, ceil(0.15 × number of projects))` projects. The `.dis` states this as the derived attribute `isAmbient`.
- Ambient packages and every reference touching them are **hidden by default**. Nothing is removed from the graph; hiding is a view state of the open canvas, **never persisted**, and resets when the diagram is reopened.
- **Hiding is always stated.** While any are hidden, a notice anchored bottom-left of the canvas reads, verbatim, "<n> packages are hidden as ambient — referenced by so many of this solution's projects that the edge tells you nothing: <names, comma-separated>." (singular "package is" for one), with a **Show them** button. While they are shown, the notice reads "Every package is drawn, including the ambient ones." with a **Hide ambient packages** button. DISL has no way to declare such a notice; the `.dis` offers the two operations and hides via a conditional `visible: false` style.
- A small solution hides nothing: the floor of five is why the four-project example draws complete.
- Only packages are ever ambient. The two highest-degree nodes of the standalone solution are projects (the composition root with 87 outgoing project references, the diagram contract with 65 incoming), and they carry the opposite information from a package at the same degree.
- Measured on the standalone solution (2026-09-08): 104 projects, 17 packages, 427 references of which 155 to packages; package degrees `26 26 26 19 18 14 6 6 4 2 2 1 1 1 1 1 1`. Fifteen percent hides five packages there; no threshold separates `Grpc.Tools` (19) from `Serilog` (18), and that is the rule working. Degree rather than a list of names, because a curated list is wrong on the next repository.
- Projects are not grouped or folded; grouping is left to be decided on evidence later.

## The property grid

DISL forms describe the rows; these are the exact rows the standalone grid shows (`DotNetContextPropertyProvider`), all read-only and all single-line:

| Element | Id | Label | Value | Group |
|---|---|---|---|---|
| Project | `dotnet.name` | Name | project name | Identity |
| Project | `dotnet.path` | Path | path relative to the solution | Identity |
| Project | `dotnet.target-frameworks` | Target framework / Target frameworks | comma-joined, or *Not discoverable* | Build |
| Project | `dotnet.dotnet-version` | .NET version | comma-joined versions, or *Not discoverable* | Build |
| Package | `dotnet.package-id` | Package | package id | Identity |
| Package | `dotnet.package-version` | Version / Versions | comma-joined, or *Not discoverable* | Identity |
| Package | `dotnet.package-version-conflict` | Version conflict | "This solution's projects reference this package at more than one version." — only when in conflict | Identity |
| Package | `dotnet.package-description` | Description | the description, or "Not available - this package is not in the local NuGet cache" | About |

- **An absence is a row that says so, never a missing row or a blank** (R 4.4). The `.dis` states each fallback text in the item's `display`. An empty target framework list and an unset .NET version both read *Not discoverable*; a package description that is unset reads *Not available*, while one that is empty in its `.nuspec` is shown empty.
- **Every row carries a read-only reason**, cause then remedy, as the items' `readOnlyReasons`. For project rows: "Defined in <relative path>; edit it in a text editor." (the named reason `projectFile`); for package identity rows: "Defined in the project files that reference it; edit it in a text editor." (`referencingProjects`); for the description: "Published by the package's author and read from the local NuGet cache; nothing in this workspace defines it."
- The labels follow the values: *Target framework* for exactly one, else *Target frameworks*; *Versions* for more than one, else *Version*.
- Read-only is enforced server-side, and the provider refuses any write regardless with "A .NET dependency graph shows the solution as it is and changes nothing in it. Edit the project file this value comes from, and the diagram will follow.", which the `.dis` holds as `behavior.messages` `std.readOnly`.
- References have no rows.

## Refresh

- **Automatic** (R 7.1, 7.4): a watcher covers exactly the files the graph was read from — the solution, every project file it resolved, and every `Directory.Packages.props` above each project (a version bump lands in one of those and in no project file). One non-recursive watcher per directory, filtered to those files, so build output never wakes it.
- **Debounced**: changes settle for 250 ms, restarted on every event, before one refresh; a watcher buffer overflow also counts as a change, since which file changed is unknowable.
- A refresh re-reads everything, re-applies stored positions (R 7.2), removes elements that have gone (R 7.3) and pushes only the differences to the canvas.
- There is **no explicit refresh** a user can reach, so a description that appears after a `restore` (the cache is not watched) shows only when a watched file changes or the diagram is opened again. The `.dis` declares no refresh operation for that reason. See *Known gaps*.
- Every view report is answered with the whole graph, not only what falls in the viewport — a recorded exception among the standalone modules (`creating-a-diagram-module.md`), acceptable at the sizes measured so far.
- When the solution has no project that resolved, the canvas shows "This solution has no projects ADP could resolve. Any reason is reported in the problems panel." Emptiness is judged on the whole graph, not on what the ambient filter left.

## Standalone wire format

Specific to the standalone host, recorded so another host can interoperate if it chooses; the ids are also in the `.dis`'s `x-dotnet` block, from which the standalone module reads them. The MIME type is `dotnet/dependency-graph`; element kinds are `dotnet/dependency-graph+project`, `+package` and `+edge`. Each element carries a `DependencyElementPayload` (`api/dotnet-dependency-graph.proto`, package `etalii.adp.dotnetdependencygraph`): `name`, `kind` (`PROJECT`, `PACKAGE`, `PROJECT_REFERENCE`, `PACKAGE_REFERENCE`), `project_relative_path` (segments, projects only, never absolute), `target_frameworks`, `versions`, `has_version_conflict`, `dependent_project_count`, `is_ambient`. An edge travels at position (0, 0) with its id as its name. Only add and remove deltas are sent; nothing folds.

## Examples

- `examples/example 1/pipeline-toolkit` in the standalone module: four projects (`Pipeline.Core`, `Pipeline.Storage` on `net10.0;netstandard2.0`, `Pipeline.Cli` referencing both, `Pipeline.Core.Tests`), three packages under central package management, and a version conflict (`Serilog` 4.4.0 centrally, pinned to 3.1.1 in the test project). Purpose-built, no third-party licence.
- It does **not** demonstrate a classic `.sln`, an unresolvable reference, transitive packages, package descriptions (unless the packages happen to be restored on the machine), or scale. The scale subject is the standalone repository's own `src/backend/EtAlii.Adp.slnx`, not copied.

## Known gaps and discrepancies

In the implementation, found while writing this:

- **The explicit refresh is not reachable by a user.** `DotNetDependencyGraphSession.Refresh()` is public and documented as the answer to R 7.4, but its only caller is the watcher; no command, button or menu entry invokes it. A description that appears after a `restore` shows only when the diagram is reopened or a watched file changes. The `.dis` no longer declares the operation the earlier version offered.
- **Read failures are not shown.** The canvas says "Any reason is reported in the problems panel", but no validator turns the graph's failures into problems; they are only logged.
- **A package referenced with two casings loses references.** `DependencyGraph.Derive` groups package ids case-insensitively for the node (`package:Serilog`), but builds each reference's target id from that reference's own spelling (`package:serilog`), and counts dependents by exact id. A project writing `serilog` therefore gets a reference to an element that does not exist, which the canvas silently drops, and the package's dependent count and ambient marking miss it, while its versions and conflict still count it. The `.dis` follows the code: the plugin supplies these values, and such a reference is a relation whose target is missing.
- `ServiceCollection.AddDotNetDependencyGraph.cs` says in its remarks that "No `IDiagramDocumentFactory`" will ever appear, then registers `DotNetSolutionDocumentFactory` as one, whose own remarks explain why it must exist. The remarks are stale.
- `DotNetDependencyGraphLayout`'s remarks say "A project that nothing in the solution references sits in the first layer"; the code puts a project that **references** nothing in the first layer (and so does its test `AProjectSitsBeyondWhatItReferences_SoDependencyRunsOneWayAcrossTheCanvas`). This file follows the code.
- The Notion row says the graph "reveals cycles", but nothing detects or marks a cycle; the layout only survives one. A `ProjectReference` cycle could be a DISL constraint (`acyclic` or `inCycle`), and was left out of the `.dis` because no host implements it.
- Notion files the tool under the family "Infrastructure / network / cloud diagrams", which fits a dependency graph of code less well than its focus area "Software delivery".

In DISL 0.3, which this file has to carry instead:

- A **model read from files the diagram type does not own** has no first-class form; the `.dis` uses a required `persistenceFormat` plugin and a split file pattern that cannot name the `body:`-bound file or its two possible extensions. FBL binds one file, so it cannot carry a model spread over a solution, its project files, the props files above them and the NuGet cache either.
- A persistence plugin cannot **report problems**, so the read failures have no DISL home.
- **Implicit pinning on drag** with continuous re-layout of everything unpinned is only approximated by `trigger` and `respect`.
- The **depth-layered layout with a package band** needs a plugin, and the **route's exact reach** is not expressible (see *Routing*).
- A **status notice** stating what the view hides, with its own buttons, cannot be declared.
- The **standalone wire ids** (element kinds, the activate action's id, the property row ids) are not DISL; they sit under `x-dotnet`.
