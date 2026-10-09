# Designers

The designer module folder: one folder per designer type, beside [`src/diagrams/`](../diagrams/) and [`src/editors/`](../editors/). The first is [`knowledge/`](knowledge/), the Knowledge designer: a table of rows and typed properties, with views of it, kept in one plain YAML, JSON or XML file.

A designer is the form-based kind of tool: a visual layout that is filled in rather than typed, with nothing in it connected. A layout whose elements are connected is a diagram, and one where typing text is the main thing the user does is an editor ([ADP terminology](https://github.com/etalii-adp/etalii.adp/blob/develop/docs/terminology.md)).

A designer takes the layout its siblings use - `definition/` (its specification and bindings, bundled from etalii-adp/etalii.adp with their checksums), `backend/` (`EtAlii.Adp.Designer.<Designer>` and its `.Tests`), `client/`, `examples/` - and plugs in through the places prepared for it: a static `Designer` class with `Definitions`, found by `DesignerDefinitionDiscovery`, and a `client/register.ts` found by the shell's registry in [`toolPanels.ts`](../client/src/shell/panels/toolPanels.ts). [Creating a designer module](../../docs/creating-a-designer-module.md) says what the host gives a designer and what a module supplies.
