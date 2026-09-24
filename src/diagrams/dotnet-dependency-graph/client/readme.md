# .NET dependency graph — client

The read-only canvas for `dotnet/dependency-graph`, and the registration the shell discovers.

The canvas (`DotNetDependencyGraphCanvas.tsx`), the model (`dotnetDependencyGraphModel.ts`)
and the stream hook (`useDotNetDependencyGraphStream.ts`) arrived with task 10 — which follows the authored `generic/dependencies`
module's conventions deliberately, so that two dependency graphs do not feel like two products.

The shell finds a module's client by scanning `src/diagrams/<type>/client/register.ts`, so
adding the canvas here is the whole of what the shell needs; nothing in the shell names this
type.
