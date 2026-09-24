# .NET dependency graph — api

`.proto` extensions specific to the `dotnet/dependency-graph` diagram type: the payload one
node or edge carries, packed into the core `Element`'s `Any`. No core proto is edited from
here.

See [../../readme.md](../../readme.md) for what this folder is for, and
[`dotnet-dependency-graph`](../../../../.spec-workflow/archive/specs/dotnet-dependency-graph/) for this
diagram type's spec.

`dotnet-dependency-graph.proto` is the one file here. The folder was empty at task 1, when the
module skeleton landed registering, resolving and drawing nothing; the payload arrived with the
client canvas that needed it.

**The backend project includes the `.proto` by name rather than by glob.** A default-include glob in a project that owns no generated types is how a file dropped
into a folder gets silently double-generated — the trap `EtAlii.Adp.Backend` carried until the
post-decomposition backlog removed it. An absent glob is a rule that cannot be forgotten; an
exclusion list is one that must be remembered.
