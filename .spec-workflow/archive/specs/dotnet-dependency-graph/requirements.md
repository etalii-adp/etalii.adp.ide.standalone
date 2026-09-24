# Requirements Document

## Introduction

This spec adds the **`.NET project and package dependency graph`** diagram type (origin `dotnet/dependency-graph`): the projects of a .NET solution, the NuGet packages they consume, and the `ProjectReference`/`PackageReference` edges between them, **computed from the `.csproj` files rather than authored**.

**It is a new module rather than a mode of the existing one, and the reason is provenance, not appearance.** `generic/dependencies` (`src/diagrams/dependency-graph`, catalogued Implemented) is the *authored* graph: a person writes nodes and edges into ADP's own YAML and may write anything. This type writes nothing. Its nodes and edges are a **projection of files the user maintains elsewhere**, it is read-only in every respect except arrangement, and it must be **refreshable** because its subject changes underneath it. Two modules that render similar pictures from opposite directions; merging them would mean one document format that is sometimes the truth and sometimes a cache.

**The `.adp` registration holds layout and nothing else.** There is no body document to own - the solution and its project files are the body. That shape already exists here: `ansible/structure` registers a bare `.adp` whose subject is the surrounding files, so this spec introduces no new file concept.

**Most of what looks novel here is already built, and this spec adopts rather than redefines it.** `RegistrationLayout` in `EtAlii.Adp.Hierarchy` already reads, writes, merges and prunes the `layout:` block and is explicitly "offered to every module"; `databricks-diagrams` Requirement 7 already settled the semantics this type needs - stored positions overlay computed ones element by element, an id with no element is ignored on read and dropped on the next write, and repositioning works on read-only diagrams because arrangement is a view concern rather than an edit to the subject. **The refresh-versus-stored-layout question is therefore answered before it is asked.** What remains genuinely open is named in Requirement 5 and in *Decisions for the user*.

**Dependencies.** This spec redefines none of them:

* `databricks-diagrams` Requirement 7 - the `layout:` block in the `.adp`, adopted wholesale.
* `dependency-graph` - the authored sibling; its canvas conventions are the visual reference.
* `ansible/structure` - the precedent for a derived module whose subject is files on disk.
* structure.md's `diagrams/<diagram>/` module layout and its example-replication rule; tech.md's **Commands** rule; `diagram-undo-redo`; `errors-and-warnings-panel`, where an unresolvable project file's finding lands.

## Alignment with Product Vision

This is the product's **"linkage over illustration"** capability in its purest available form: the elements are not pictures *of* code artifacts, they **are** the code artifacts, named by the files that define them. It is also the clearest case of **"files are the source of truth"** - the graph cannot drift from the build, because the build's own files are what it reads, and the only thing ADP stores is where the user put the boxes. And it delivers **"value from day one"**: a solution acquires an accurate architecture diagram by adding one registration file, with nothing to author and nothing to keep in sync.

## Requirements

### Requirement 1 - A derived module of its own

**User Story:** As a maintainer, I want this to be a full module beside the others, so that it costs core nothing and could be removed without a trace.

#### Acceptance Criteria

1. The type SHALL live at `src/diagrams/dotnet-dependency-graph/` with the `api/`, `backend/`, `client/` and `examples/` layout structure.md requires, and SHALL register the MIME type `dotnet/dependency-graph`.
2. Core SHALL gain no knowledge of .NET, MSBuild, NuGet or solution files; every type this spec introduces SHALL live in the module.
3. The catalog row in `docs/diagrams.md` SHALL be added with its state icon and the `dotnet/dependency-graph` origin tag, and SHALL be moved as the state changes.
4. WHERE `docs/creating-a-diagram-module.md` names a touch point this module moves, that document SHALL be updated in the same change.

### Requirement 2 - The subject: a solution, bound by the registration

**User Story:** As a developer, I want to point ADP at my solution and get its dependency graph, so that I do not have to describe a structure the solution already states.

#### Acceptance Criteria

1. WHEN a registration names a solution file THEN the module SHALL read that solution and treat its projects as the graph's project set.
2. The module SHALL support both `.sln` and `.slnx`.
3. WHERE a folder holds more than one solution, the registration SHALL identify which one, so the binding is never inferred.
4. WHEN a solution is unreadable, or names a project file that is not there THEN the diagram SHALL still open showing what it could resolve, and the failure SHALL be reported as a problem rather than presented as an empty diagram.

### Requirement 3 - The graph: projects, packages, and the two reference kinds

**User Story:** As an architect, I want to see which projects depend on which projects and packages, so that I can judge the shape of a solution I did not write.

#### Acceptance Criteria

1. Each project in the solution SHALL be one element; each distinct package consumed SHALL be one element; the two kinds SHALL be visually distinguishable.
2. Every `ProjectReference` SHALL be a directed edge from the referencing project to the referenced project.
3. Every `PackageReference` SHALL be a directed edge from the referencing project to the package.
4. WHERE a package is referenced by several projects at one version it SHALL be a single element with several incoming edges, so the diagram shows sharing rather than repeating it.
5. WHERE the same package is referenced at different versions, that difference SHALL be visible rather than silently collapsed.
6. The module SHALL resolve references declared through central package management and MSBuild imports to the same degree the build does, or SHALL state in its readme what it does not resolve. **This repository uses central package management, so the question is not hypothetical.**

### Requirement 4 - Element properties, read-only

**User Story:** As a user, I want to select an element and see what it is, so that I can read the diagram without opening the files behind it.

#### Acceptance Criteria

1. A project element SHALL expose **project name**, **target framework**, and **.NET version where available**.
2. A package element SHALL expose **package name** and **package version**.
3. Every property in this requirement SHALL be **read-only**: the property grid SHALL present it as a value that cannot be edited, not as an editor that rejects input.
4. WHERE a property has no value for an element - a project with no discoverable .NET version, say - the grid SHALL show that absence explicitly, rather than an empty field indistinguishable from an empty value.

### Requirement 5 - The package description, and where it comes from

**User Story:** As a user, I want to see what a package actually is without leaving the diagram, because a package id is not always self-explanatory.

#### Acceptance Criteria

1. The property grid SHALL show a **package description** for a package element, read-only.
2. **The description has no source in the repository**, unlike every other property in Requirement 4. The design SHALL state where it is read from and SHALL NOT leave it implied.
3. WHEN a description cannot be obtained THEN the grid SHALL say so explicitly, and the diagram SHALL open and behave normally: a missing description SHALL never be an error, a blocking wait, or an empty diagram.
4. WHERE obtaining a description requires network access, it SHALL be optional and SHALL NOT be required for any other part of this type to work - the product's "no required backend infrastructure beyond the workspace's own files" principle binds here.

### Requirement 6 - Layout: adopted, not redefined

**User Story:** As a user, I want to arrange the graph so it makes sense to me, and find that arrangement again after the solution changes.

#### Acceptance Criteria

1. Authored positions SHALL be stored in the diagram's own `.adp` file in the `layout:` block core already defines, through core's existing `RegistrationLayout`; this spec SHALL add no second layout mechanism.
2. WHEN the diagram opens with no stored layout THEN the module SHALL compute one suited to a directed dependency graph.
3. WHEN the diagram opens with a stored layout THEN stored positions SHALL win over computed ones **element by element**, so a solution that has gained projects degrades gracefully rather than losing the whole arrangement.
4. WHEN a stored id no longer matches any element THEN it SHALL be ignored on read and dropped on the next layout write.
5. Elements SHALL be draggable even though they are read-only: arrangement is a view concern, not an edit to the solution.
6. A reposition SHALL be an undoable command with an inverse, like any other edit.
7. Element ids SHALL be **stable across refreshes**, and the design SHALL state what an id is made of - it is what a stored position is bound to, so a renamed project keeping or losing its place follows from this choice rather than from chance.

### Requirement 7 - Refresh: the graph follows its subject

**User Story:** As a developer, I want the diagram to reflect what my solution says now, not what it said when I opened it.

#### Acceptance Criteria

1. The diagram SHALL be refreshable, so that projects, packages and relations added since it was opened appear.
2. WHEN the graph is recomputed THEN the stored layout SHALL be re-applied per Requirement 6, so a refresh SHALL NOT cost the user their arrangement.
3. WHEN an element disappears from the recomputed graph THEN it SHALL disappear from the diagram, and its stored position SHALL be handled per Requirement 6.4.
4. The design SHALL state whether refresh is automatic on file change, explicit, or both, and SHALL justify that against the product's **live, pushed updates** capability.

### Requirement 8 - Examples

**User Story:** As someone evaluating the type, I want a shipped example that shows a believable graph.

#### Acceptance Criteria

1. The module SHALL ship an example under `src/diagrams/dotnet-dependency-graph/examples/`, replicated per structure.md's example-replication rule.
2. The example's readme SHALL record what the corpus does **not** demonstrate.
3. WHERE example data is vendored from an external source, its licence SHALL be permissive - share-alike refused - and the licence file SHALL be vendored verbatim beside the data as `LICENSE.md`, read from the dataset's own statement rather than from the page around it.

## Non-Functional Requirements

### Performance and scale

* **This repository is itself a live subject, and not a toy.** `EtAlii.Adp.slnx` carries roughly thirty backend projects plus sixty-one diagram modules. The type SHALL remain usable at that size, and the design SHALL say what it does when a graph is too large to read - grouping, filtering, or an explicit limit - rather than rendering an unreadable hairball and calling it correct.
* Reading project files SHALL NOT block the diagram from opening.

### Correctness

* The module SHALL NOT invent edges: an edge SHALL correspond to a declaration in a project file. WHERE a declaration cannot be resolved, the module SHALL report that rather than omit it silently.
* The type SHALL never write to a `.csproj`, `.sln` or `.slnx`. The only file it writes is its own `.adp`, and only that file's `layout:` block.

### Consistency

* The canvas SHALL follow the conventions of the authored `generic/dependencies` module, so that two dependency graphs do not feel like two products.

## Decisions for the user

Three choices change what gets built. They are recorded here rather than settled by whoever implements the task, and each names a recommendation.

1. **Where the package description comes from.** *(a)* the `.nuspec` in the local NuGet cache - offline, no network, but empty until a restore has run; *(b)* the configured feed - always populated, needs network, and the only option that reaches a package never restored locally; *(c)* both, cache first. **Recommendation: (c)** - it satisfies Requirement 5.4 offline and still answers for packages this machine has not restored.
2. **Direct or transitive package references.** A `PackageReference` is a direct declaration; a build resolves transitive ones too. *(a)* direct only - matches the files exactly, and is what "using the `PackageReference` entries" says; *(b)* direct plus transitive, visually distinguished. **Recommendation: (a)** - it keeps the diagram a projection of the files rather than of a restore, and (b) can be added later without invalidating a stored layout.
3. **Whether this repository ships as the example.** *(a)* a small purpose-built solution - readable, demonstrates little; *(b)* `EtAlii.Adp.slnx` itself - honest about scale, and already here. **Recommendation: both** - (a) as the readable example and (b) as the scale example, because the scale question above needs a subject that actually exercises it.
