# .NET project and package dependency graph — `dotnet/dependency-graph`

The projects of a .NET solution, the NuGet packages they consume, and every `ProjectReference`
and `PackageReference` between them — **computed from the files your build already owns, not
authored**.

| Folder | What is in it |
|---|---|
| `api/` | The wire payload a node or edge carries. Empty until a client needs one. |
| `backend/` | The readers, the graph, the session and the read-only property provider. |
| `client/` | The canvas and the registration the shell discovers. |
| `examples/` | Shipped examples, replicated per structure.md's example-replication rule. |

## What this type writes

**One file, one block: the `layout:` block of its own `.adp` registration.** It never writes to
a `.csproj`, a `.sln` or a `.slnx`. The solution is the truth; the registration holds only where
you put the boxes.

## What the readers resolve, and what they do not

This matters more here than in an authored diagram, because a missing edge in a *derived* graph
looks exactly like a dependency that is not there. **So the limits are written down rather than
left for a reader to infer from a gap.**

**Resolved:**

* `ProjectReference` and `PackageReference` entries, as the project file declares them.
* `TargetFramework` and `TargetFrameworks`, including multi-targeting.
* A `PackageReference` carrying no `Version`, through **central package management**: the reader
  walks upward from the project's folder to find a `Directory.Packages.props` naming the
  package, nearest file winning, as MSBuild does. This repository is centrally managed, so this
  is the ordinary case rather than an exotic one.
* A version written either as a `Version` attribute or as a `<Version>` child element.
* **A wildcard `ProjectReference`**, expanded as MSBuild expands it — `../../diagrams/*/backend/*/*.csproj`
  is three wildcards and resolves to every module it matches. This repository's own host project
  uses one, and reading it literally cost **seventy-eight real edges** and produced two failures
  nobody could act on.

**Not resolved, deliberately:**

* **MSBuild conditions.** A reference inside `Condition="..."` is read as declared, whether or
  not the condition would hold for a given build.
* **`Import` elements and imported props that compute references.** The reader reads the project
  file, not an evaluated project.
* **Properties inside a version or an include.** `$(SomeVersion)` is not expanded; a version it
  cannot read is reported as *not discoverable* rather than guessed at.
* **An `Exclude` beside a wildcard `Include`.** The wildcard expands; anything the project
  excludes from it is still drawn. Reading what a project declares rather than evaluating it is
  the line this reader holds, and a half-evaluated pattern would be a worse answer than an
  honest over-inclusion.
* **Transitive packages.** Only direct `PackageReference` declarations become edges — the
  requirements' recorded decision, so that the diagram stays a projection of the files rather
  than of a restore.

**A reference the reader cannot put a version to still becomes an edge**, with the version shown
as not discoverable. Dropping it would misrepresent the project; an edge with an unknown version
is the truth. **An edge is never invented and never silently omitted.**

**The classic `.sln` path is exercised only by generated fixtures, and permanently so.**
`dotnet solution migrate` goes `.sln` → `.slnx` and there is no reverse, and this repository
holds no `.sln` anywhere — so no real file in this tree has ever reached that reader. What
guards it instead is two fixtures produced by `dotnet new sln` and `dotnet solution add` rather
than typed by hand: the default shape, which files a nested project under a solution folder, and
the `--in-root` shape, which does not. **`--in-root` defaults to `False`, so the solution-folder
line is the SDK's ordinary output** — the case a hand-written fixture would most likely have
omitted, because nobody imagining a solution file imagines that. A maintainer reading a green
suite deserves to know which half of the format has met a real file.

## What it does when the graph is large

**A handful of packages are hidden by default, and the diagram says so.**

Measured against this repository's own `EtAlii.Adp.slnx`: 104 projects, 17 packages, 427 edges
of which 155 are package references — and the package degrees run
`26 26 26 19 18 14 6 6 4 2 2 1 1 1 1 1 1`. **Four package nodes carry 97 of those 155 edges**,
and they discriminate nothing: an edge present on 26 of 104 projects says *this is a test
project*, which the project's own name already says. **A near-universal edge is noise wearing
the shape of information.**

So a package referenced by **15% or more of a solution's projects** (and by at least five of
them) is marked *ambient*, and the canvas leaves it out until you ask for it.

* **Nothing is removed and nothing is truncated.** The graph holds every node and every edge;
  ambient is a marking the canvas reads, and "Show them" restores rather than re-derives. **The
  node count was never the problem** — 119 nodes laid out by depth is busy, not unreadable.
  Nothing here is too big, only too uniformly connected, and a limit would lose real structure
  to fix a problem that is not that shape.
* **It is stated, always.** The canvas says how many it hid, names them, and gives them back in
  one click. Filtering that cannot be seen is just a wrong diagram.
* **Degree, never a list of package names.** A curated "build and test packages" list needs
  maintaining and is wrong on the first repository that is not this one. That has a consequence
  worth knowing rather than discovering: here no threshold separates `Grpc.Tools` (19) from
  `Serilog` (18), so the rule catches both. That is the rule working.
* **A small solution hides nothing.** The five-dependent floor is why: below a handful there is
  no crowd to disappear into, and the shipped four-project example draws complete.

**Only packages are ever hidden, and that is deliberate rather than incidental.** The two
highest-degree nodes here are both *projects*, and both beat every package hub:
`EtAlii.Adp.Backend.Service` with 87 outgoing project edges, and `EtAlii.Adp.Diagram` with 65
incoming ones — more than the 26 of the largest package. A degree rule applied to every node
would hide both, and they carry the **opposite** information from a package at the same degree:
`xunit.v3` on 26 test projects tells you nothing the project names do not, while the composition
root referencing 87 modules tells you how the application is assembled. Same degree signature,
opposite information content.

Projects are **not** grouped or folded either. 102 of them layered by depth is legible, and
grouping is kept available as a later answer to be decided on evidence rather than pre-empted
now.

## Where the package description comes from

**The local NuGet cache, and nowhere else** — the `.nuspec` beside the restored package in the
global packages folder. **No feed is queried and no network call is made**, which is the user's
recorded decision, taken over a more capable recommendation, to keep the product's
"no infrastructure beyond the workspace's own files" principle absolute.

The cost is accepted and explicit: **a package that has never been restored on this machine
shows no description.** That is never an error, never a wait, and never an empty diagram — and
it is why a refresh can be asked for explicitly, since a `restore` makes descriptions available
without touching any file the watcher is watching.
