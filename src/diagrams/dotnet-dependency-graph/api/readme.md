# .NET dependency graph — api

`.proto` extensions specific to the `dotnet/dependency-graph` diagram type: the payload one
node or edge carries, packed into the core `Element`'s `Any`. No core proto is edited from
here.

See [../../readme.md](../../readme.md) for what this folder is for, and
[`dotnet-dependency-graph`](../../../../.spec-workflow/specs/dotnet-dependency-graph/) for this
diagram type's spec.

**Empty at task 1, and the emptiness is deliberate.** The module skeleton lands registering,
resolving and drawing nothing, so that every later task changes a module that already builds.
A payload arrives with the client canvas that needs one.

**When a `.proto` does land here, the backend project includes it by name rather than by
glob.** A default-include glob in a project that owns no generated types is how a file dropped
into a folder gets silently double-generated — the trap `EtAlii.Adp.Backend` carried until the
post-decomposition backlog removed it. An absent glob is a rule that cannot be forgotten; an
exclusion list is one that must be remembered.
