# Creating an editor module

How to give ADP a purpose-built text editor for a file kind. Editors are the second plugin family, living at [`src/editors/`](../src/editors/) beside `src/diagrams/`, and they plug in through the same mechanism — so this document covers only what is genuinely the editor family's own, and hands everything the families share to [creating a diagram module](creating-a-diagram-module.md). It is traced against the shipped editors: **markdown** (`src/editors/markdown/`), the worked example of an editor that claims files, **plain** (`src/editors/plain/`), the fallback that makes "every text file opens somewhere" true, and **yaml** (`src/editors/yaml/`), the worked example of an analysis built on a third-party language service.

## What the families share

Read these sections of the diagram walkthrough first; they apply verbatim, with `editors` in place of `diagrams` and `Editor` in place of `Diagram`:

- [What a module is](creating-a-diagram-module.md#what-a-module-is) — the same four folders (`backend/` holding `EtAlii.Adp.Editor.<Editor>` and its `.Tests`, `api/`, `client/`, `examples/`), the same absolute dependency direction. [`src/editors/readme.md`](../src/editors/readme.md) states the layout in the family's own words.
- [The definition](creating-a-diagram-module.md#the-definition) — a static `Editor` class exposing `Definitions`, discovered at startup by `EditorDefinitionDiscovery` ([`src/backend/EtAlii.Adp.Editor/EditorDefinitionDiscovery.cs`](../src/backend/EtAlii.Adp.Editor/EditorDefinitionDiscovery.cs)), the same reflection walk the diagram family uses, feeding `AddEditorDefinitions` which invokes each definition's `Build` delegate. Adding a module edits no core file here either.
- [The client](creating-a-diagram-module.md#the-client) — a `client/register.ts` found by the *same* `import.meta.glob` in [`toolPanels.ts`](../src/client/src/shell/panels/toolPanels.ts) (its pattern covers `editors/*/client/register.ts` too), matching on the editor's mime.
- [Tests and examples](creating-a-diagram-module.md#tests-fixtures-and-examples) — xUnit v3 executable test projects, the CLAUDE.md test command, and `examples/` seeded into `src/examples/`. Unlike a diagram module's, an editor's two copies **are** held in sync: each editor's own `Examples.Tests` opens every example in the editor, requires a byte-identical round trip, and requires a byte-identical replica under `src/examples/editors/<editor>/`. Editor examples carry no `.adp` registrations, so the diagram modules' registration walk has nothing to check for them.

The one shared piece with a family twist: an editor module's spec still comes first, and the family's own requirements were written as the `modular-text-editors` specification — the analogue of tech.md's diagram-type checklist. That specification was archived and has since been removed from the working tree; its text is still in history, readable with `git show d573ad87^:.spec-workflow/archive/specs/modular-text-editors/requirements.md`.

## The definition, and what it claims

An editor's identity is a static `Editor` class — markdown's is [`Editor.cs`](../src/editors/markdown/backend/EtAlii.Adp.Editor.Markdown/Editor.cs) — carrying an `EditorDefinition` ([the record](../src/backend/EtAlii.Adp.Editor/_Model/EditorDefinition.cs)):

- **`Id`** — the stable editor id (`"markdown"`, `"plain"`). The client-facing mime is `editor/<id>`, which is what markdown's `register.ts` matches.
- **`Extensions`** — the file extensions it serves, dot included; compared case-insensitively (the record lower-cases them at construction). Markdown claims `[".md", ".markdown"]`.
- **`FileNames`** — exact-name claims for extensionless files (`Makefile`, `Dockerfile`, `LICENSE`). Markdown claims none.
- **`IsFallback`** — see plain, below.
- **`IsDefaultForSharedExtension`** — the opt-in tie-breaker for the one *legitimate* shared-extension case: when a raw editor and a format-aware one deliberately share an extension, exactly one declares itself the default and the other stays reachable through "Open with…". Neither shipped editor needs it.
- **`Build`** — registers the module's services; markdown's is a single line adding its `IEditorSessionFactory`.

The class carries `[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]`. Discovery finds it by reflection and nothing calls it by name, so without the attribute Rider reports it as unused. `JetBrains.Annotations` is referenced for every project in `src/Directory.Build.props`, so the module adds only the `using`:

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Editor.Foo;

/// <summary>This editor's identity: Foo, by extension.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Editor
{
    public static EditorDefinition Foo { get; } = new(
        "foo",
        "Foo",
        "What a foo file is, and why it is worth its own editor.",
        Icon: "mdi-file-document-outline",
        Extensions: [".foo"],
        FileNames: [],
        Build: builder => builder.Services.AddSingleton<IEditorSessionFactory, FooEditorSessionFactory>());

    public static EditorDefinition[] Definitions { get; } = [Foo];
}
```

A definition carries no rendering details — no theme, no keymap, no engine options. Those belong to the module's own client code; the definition is what core needs to route.

## The plain fallback

The promise "every text file in your project opens somewhere" is made true by a module, not by a special case in core: [`src/editors/plain/`](../src/editors/plain/) claims **no** extensions and no file names, and declares `IsFallback: true` ([its `Editor.cs`](../src/editors/plain/backend/EtAlii.Adp.Editor.Plain/Editor.cs)). The resolver falls back to it only when nothing else matched — last resort by construction, not by registration order. Your module never competes with it: claim your extensions, and everything you do not claim still opens as plain text.

## Resolution, and what happens when two editors claim one extension

Core resolves a file's editor through one resolver that knows no extension of its own: [`EditorResolver.cs`](../src/backend/EtAlii.Adp.Hierarchy/EditorResolver.cs). The order is: exact file-name claim, then extension, then the fallback.

Two editors claiming the same extension (or file name) *unintentionally* is a deployment error, **reported at startup** — once, at `Error` level, naming the editors and the claim — and every affected file then refuses to open with the conflict rather than one editor silently winning. Deliberately *not* a startup throw: the resolver's own remarks record why (an optional, additive family must not take the whole host down, diagrams included, over its own misconfiguration), which is a softer failure than the diagram family's two-validators-one-origin throw, chosen after comparing both precedents. Behaviour depending on assembly load order is the thing being prevented either way.

## The session

The backend seam is [`IEditorSessionFactory`](../src/backend/EtAlii.Adp.Editor/IEditorSessionFactory.cs) / [`IEditorSession`](../src/backend/EtAlii.Adp.Editor/IEditorSession.cs), resolved by editor id through `EditorSessionFactories` — the editor family's mirror of the diagram family's session factories. A text-first editor needs no session of its own: [`TextEditorSession`](../src/backend/EtAlii.Adp.Editor/TextEditorSession.cs) reads the file as text, watches it for other programs' changes and retries a read the other program has not finished. Plain's [`PlainEditorSessionFactory.cs`](../src/editors/plain/backend/EtAlii.Adp.Editor.Plain/PlainEditorSessionFactory.cs) hands it out as it is; markdown's [`MarkdownEditorSession.cs`](../src/editors/markdown/backend/EtAlii.Adp.Editor.Markdown/MarkdownEditorSession.cs) is a thin variant of it, there only so its records say markdown, and [`MarkdownEditorSessionFactory.cs`](../src/editors/markdown/backend/EtAlii.Adp.Editor.Markdown/MarkdownEditorSessionFactory.cs) is the worked example of a factory. The JSON, XML and YAML editors' sessions are thin variants too; write a session of your own only when an editor needs more from the backend than its file's text.

What a session owes is the family's whole editing contract: content served to the client, external changes reaching the open editor, and the file's shape preserved — encoding, BOM, line endings, the presence or absence of a final newline. The shared [`TextFileBuffer`](../src/backend/EtAlii.Adp.Editor/TextFileBuffer.cs) in `EtAlii.Adp.Editor` owns that shape-preserving read/write, so a module does not re-derive it; plain's examples (`crlf-notes.txt`, `utf8-bom-notes.txt`) exist to pin exactly those properties. Saving is not your session's job at all: core's service dispatches one shared `SaveTextFileCommand` through the history for every editor alike, and its handler opens its own `TextFileBuffer` on the path and writes there — the command layer is family-wide, not per module. A session has no save of its own, so it hears the write as a change on disk, which is how the open text view learns what was saved.

## The client panel

Markdown's client is real and registered — [`register.ts`](../src/editors/markdown/client/register.ts) matches `editor/markdown` and mounts [`MarkdownEditorPanel.tsx`](../src/editors/markdown/client/MarkdownEditorPanel.tsx), the CodeMirror-based editor with the preview that makes markdown worth its own module (`marked` renders it; the CodeMirror packages are inventoried in [dependencies.md](dependencies.md)). Plain's [`PlainEditorPanel.tsx`](../src/editors/plain/client/PlainEditorPanel.tsx) is the same shape without the preview. `editors/*/client` is one of the npm workspaces in `src/package.json`, so a module whose panel needs a dependency of its own gives its client folder a `package.json` declaring it, and `DependencyInventoryTests` then wants its row in [dependencies.md](dependencies.md). The JSON editor is that case: [`src/editors/json/client/package.json`](../src/editors/json/client/package.json) declares its grammar and Microsoft's JSON language service. A module that needs nothing of its own, as markdown and plain, has no `package.json`: npm skips a matched folder without one. The client's Vitest run and typecheck include `editors/*/client`, so a module's tests sit beside its panel.

Every module's `register.ts`, and so every panel it imports, is in the shell's own bundle. A heavy dependency is therefore loaded with `import()` the first time it is needed, from a file the panel does not import statically: yaml's [`yamlAnalysis.ts`](../src/editors/yaml/client/yamlAnalysis.ts) loads Red Hat's YAML language service that way, and the build puts it in a chunk of its own.

A dependency written for Node may import a module of Node, which Vite leaves out of a browser build: the build then succeeds and the first call into the missing module throws in the browser. The one such module there is, `path` for the YAML language service, is replaced by `path-browserify` through one alias in [`src/client/vite.config.ts`](../src/client/vite.config.ts), the one shared file an editor module has touched. [`viteBrowserExternals.test.ts`](../src/client/src/viteBrowserExternals.test.ts) builds that service's chunk and fails when any module of Node is left out of it; a new dependency of the same kind needs its own alias there, and a case in that test.


## The shared editor pieces

Five pieces in [`src/client/src/editors/`](../src/client/src/editors/) give every editor the same colour, findings, completion and schema lookup (json-editor Requirement 9). A module brings what its format is — a grammar, an analysis — and uses these for the rest; none of them names a format.

- **Colour** — [`editorHighlight.ts`](../src/client/src/editors/editorHighlight.ts) maps Lezer's highlight tags to nine theme tokens, `--color-syntax-name`, `-string`, `-number`, `-keyword`, `-comment`, `-punctuation`, `-tag`, `-attribute` and `-invalid`, declared for both themes in [`index.css`](../src/client/src/index.css) and held to text contrast on the editor's surface by [`theme.contrast.test.ts`](../src/client/src/theme.contrast.test.ts). [`BaseTextEditor`](../src/client/src/editors/BaseTextEditor.tsx) installs it, with bracket matching and the fold gutter, for every editor: a module passes its grammar in `extensions` and writes no colour.
- **Findings** — a module hands `TextEditorPanel` an `analysis`, an [`EditorAnalysis`](../src/client/src/editors/editorAnalysis.ts) whose `findings(text)` answers `EditorFinding`s: a range, a severity (`error`, `warning`, `info`), a sentence and a source. [`analysisExtensions.ts`](../src/client/src/editors/analysisExtensions.ts) runs it after a pause in typing and shows each finding as an underline whose line style differs per severity, a gutter mark and its sentence on hover, on F8 and in the findings panel (Ctrl+Shift+M); the status line counts errors and warnings and then shows the analysis's own `status(text)`. An answer for a text the view no longer shows is dropped. No module draws its own underline.
- **Completion and hover text** — the same analysis's optional `completions(text, offset, explicit)` and `hover(text, offset)` answer `EditorCompletion`s and an `EditorHover`. The list opens on typing and on Ctrl+Space and is walked, accepted and dismissed from the keyboard; an item's `info` is shown beside it; an insert holding `${1}` tab stops is applied as a snippet; hover text shows under the pointer and on Ctrl+K Ctrl+I. Descriptions and hover text are always set as text, never as markup.
- **Where a schema comes from** — [`resolveSchemaReference`](../src/client/src/editors/schemaReference.ts) decides a reference a document holds (a `$schema`, a `$ref`, a schema location) against the document's path: a file of the project, a schema ADP bundles, or none with the reason said to the user. A reference above the project root is refused, never clamped, and a web address is never fetched. The cases are [`src/fixtures/schema-reference-cases.json`](../src/fixtures/schema-reference-cases.json), which a module implementing the rule in C# is to be held to as well.
- **Reading a schema** — [`SchemaFiles`](../src/client/src/editors/SchemaFiles.tsx) renders nothing: given project paths, it opens each over the connection's existing stream, as a text tab opens its own file, and reports each file's text and revision, or why it could not be read, as they arrive and change. When a schema changes, the module bumps `TextEditorPanel`'s `analysisRevision` and the text is analysed again without an edit. No backend call and no stream is added for it.

The JSON editor is the worked example of all five. [`JsonEditorPanel.tsx`](../src/editors/json/client/JsonEditorPanel.tsx) passes the JSON grammar and an analysis, and mounts `SchemaFiles` for the paths that analysis asks for; [`jsonAnalysis.ts`](../src/editors/json/client/jsonAnalysis.ts) is the only file that knows the language service, and loads it with `import()`, so the service is a chunk of its own that the shell's bundle does not carry. A library only the analysis needs belongs behind such an `import()`.

## Asking the backend about the text

A format whose checking belongs in the backend, as XML's schemas do, answers the family's two analysis calls rather than adding a service of its own (xml-editor ruling A1). [`EditorService`](../src/backend/EtAlii.Adp.Diagram/EditorService.cs) has `Analyze` and `Complete`, unary calls that carry the text as typed and, for completion, the offset; they name no format. A module answers them by implementing [`IEditorAnalyzer`](../src/backend/EtAlii.Adp.Editor/IEditorAnalyzer.cs) for its editor id and registering it in `Build` as an `IEditorAnalyzer`; [`EditorAnalyzers`](../src/backend/EtAlii.Adp.Diagram/EditorAnalyzers.cs) finds it by the tab's editor id. The service refuses a text over `MaximumAnalyzedLength`, a path outside the project and an editor with no analyser before a module is asked, logs an analyser's exception once with the path and never the text, and keeps only the `DependsOn` files inside the project, as project paths. Offsets are into the editor's LF-only text. The XML editor's [`XmlAnalyzer`](../src/editors/xml/backend/EtAlii.Adp.Editor.Xml/XmlAnalyzer.cs) is the worked example, and its client's [`xmlAnalysis.ts`](../src/editors/xml/client/xmlAnalysis.ts) the `EditorAnalysis` that asks it. A format checked in the client, as JSON is, needs none of this.

## The checklist

For a new editor `foo`:

1. `src/editors/foo/backend/EtAlii.Adp.Editor.Foo` (+ `.Tests`, `<OutputType>Exe</OutputType>`), both added to `EtAlii.Adp.slnx`.
2. `Editor.cs` with the definition: id, title, description, icon, claims, `Build` registering your `IEditorSessionFactory`, and `[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]` on the class.
3. The session, on `TextFileBuffer`, with tests pinning round-trip shape (copy plain's fixtures approach).
4. `client/register.ts` matching `editor/foo`, and the panel it mounts: `TextEditorPanel` with the format's grammar, and an `analysis` if the format has findings or completion (see *The shared editor pieces*, and *Asking the backend about the text* when the backend answers it). A dependency of the panel's own goes in `client/package.json`, with its row in `docs/dependencies.md`. A heavy one is loaded with `import()`; one that imports a module of Node needs an alias in `src/client/vite.config.ts` (see *The client panel*).
5. `examples/` with files the editor is for, and a byte-identical replica under `src/examples/editors/foo/` so the editor appears in the combined showcase project; the module's `Examples.Tests` opens every example, saves it unchanged and holds the replica to the same bytes. Examples whose line endings are the point (a CRLF file beside LF ones) get a `-text` line in `.gitattributes`, scoped to the module's examples and the replica by extension, as the JSON editor's are.
6. No core edits, no shell edits beyond such an alias; one catalog row — `docs/tools.md` lists every tool type, editors included, with the Kind `Editor` and the origin `editor/foo`.
