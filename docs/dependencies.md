# Dependencies

What ADP is built on: every direct dependency of the backend and the client, at which version, why it is there, and under which license. Transitive packages are the package managers' business and are deliberately not listed.

The **Dependency** and **Version** columns are guarded by `DependencyInventoryTests` (in `src/backend/EtAlii.Adp.Backend.Tests/`), which compares these tables against the package manifests — `src/Directory.Packages.props` for the backend, and the npm workspace manifests `src/package.json` declares for the client — and fails the build when a row is missing, spurious, or version-mismatched. The **Reason for usage** and **License** columns are the author's work and are not machine-checked: a reason cannot be generated, and a license lookup would make a unit test depend on package-metadata availability. Instead, the guard forces every version bump to edit its row here — and re-checking the license is part of that same edit. Licenses are the SPDX expressions from the packages' own metadata (the NuGet license expression, the npm `license` field), read at the time the row was written.

Keep the tables in this exact shape — two sections, first column the package name, second the version — because that is what the guard parses.

## Backend

One row per `PackageVersion` entry in `src/Directory.Packages.props`, which manages every backend package version centrally.

| Dependency | Version | Reason for usage | License |
|---|---|---|---|
| `Google.Protobuf` | 3.31.1 | The protobuf runtime behind every generated message — including the `Any` payloads diagram modules pack their element data into. | BSD-3-Clause |
| `Grpc.AspNetCore` | 2.83.0 | Hosts the gRPC services inside the one ASP.NET Core process that is ADP's whole backend. | Apache-2.0 |
| `Grpc.AspNetCore.Web` | 2.83.0 | The gRPC-Web middleware that lets a browser — which cannot speak native gRPC — reach those same services. | Apache-2.0 |
| `Grpc.Core.Api` | 2.83.0 | The gRPC contract assembly - `ServerCallContext` and friends - without the ASP.NET Core server stack. `EtAlii.Adp.Common` references it because its `SessionContext` accessor once compiled against it there. That accessor has since moved to `EtAlii.Adp.Authentication`, which gets gRPC through `Grpc.AspNetCore`, and today no source file in Common uses the package and none of its five protos declares a service. Whether another project relies on it arriving transitively through Common has not been measured. | Apache-2.0 |
| `Grpc.Core.Testing` | 2.46.6 | Fabricates `ServerCallContext` instances so gRPC service methods can be unit-tested without a running server. | Apache-2.0 |
| `Grpc.Net.Client` | 2.83.0 | The gRPC client the integration tests use to call the real host over its own wire. | Apache-2.0 |
| `Grpc.Tools` | 2.83.0 | Compiles the `.proto` contracts in `src/api/` and the module `api/` folders into C# at build time. | Apache-2.0 |
| `JetBrains.Annotations` | 2026.2.0 | Usage and nullability annotations for the ReSharper/Rider inspection engine, which is this team's authoritative one. | MIT |
| `Microsoft.AspNetCore.Hosting.Abstractions` | 2.3.12 | The minimal hosting abstractions the diagram and editor definition `Build` delegates are written against, so a module project never references the whole web stack. | Apache-2.0 (pre-SPDX package; license via its `licenseUrl`, the ASP.NET Core 2.x LICENSE.txt) |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.11 | `WebApplicationFactory` — the in-memory real host every backend integration test runs against. | MIT |
| `Microsoft.Extensions.DependencyModel` | 10.0.11 | `DependencyContext` enumerates the application's assemblies for the reflection scan that discovers `Diagram.Definitions` and `Editor.Definitions`. | MIT |
| `Microsoft.NET.Test.Sdk` | 18.9.0 | The test-project build plumbing that makes each xUnit v3 test project its own executable host. | MIT |
| `Nerdbank.GitVersioning` | 3.10.94 | Computes the version from git height, which is what the release pipeline stamps on every published ZIP. | MIT |
| `Serilog` | 4.4.0 | The logging pipeline behind the house convention of one static `Log.ForContext<T>()` logger per class. | Apache-2.0 |
| `Serilog.AspNetCore` | 10.0.0 | Wires Serilog into the ASP.NET Core host and reads its levels and sinks from the `Serilog` section of `appsettings.json`. | Apache-2.0 |
| `xunit.runner.visualstudio` | 4.0.0 | Lets Rider and VS Test Explorer discover and run the xUnit v3 tests a developer works in daily. | Apache-2.0 |
| `xunit.v3` | 4.0.0 | The test framework for every backend test project, on Microsoft.Testing.Platform per `src/global.json`. | Apache-2.0 |
| `YamlDotNet` | 18.1.0 | Reads YAML with a line number on every node for the ansible-structure, azure-pipeline, timeline, dependency-graph, helm-charts and databricks modules — reading only; no module serialises back through it (the manifest's own comment records the full reasoning). | MIT |

## Client

One row per distinct package across the npm workspace manifests: `src/package.json` and the workspaces it declares — `src/client/package.json` and every `src/diagrams/*/client/package.json`. Both `dependencies` and `devDependencies` count; the version cell carries the manifest's specifier verbatim, and a package whose manifests disagree would show every distinct specifier, sorted and comma-separated.

| Dependency | Version | Reason for usage | License |
|---|---|---|---|
| `@bufbuild/buf` | ^1.47.2 | The buf CLI that `npm run generate` drives to turn the `.proto` contracts into TypeScript. | Apache-2.0 |
| `@bufbuild/protobuf` | ^2.2.3 | The protobuf runtime the generated TypeScript messages run on — which is why every diagram module's client package depends on it too. | (Apache-2.0 AND BSD-3-Clause) |
| `@bufbuild/protoc-gen-es` | ^2.2.3 | The code generator buf invokes to emit those TypeScript messages and service stubs. | Apache-2.0 |
| `@codemirror/lang-markdown` | ^6.5.2 | Markdown syntax support for the markdown editor module's CodeMirror instance. | MIT |
| `@codemirror/language` | ^6.12.4 | CodeMirror's language-infrastructure layer, which the editor modules build their file-type support on. | MIT |
| `@codemirror/state` | ^6.7.2 | CodeMirror's document/state model — what an editor session's text and selection actually live in. | MIT |
| `@codemirror/view` | ^6.43.10 | CodeMirror's DOM view layer: the visible, editable text surface of the editor modules. | MIT |
| `@connectrpc/connect` | ^2.0.1 | The Connect RPC client core the generated service clients call through — shared by the shell and every module client. | Apache-2.0 |
| `@connectrpc/connect-web` | ^2.0.1 | The browser transport binding Connect to gRPC-Web, matching the backend's gRPC-Web middleware. | Apache-2.0 |
| `@mdi/font` | ^7.4.47 | The Material Design icon font behind every `mdi-*` icon name the backend describes in its action, toolbox and property data. | Apache-2.0 |
| `@testing-library/dom` | ^10.4.0 | DOM queries and assertions for the component tests, shared foundation of the React testing utilities. | MIT |
| `@testing-library/react` | ^16.1.0 | Renders components the way a user sees them, so client tests assert behaviour rather than implementation. | MIT |
| `@types/node` | ^26.3.0 | Node type definitions for the Vite/Vitest config and tooling code that runs under Node. | MIT |
| `@types/react` | ^18.3.12 | React type definitions for the typecheck gate. | MIT |
| `@types/react-dom` | ^18.3.1 | React DOM type definitions for the typecheck gate. | MIT |
| `@vitejs/plugin-react` | ^6.1.0 | Teaches Vite React's JSX transform and fast refresh, which is what makes the F5 hot-reload loop work. | MIT |
| `codemirror` | ^6.0.2 | The CodeMirror 6 umbrella package bundling the editor's baseline extensions for the editor modules. | MIT |
| `jsdom` | ^25.0.1 | The DOM implementation Vitest runs the component tests in, since there is no browser in the test gate. | MIT |
| `marked` | ^18.0.11 | Renders markdown to HTML for the markdown editor's preview pane. | MIT |
| `react` | ^18.3.1 | The UI framework of the shell and of every diagram module's canvas — which is why each module client declares it. | MIT |
| `react-dom` | ^18.3.1 | React's DOM renderer, mounting the shell into the page. | MIT |
| `typescript` | ^5.7.2 | The compiler behind the `npm run typecheck` gate and the language everything client-side is written in. | Apache-2.0 |
| `vite` | ^8.2.2 | The dev server the F5 experience proxies to, and the production bundler whose output ASP.NET Core serves. | MIT |
| `vitest` | ^4.1.11 | The client test runner behind the `npm test` gate. | MIT |
