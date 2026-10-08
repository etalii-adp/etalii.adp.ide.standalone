# Creating a designer module

How to give ADP a designer - the third family of tools, beside [diagrams](creating-a-diagram-module.md) and [editors](creating-an-editor-module.md). **No designer exists yet**: this document is the placeholder that says where one goes and what it can count on, so the first designer module starts from a prepared place rather than from a copy of a diagram module.

## What a designer is

A designer is a form-based visual layout: more than text input, and not a diagram, because nothing in it is connected. Apply the tests of [ADP terminology](https://github.com/etalii-adp/etalii.adp/blob/develop/docs/terminology.md) in order: when typing text is the main thing the user does, it is an editor; when its elements are connected, it is a diagram; when it is laid out visually and filled in, with nothing connected, it is a designer. Every tool ADP offers today is a diagram or an editor ([`docs/tools.md`](tools.md)), so a new designer is also a new row there, of kind Designer.

## What is already prepared

- **The folder**: [`src/designers`](../src/designers), beside `src/diagrams` and `src/editors`, holding only its readme.
- **Discovery and registration**: [`DesignerDefinitionDiscovery`](../src/backend/EtAlii.Adp.Designer/DesignerDefinitionDiscovery.cs) scans the application's assemblies for a static `Designer` class with a public static `Definitions` property holding [`DesignerDefinition`](../src/backend/EtAlii.Adp.Designer/_Model/DesignerDefinition.cs)s - the same scan, `ToolDefinitionScan`, that finds `Diagram` and `Editor` classes. The host runs it at startup, today finds none, and registers what it finds with [`AddDesignerDefinitions`](../src/backend/EtAlii.Adp.Designer/_Model/HostApplicationBuilder.AddDesignerDefinitions.cs): each definition's `Build` runs, and the definitions are served through [`IDesignerDefinitionCatalog`](../src/backend/EtAlii.Adp.Designer/IDesignerDefinitionCatalog.cs).
- **The definition**: a `DesignerDefinition` declares its origin (`<vendor>/<type>`), title, description, icon, the file formats a document can be created in ([`DesignerFormat`](../src/backend/EtAlii.Adp.Designer/_Model/DesignerFormat.cs): a name and an extension, chosen when the document is added) and `Build`. These live in the core project `EtAlii.Adp.Designer`, beside `EtAlii.Adp.Diagram` and `EtAlii.Adp.Editor`.
- **The client registry**: the shell's [`toolPanels.ts`](../src/client/src/shell/panels/toolPanels.ts) globs `{diagrams,designers,editors}/*/client/register.ts`, so a designer's `register.ts` is picked up with no shell edit, exactly as the other families' are.

## What the family will share

Read [creating a diagram module](creating-a-diagram-module.md) for the mechanism every family uses; a designer module follows it with `designers` in place of `diagrams` and `Designer` in place of `Diagram`:

- the same four folders - `backend/` (`EtAlii.Adp.Designer.<Designer>` and its `.Tests`), `api/`, `client/`, `examples/` - and the same dependency direction: a module depends on core, never the other way round;
- a static `Designer` class exposing `Definitions`, whose origin (`<vendor>/<type>`) names the designer type in its `.adp` registration's first line;
- a `client/register.ts` exporting `registrations` of `ToolPanelRegistration`, whose `Panel` is the designer's canvas;
- xUnit v3 test projects added to `EtAlii.Adp.slnx`, and examples seeded into the showcase under `src/examples`, in a `designers` folder beside `diagrams` and `editors`.

## What the first designer adds

What a designer needs beyond the scan is decided by its specification, the way `modular-text-editors` decided the editor family's: the routing and session seams the host serves designers through (the definition and its catalog are in place), and - when ADP specifies designer types in a language - DESL, the Designer Specification Language, with the designs users create stored as DED, the Designer Definition Language. Extend this document in the same change.
