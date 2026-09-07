# Design Document

## Overview

The `dotnet/dependency-graph` module reads a .NET solution, derives a graph of its projects and the NuGet packages they consume, and renders it read-only except for arrangement. It writes exactly one thing: the `layout:` block of its own `.adp` registration.

**`ansible/structure` is the template, not merely a precedent.** It is the repository's existing derived module - subject on disk, nothing authored, read-only properties, layout in the registration - and this design follows its component shape deliberately, so a maintainer who knows one knows the other. Where this type diverges from it, the divergence is called out and argued.

**Recorded decision - the package description comes from the local NuGet cache only.** The requirements offered three options and recommended cache-plus-feed as the more capable one; **the user chose cache-only, and the reasoning belongs beside the decision**: a feed lookup would put an outbound network call inside a product whose principle is "no required backend infrastructure beyond the workspace's own files", and the user kept that principle absolute rather than optional. The cost is accepted and explicit: **a package that has never been restored on this machine shows no description.** Per Requirement 5.3 that is never an error, never a wait, and never an empty diagram.

## Steering Document Alignment

### Technical Standards (tech.md)

* **Commands.** The single edit this type permits - a reposition - is dispatched as core's existing `SetRegistrationLayoutCommand`, so it is undoable with an inverse like any other edit. The module defines no command of its own.
* **Read-only is enforced, not styled.** Properties are described with a non-empty `ContextPropertyDefinition.ReadOnlyReason`; `ContextPropertyResolver` refuses writes to such a property server-side, whatever a client claims. The provider's `SetAsync` refuses anyway, because a provider that would accept a write if the resolver ever changed is a trap rather than a design.
* **Serilog** per class, `Log.ForContext<T>()`, as everywhere else.

### Project Structure (structure.md)

* `src/diagrams/dotnet-dependency-graph/` with `api/`, `backend/`, `client/`, `examples/`, following the module layout and the example-replication rule.
* Backend assembly `EtAlii.Adp.Diagram.DotNetDependencyGraph`, tests beside it, namespace matching.

## Code Reuse Analysis

### Existing Components to Leverage

* **`RegistrationLayout`** - reads, writes, merges and prunes the `layout:` block. `Apply(computed, stored)` gives Requirement 6.3 and 6.4 outright; `Prune` gives 6.4's write half. **This module adds no layout mechanism.**
* **`SetRegistrationLayoutCommand`** - the reposition, undoable.
* **`IDiagramSessionFactory` / `IDiagramSession`** - the module's entry point, shaped as `AnsibleSessionFactory` shapes it.
* **`IContextPropertyProvider`** with `ReadOnlyReason` - Requirements 4 and 5, and `AnsibleContextPropertyProvider` is the 100%-read-only precedent: every row carries a reason, and each reason names the file the value lives in and how it is edited, cause first then remedy.
* **`IContextSourceResolver`** - selection routing for elements.
* **File watching**, as `AnsibleWatchedFolder` does it, for Requirement 7.

### Where `RegistrationLayout` actually lives, which is not where its comment says

**Its doc comment reads "Defined once here, in core"; it lives in `EtAlii.Adp.Hierarchy`, an area project.** Six diagram modules already consume it - ansible-structure, causal-loop, databricks, helm-charts, rdf, sparql - **and none of them references `EtAlii.Adp.Hierarchy`**: they reach it transitively through `EtAlii.Adp.Diagram`, which does reference it.

**This design follows that established path rather than diverging**, because consistency with six modules beats a seventh doing it differently. **The placement is recorded here as current-but-questioned, not as settled architecture**: a `.adp` file-format utility living in the hierarchy area, reached transitively by every diagram module, looks like a decomposition leftover, and `EtAlii.Adp.Documents` - which owns the `AdpFileWriter` that `RegistrationLayout` calls to save - is the obvious home. That is a backlog question, routed, and **it changes nothing in this design except which project name appears in a reference if it moves.**

**Correction, added after approval and after measurement: the observation above is true and the remedy it implies is false.** Developer 3 enumerated all 65 Hierarchy types against all 947 module `.cs` files and found that **no module's only Hierarchy use is `RegistrationLayout` - not one.** All six that consume it also consume `DiagramFilePair`, `DiagramFileRouter` and `HierarchyModel`; the other six use Hierarchy types regardless. **So moving `RegistrationLayout` to `EtAlii.Adp.Documents` would remove no module's dependency on Hierarchy** - it would relocate a file and leave every edge in place. **The dependency is over-determined, and `RegistrationLayout` is not a necessary cause of it**, which is the same error this repository records at *not a cause* versus *not a necessary cause*. The verdict is **leave it**: no structural gain, roughly fifty files of churn, and item 12's sweep means twenty-nine of them would need a `using` deleted rather than swapped. The placement stays **current-but-questioned**, the false *"Defined once here, in core"* comment is fixed separately, and this paragraph exists so a reader meeting the true sentence above does not draw the false conclusion from it - as both its author and the coordinator did before it was measured.

### A dependency deliberately not taken

**The location of the Contexts family is in flux** - a rename moving the Context provider contract out of `EtAlii.Adp.Common` into `EtAlii.Adp.Context` is in progress at the time of writing. **This design does not depend on where those types live.** It consumes `IContextPropertyProvider` and `IContextSourceResolver` by name through `EtAlii.Adp.Diagram`'s reference chain, exactly as the six existing modules do, so wherever the family lands the module follows without redesign. **Today's layout is recorded as today's, not as settled.**

## Architecture

Four stages, each independently testable, with the boundary between "read the files" and "make a graph" kept sharp so the parsing can be tested without a canvas and the graph without a filesystem:

1. **Bind** - the registration names a solution; the session resolves it.
2. **Read** - the solution yields project files; each project file yields its references and target framework; the package cache yields descriptions.
3. **Derive** - readings become a graph of nodes and edges with stable ids.
4. **Place** - computed positions, overlaid element-by-element by stored ones.

### Binding: a `body:` header, unlike ansible's bare registration

**This is the one place the design departs from its template, and the reason is Requirement 2.3.** `ansible/structure` declares no document extension, so the router routes its registration as its own body and the subject is the containing folder. That cannot identify *which* solution when a folder holds two.

**So this module declares `.sln` and `.slnx` as document extensions and binds through the `body:` header** - the `c4` pattern (`body: bottling-mes.dsl`) rather than the ansible one. The registration therefore reads:

```
dotnet/dependency-graph
body: EtAlii.Adp.slnx
layout:
  project:src/backend/EtAlii.Adp.Diagram/EtAlii.Adp.Diagram.csproj: 120.5 40
  package:Serilog: -60 220
```

The binding is stated, never inferred, which is what Requirement 2.3 asks for.

## Components and Interfaces

### Backend: reading

* **`SolutionReader`** - parses `.sln` (the classic `Project(...)` lines) and `.slnx` (XML). Yields project file paths, resolved relative to the solution. Unreadable solution or missing project file: reported as a problem, the rest of the graph still built (Requirement 2.4).
* **`ProjectReader`** - parses one `.csproj` for `ProjectReference`, `PackageReference`, `TargetFramework`/`TargetFrameworks`. **Central package management is in scope and not hypothetical - this repository uses it**, so a `PackageReference` carrying no `Version` has its version resolved from `Directory.Packages.props`, walking upward as MSBuild does. What the reader does *not* resolve - arbitrary MSBuild conditions, imported props computing references - is stated in the module readme rather than silently dropped (Requirement 3.6).
* **`PackageDescriptionReader`** - **local NuGet cache only.** Reads `<global-packages>/<id>/<version>/<id>.nuspec` and takes its `<description>`. Never queries a feed. Returns absence, not an error, when the package is not cached.

### Backend: the graph and its ids

* **`DependencyGraph`** - nodes and edges derived from the readings.

**Element ids are the load-bearing decision in this document, because a stored position is bound to one.** They are:

* **`project:<path relative to the solution>`** - deterministic, stable while the file stays put, and legible in the `.adp` beside ansible's `playbook:site.yml`.
* **`package:<package id>`** - **deliberately without the version.**

**Excluding the version from a package id is the sharpest choice here, and it resolves a real tension between two requirements.** Keying on id-plus-version would make Requirement 3.5 free - different versions would simply be different elements - but **a version bump would change the id and silently lose the user's stored position, and a package upgrade is the single commonest change a solution undergoes.** That would breach Requirement 7.2, which says a refresh must not cost the user their arrangement, on the most frequent edit there is.

So: **one element per package id, with the version carried as a property.** Requirement 3.4 (sharing shown once) falls out. Requirement 3.5 is met by making the difference *visible on the element* - a package referenced at two versions shows both, and is marked as a version conflict, which is more useful than two disconnected boxes anyway, because a conflict is precisely the thing a reader wants surfaced. **Silently collapsing is what 3.5 forbids; collapsing loudly is what it asks for.**

### Backend: session, layout, refresh

* **`DotNetDependencyGraphSessionFactory`** - resolves the solution from the registration's `body:` header, opens a session per connection, takes `IHistoryStackStore` for the reposition command.
* **`DotNetDependencyGraphSession`** - holds the derived graph, serves it, applies layout.
* **`DotNetDependencyGraphLayout`** - computes a layered placement suited to a directed dependency graph: projects layered by depth, packages banded at the edge they are consumed from. `RegistrationLayout.Apply` then overlays stored positions element by element.
* **Refresh is automatic on file change, and also explicit.** Automatic because the product capability is **live, pushed updates** and every other module behaves that way - a watcher over the solution, its project files and `Directory.Packages.props`. Explicit as well because a package description can become available after a restore that touches no watched file, so a manual refresh is the only way to pick that up (Requirement 7.4).

### Backend: context providers

* **`DotNetContextPropertyProvider`** - `ContextScope.DiagramElement`. Project rows: name, target framework, .NET version where discoverable. Package rows: id, version(s), description. **Every row carries a non-empty `ReadOnlyReason`**, following the house convention of cause then remedy - naming the file the value comes from and how it would be changed. **Absence is shown as absence**, distinct from an empty value (Requirement 4.4), and a missing description says so (Requirement 5.3).
* **`DotNetContextSourceResolver`** - routes selection to elements.

### Client

* A canvas following `generic/dependencies`' conventions, so two dependency graphs do not feel like two products. Projects and packages visually distinct; edges directed.
* Elements drag - **read-only subject, arranged view** - and a drop dispatches the reposition command.

## Data Models

```
ProjectNode   : Id, Name, ProjectPath, TargetFrameworks, DotNetVersion?
PackageNode   : Id, PackageId, Versions[], Description?, HasVersionConflict
DependsOnEdge : Id, FromElementId, ToElementId, Kind (Project | Package)
```

`Description?` and `DotNetVersion?` are nullable on purpose: **null means "not discoverable", which the grid renders differently from an empty string**, and the two must not be conflated.

## Error Handling

### Error Scenarios

1. **Solution unreadable** - the diagram opens empty with a problem reported; it does not fail to open.
2. **Project file named by the solution is missing** - that project is omitted, a problem is reported naming it, and the rest of the graph is built (Requirement 2.4).
3. **Project file unparseable** - same: omit, report, continue.
4. **Package version unresolvable** (no `Version`, nothing in central package management) - the package element exists with its version shown as not discoverable, rather than the edge being dropped. **An edge is never invented and never silently omitted** (non-functional: correctness).
5. **Package not in the local cache** - no description. Not an error, not a wait, not an empty diagram.
6. **Stored layout id matches no element** - ignored on read, dropped on next write. Core's behaviour, inherited.

## Testing Strategy

### Unit Testing

* `SolutionReader` against both formats, including a `.slnx` and a classic `.sln` of the same solution.
* `ProjectReader` against: a direct `PackageReference` with a version; one without, resolved from `Directory.Packages.props`; a `ProjectReference`; multi-targeting.
* Id stability: **a package whose version changes keeps its element id** - the test that guards the design decision above, and it must be seen to fail against an id that includes the version.
* `PackageDescriptionReader` against a cache layout with the package present and absent.

### Integration Testing

* Open a registration bound to a small solution; assert nodes, edges and computed layout.
* Reposition, reopen, assert the stored position wins for that element and computed positions hold for the rest.
* Add a project to the solution, refresh, assert the arrangement of the untouched elements survives.
* Remove a project, refresh, assert its element is gone and its stored id is dropped on the next write.

### End-to-End Testing

* **`EtAlii.Adp.slnx` itself** - roughly thirty backend projects plus sixty-one diagram modules - as the scale subject required by the non-functional requirements. This exercises the readability question rather than asserting it away, and the design's answer to "too large to read" is measured against it rather than assumed.
