# Findings

Defects and observations surfaced by the `developer-sign-in-bypass` task-4 pass — running the sign-in-blocked `tests.md` checks against a developer build. Routed here for the Scrum master to dispatch to the owning module.

## FLAG — wardley-map: a canvas element selection never fills the property grid

**Severity: functional defect. Owner: wardley-map module. Found: 2026-09-05, by running the app.**

Clicking a component on a Wardley map does **not** fill the Property Grid. The selection resolves to the `.adp` file, never the element — the backend logs `Selected …/tea.adp … as Activate` and `Described 0 properties for …/tea.adp`, so the heading stays the file and the element's Identity / Position / Strategy rows never appear.

**Root cause, confirmed in source.** `src/diagrams/wardley-map/client/WardleyCanvas.tsx` reports no element context-selection at all. The working modules do — causal-loop wires `onSelect → select(elementSelectionOf(entryId, path, id, gesture))` to each element's `onClick` and imports `useContextSelection` / `useElementContextMenu`. `WardleyCanvas` has none of these; its element `<g>` carries only an `onMouseDown` drag handler, and a comment in it admits the `.adp` entry *"a selection reports as its outer level … task 19's context resolver needs and nothing here does."* The backend `WardleyContextSourceResolver.cs` and `WardleyContextPropertyProvider` exist and are unit-tested, but the client never hands them an element, so `WardleyMapFlowTests` passing (backend flow) does not cover this client gap.

**This is the same class of bug as the already-fixed causal-loop "context surface is reachable" defect** — a property grid that never populates because element-selection wiring is missing on the client.

**Fix:** wire element-selection reporting into `WardleyCanvas` (report the clicked element as the innermost context selection, using `entryId`, the way causal-loop does), and leave a guard behind — a test that fails before the wiring and passes after. Reproduction and evidence are in the `tests.md` entry *"An element selected on the canvas fills the property grid (wardley-map, task 26)"*, marked **FAILED**.

## Byte-fidelity observations (lower severity)

- **wardley-map writer strips a leading UTF-8 BOM.** Dragging a component and saving `tea.owm` produced the correct one-line position change but also removed the file's leading BOM (`-﻿title` / `+title`); Undo reverted the position but not the BOM (write-side normalization).
- **dependency-graph remove+undo re-orders the file.** Removing a dependency and undoing restored the relation at the **end** of the `relations:` list rather than its original position — the canvas is right, the bytes are not.
- **Both wardley and dependency-graph leave an `*.identities.json` sidecar** in the project folder after an edit; ADP's own file, but it appears beside the user's document.
