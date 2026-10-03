# Findings

Defects and observations surfaced by the `developer-sign-in-bypass` task-4 pass — running the sign-in-blocked `tests.md` checks against a developer build. Routed here for the Scrum master to dispatch to the owning module.

## FLAG — wardley-map: a canvas element selection never fills the property grid

**Severity: functional defect. Owner: wardley-map module. Found: 2026-09-05, by running the app.**

Clicking a component on a Wardley map does **not** fill the Property Grid. The selection resolves to the `.adp` file, never the element — the backend logs `Selected …/tea.adp … as Activate` and `Described 0 properties for …/tea.adp`, so the heading stays the file and the element's Identity / Position / Strategy rows never appear.

**Root cause, confirmed in source.** `src/diagrams/wardley-map/client/WardleyCanvas.tsx` reports no element context-selection at all. The working modules do — causal-loop wires `onSelect → select(elementSelectionOf(entryId, path, id, gesture))` to each element's `onClick` and imports `useContextSelection` / `useElementContextMenu`. `WardleyCanvas` has none of these; its element `<g>` carries only an `onMouseDown` drag handler, and a comment in it admits the `.adp` entry *"a selection reports as its outer level … task 19's context resolver needs and nothing here does."* The backend `WardleyContextSourceResolver.cs` and `WardleyContextPropertyProvider` exist and are unit-tested, but the client never hands them an element, so `WardleyMapFlowTests` passing (backend flow) does not cover this client gap.

**This is the same class of bug as the already-fixed causal-loop "context surface is reachable" defect** — a property grid that never populates because element-selection wiring is missing on the client.

**Fix:** wire element-selection reporting into `WardleyCanvas` (report the clicked element as the innermost context selection, using `entryId`, the way causal-loop does), and leave a guard behind — a test that fails before the wiring and passes after. Reproduction and evidence are in the `tests.md` entry *"An element selected on the canvas fills the property grid (wardley-map, task 26)"*, marked **FAILED**.

> **Does not reproduce as of 2026-09-06 — fixed, and this flag should not be dispatched (Tester 2).**
> Confirmed two ways on a developer build at the reserved ports, against `diagrams/wardley-map/example 1/tea.owm`.
>
> **In the running app**: clicking the `Cup of Tea` component fills the Property Grid with the element's own rows —
> a `Cup of Tea` heading below the `tea.adp` file section, carrying `Identity` (`Name`, `Kind: component` with its
> read-only reason) and `Position` (`Visibility`, `Maturity`, `Evolution stage`). Those are precisely the rows this
> flag says never appear. The ribbon also gains the element actions — Evolve to…, Mark as having inertia, Mark as
> market, Mark as ecosystem, Mark as build — which only resolve for an element selection.
>
> **In the source**: the root cause stated here no longer holds. `WardleyCanvas.tsx` now imports and calls
> `elementSelectionOf`, reporting the clicked element at line 403 and again for the context menu at 471. The fix is
> `1d33255d`, *"wardley-map becomes selectable, so its backend action…"*, committed 2026-09-05 at 21:02 — **after**
> this flag was written the same day. The flag was accurate when filed and was overtaken within hours.
>
> The lower-severity byte-fidelity observations below were **not** re-checked and are not covered by this note.

## Byte-fidelity observations (lower severity)

- **wardley-map writer strips a leading UTF-8 BOM.** Dragging a component and saving `tea.owm` produced the correct one-line position change but also removed the file's leading BOM (`-﻿title` / `+title`); Undo reverted the position but not the BOM (write-side normalization).
- **dependency-graph remove+undo re-orders the file.** Removing a dependency and undoing restored the relation at the **end** of the `relations:` list rather than its original position — the canvas is right, the bytes are not.
- **Both wardley and dependency-graph leave an `*.identities.json` sidecar** in the project folder after an edit; ADP's own file, but it appears beside the user's document.
## Observations from the closing pass (2026-09-06)

- **No vendored SHACL corpus contains a `sh:sparql` constraint.** `sh:sparql` and `sh:select`
  appear nowhere under `src/examples/diagrams/shacl/`; the only mention of SPARQL in either
  corpus is an `rdfs:comment` in `shacl-shacl.ttl`. So the manual check named for that card
  cannot be run against the example data at all — not a module defect, and now stated in
  `src/examples/diagrams/shacl/w3c-shacl/readme.md`. Vendoring a shapes file that carries one
  is the way to demonstrate the opaque badged row.
- **A canvas *background* right-click shows one entry, "Validate all"** — the shell's
  project-wide *document* validation (`ValidateAllContextActionProvider`, scope
  `ProblemsPanel`). The causal-loop canvas shows the identical single entry, so this is not the
  SHACL module offering to validate data, and it is **not** raised as a defect. It is recorded
  because the word appears on a shapes canvas, which is exactly the impression that check
  exists to police, and because the module's own background action (*Add node shape here…*,
  `ShaclActions.cs:69`) did not appear either — on causal-loop as much as on shacl. Whoever
  owns canvas-background context resolution may want to look at that second half.
- **`tests.md` credited `C4InteropTests` with passing in a full-suite gate; the suite skips all
  43 of its cases** for want of a Structurizr CLI. A green run prints nothing about a skipped
  test, so the claim survived a gate that could never contradict it. Corrected in place.
