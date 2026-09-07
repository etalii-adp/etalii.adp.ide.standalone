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

**Not resolved, deliberately:**

* **MSBuild conditions.** A reference inside `Condition="..."` is read as declared, whether or
  not the condition would hold for a given build.
* **`Import` elements and imported props that compute references.** The reader reads the project
  file, not an evaluated project.
* **Properties inside a version or an include.** `$(SomeVersion)` is not expanded; a version it
  cannot read is reported as *not discoverable* rather than guessed at.
* **Transitive packages.** Only direct `PackageReference` declarations become edges — the
  requirements' recorded decision, so that the diagram stays a projection of the files rather
  than of a restore.

**A reference the reader cannot put a version to still becomes an edge**, with the version shown
as not discoverable. Dropping it would misrepresent the project; an edge with an unknown version
is the truth. **An edge is never invented and never silently omitted.**

## Where the package description comes from

**The local NuGet cache, and nowhere else** — the `.nuspec` beside the restored package in the
global packages folder. **No feed is queried and no network call is made**, which is the user's
recorded decision, taken over a more capable recommendation, to keep the product's
"no infrastructure beyond the workspace's own files" principle absolute.

The cost is accepted and explicit: **a package that has never been restored on this machine
shows no description.** That is never an error, never a wait, and never an empty diagram — and
it is why a refresh can be asked for explicitly, since a `restore` makes descriptions available
without touching any file the watcher is watching.
