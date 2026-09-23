# Creating an editor module

How to give ADP a purpose-built text editor for a file kind. Editors are the second plugin family, living at [`src/editors/`](../src/editors/) beside `src/diagrams/`, and they plug in through the same mechanism — so this document covers only what is genuinely the editor family's own, and hands everything the families share to [creating a diagram module](creating-a-diagram-module.md). It is traced against the two shipped editors: **markdown** (`src/editors/markdown/`), the worked example of an editor that claims files, and **plain** (`src/editors/plain/`), the fallback that makes "every text file opens somewhere" true.

## What the families share

Read these sections of the diagram walkthrough first; they apply verbatim, with `editors` in place of `diagrams` and `Editor` in place of `Diagram`:

- [What a module is](creating-a-diagram-module.md#what-a-module-is) — the same four folders (`backend/` holding `EtAlii.Adp.Editor.<Editor>` and its `.Tests`, `api/`, `client/`, `examples/`), the same absolute dependency direction. [`src/editors/readme.md`](../src/editors/readme.md) states the layout in the family's own words.
- [The definition](creating-a-diagram-module.md#the-definition) — a static `Editor` class exposing `Definitions`, discovered at startup by `EditorDefinitionDiscovery` ([`src/backend/EtAlii.Adp.Editor/EditorDefinitionDiscovery.cs`](../src/backend/EtAlii.Adp.Editor/EditorDefinitionDiscovery.cs)), the same reflection walk the diagram family uses, feeding `AddEditorDefinitions` which invokes each definition's `Build` delegate. Adding a module edits no core file here either.
- [The client](creating-a-diagram-module.md#the-client) — a `client/register.ts` found by the *same* `import.meta.glob` in [`diagramCanvases.ts`](../src/client/src/shell/panels/diagramCanvases.ts) (its pattern covers `editors/*/client/register.ts` too), matching on the editor's mime.
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

A definition carries no rendering details — no theme, no keymap, no engine options. Those belong to the module's own client code; the definition is what core needs to route.

## The plain fallback

The promise "every text file in your project opens somewhere" is made true by a module, not by a special case in core: [`src/editors/plain/`](../src/editors/plain/) claims **no** extensions and no file names, and declares `IsFallback: true` ([its `Editor.cs`](../src/editors/plain/backend/EtAlii.Adp.Editor.Plain/Editor.cs)). The resolver falls back to it only when nothing else matched — last resort by construction, not by registration order. Your module never competes with it: claim your extensions, and everything you do not claim still opens as plain text.

## Resolution, and what happens when two editors claim one extension

Core resolves a file's editor through one resolver that knows no extension of its own: [`EditorResolver.cs`](../src/backend/EtAlii.Adp.Hierarchy/EditorResolver.cs). The order is: exact file-name claim, then extension, then the fallback.

Two editors claiming the same extension (or file name) *unintentionally* is a deployment error, **reported at startup** — once, at `Error` level, naming the editors and the claim — and every affected file then refuses to open with the conflict rather than one editor silently winning. Deliberately *not* a startup throw: the resolver's own remarks record why (an optional, additive family must not take the whole host down, diagrams included, over its own misconfiguration), which is a softer failure than the diagram family's two-validators-one-origin throw, chosen after comparing both precedents. Behaviour depending on assembly load order is the thing being prevented either way.

## The session

The backend seam is [`IEditorSessionFactory`](../src/backend/EtAlii.Adp.Editor/IEditorSessionFactory.cs) / [`IEditorSession`](../src/backend/EtAlii.Adp.Editor/IEditorSession.cs), resolved by editor id through `EditorSessionFactories` — the editor family's mirror of the diagram family's session factories. Markdown's [`MarkdownEditorSessionFactory.cs`](../src/editors/markdown/backend/EtAlii.Adp.Editor.Markdown/MarkdownEditorSessionFactory.cs) and [`MarkdownEditorSession.cs`](../src/editors/markdown/backend/EtAlii.Adp.Editor.Markdown/MarkdownEditorSession.cs) are the worked example; plain's session is the same contract at its simplest.

What a session owes is the family's whole editing contract: content served to the client, external changes reaching the open editor, and the file's shape preserved — encoding, BOM, line endings, the presence or absence of a final newline. The shared [`TextFileBuffer`](../src/backend/EtAlii.Adp.Editor/TextFileBuffer.cs) in `EtAlii.Adp.Editor` owns that shape-preserving read/write, so a module does not re-derive it; plain's examples (`crlf-notes.txt`, `utf8-bom-notes.txt`) exist to pin exactly those properties. Saving is not your session's job to make undoable: core's service dispatches one shared `SaveTextFileCommand` through the history for every editor alike, and its handler opens its own `TextFileBuffer` on the path and writes there — the command layer is family-wide, not per module. `IEditorSession` still requires a `SaveAsync`, and both shipped sessions implement it over their buffer, but nothing outside tests calls it today.

## The client panel

Markdown's client is real and registered — [`register.ts`](../src/editors/markdown/client/register.ts) matches `editor/markdown` and mounts [`MarkdownEditorPanel.tsx`](../src/editors/markdown/client/MarkdownEditorPanel.tsx), the CodeMirror-based editor with the preview that makes markdown worth its own module (`marked` renders it; the CodeMirror packages are inventoried in [dependencies.md](dependencies.md)). Plain's [`PlainEditorPanel.tsx`](../src/editors/plain/client/PlainEditorPanel.tsx) is the same shape without the preview. An editor module's client folder needs no `package.json` today: the npm workspaces in `src/package.json` cover `client` and `diagrams/*/client`, and the editor panels' imports resolve through the workspace root — add `editors/*/client` to the workspaces the day an editor panel needs a dependency of its own.

## The checklist

For a new editor `foo`:

1. `src/editors/foo/backend/EtAlii.Adp.Editor.Foo` (+ `.Tests`, `<OutputType>Exe</OutputType>`), both added to `EtAlii.Adp.slnx`.
2. `Editor.cs` with the definition: id, title, description, icon, claims, `Build` registering your `IEditorSessionFactory`.
3. The session, on `TextFileBuffer`, with tests pinning round-trip shape (copy plain's fixtures approach).
4. `client/register.ts` matching `editor/foo`, and the panel it mounts.
5. `examples/` with files the editor is for — seeded into `src/examples/editors/foo/` so the editor appears in the combined showcase project; the two copies are not held in sync afterwards.
6. No core edits, no shell edits, no catalog row — the diagram catalog tracks diagram types; editors are listed by `src/editors/readme.md`'s own folder.
