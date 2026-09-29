# Designers

The designer module folder: one folder per designer type, beside [`src/diagrams/`](../diagrams/) and [`src/editors/`](../editors/). **It is empty** - ADP has no designer yet - and this file is what keeps the folder in a clone.

A designer is the form-based kind of tool: a visual layout that is filled in rather than typed, with nothing in it connected. A layout whose elements are connected is a diagram, and one where typing text is the main thing the user does is an editor ([ADP terminology](https://github.com/etalii-adp/etalii.adp/blob/develop/docs/terminology.md)).

When the first designer arrives it takes the layout its siblings use - `backend/` (`EtAlii.Adp.Designer.<Designer>` and its `.Tests`), `api/`, `client/`, `examples/` - and plugs in through the places already prepared for it: a static `Designer` class with `Definitions`, found by `DesignerDefinitionDiscovery`, and a `client/register.ts` found by the shell's registry in [`toolPanels.ts`](../client/src/shell/panels/toolPanels.ts). [Creating a designer module](../../docs/creating-a-designer-module.md) says what exists and what the family will share.
