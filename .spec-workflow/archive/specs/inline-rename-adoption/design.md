# Design Document

## Overview

Nothing central is built here. `InlineLabelEditor`, `InlineLabelPlacementContext`, the `ShellPromptHost` deferral and `noPrivateLabelEditors` already exist and already serve every canvas; a module adopts by marking the prompts whose value is the label it draws and registering a placement resolver.

**The one genuinely new idea is that there are only three placement shapes in the entire tree**, and two of them are already written. Measured across the fourteen canvases: every label is either centred in the element's own box, or a named line at a known offset inside a composite box, or a bare text at a connection's midpoint. So "make it generic and reusable" is answered by three small pure helpers in the shared library rather than by fourteen bespoke resolvers — and an adopter's placement resolver becomes two or three lines that name a helper and pass its own geometry.

The rest of this document is ordering: which module goes first and why, which prerequisite is a task of its own, and which exclusion an implementer will try to reopen.

## Steering Document Alignment

### Technical Standards (tech.md)

* **Context** — a module offers what a user can do through its action provider; this adds a presentation for one kind of answer, never a second path to the backend. No adopter calls anything the dialog does not already call.
* **Consumers are pure subscribers** — a canvas reads the pushed prompt and reports geometry. It does not ask what the running action is, and it does not interpret an action id.

### Project Structure (structure.md)

* **Core vs module** — the editor, the registry and the three placement helpers are core. Which labels are renameable is each module's own knowledge and stays in its provider.
* **Dependency direction** — a helper takes plain numbers and a string. Nothing in `src/client/src/canvas/` learns what a task, a stage or a span is.

## Code Reuse Analysis

### Existing components to leverage

| Piece | Where | Used how |
| --- | --- | --- |
| `InlineLabelEditor` | `canvas/label/` | Unchanged. Every adopter renders it; none configures it. |
| `InlineLabelPlacementContext` | `shell/panels/` | Unchanged. Adopters call `useRegisterInlineLabelPlacement`. |
| `ShellPromptHost` deferral | `shell/context/` | Unchanged. Already stands down for any marked, placeable prompt. |
| `ContextInputRequest.InlineLabelElementId` | backend `Context/_Model` | Trailing and defaulted; a module adopts by adding one argument to one call. |
| `noPrivateLabelEditors.test.ts` | `canvas/label/` | Unchanged, and it already covers every module added later. |
| `canvas-connection-hit` | `canvas/canvas.css` | The shared grab width, for any connection that needs selecting. |

### What is *not* being built

No new component, no new context, no proto change, no backend mechanism. If an adopter appears to need one, Requirement 2.2 makes that a stop-and-report.

## Architecture

### The three placement shapes

```ts
// canvas/label/labelPlacement.ts - pure, no React, no module knowledge
centredLabelPlacement(box, text)            // the whole box is the label
insetLabelPlacement(box, top, height, text) // one named line inside a composite box
midpointLabelPlacement(from, to, dy, text)  // a bare text on a connection
```

Measured against what the canvases actually draw:

| Shape | Drawn by | Modules |
| --- | --- | --- |
| Centred in the element's box | `SpanElement`, `BoxElement` | timeline, dependency-graph, databricks, azure-pipeline |
| A named line inside a composite | `StyledBoxElement` | c4 (already written, becomes the helper's first caller) |
| A connection's midpoint | `InteractiveBezierConnection`, `StraightConnection` | timeline, dependency-graph, c4 (already written) |

C4's two private helpers — `nodeNamePlacement` and `relationshipLabelPlacement` — are the prototypes for the second and third, and moving them is the first task's real work: the extraction is proven when C4's existing tests pass unchanged.

**The measured-width fallback moves with the third helper.** A connection label has no box, so its width is measured with `getBBox()` where the browser can measure it and estimated per character otherwise. jsdom implements no `getBBox`, so unit tests exercise the estimate by construction, and that stays true wherever the helper is called.

### The ordering, and why it is not the obvious one

**timeline and dependency-graph first, because nothing is missing there.** Both draw with the *same two* shared components — `SpanElement` for the element and `InteractiveBezierConnection` for the relation — both forward `F2`, both select elements, and `InteractiveBezierConnection` already carries `onSelect` and a hit path. Their providers already offer `Rename` on the element's own `Label` and `Relabel` on the relation's own `Label`. So the first adopter needs no prerequisite, and the second is close to a copy of it: two modules, one pattern, both covering elements **and** connections.

**The RDF family looks most ready and is the clearest exclusion in the specification.** All four readings forward `F2`, all four have selectable resources, all four have a `Rename resource` action — everything an implementer scans for. And all four edit an **IRI** while drawing a prefixed name computed from it: changing the drawn text means renaming that term across the whole document, with collision and prefix rules whose refusals need more room than a textbox has. **Most tempting, and out.** This pairing is stated here rather than only in the requirements because a reader who does not know it will start with the module that looks most ready.

**Wardley is not an adoption task and must not be planned as one.** `WardleyCanvas` imports nothing from the context channel — no `useContextConnection`, no `useContextSelection`, no `elementSelectionOf`. It has no selection, no menu and no shortcut, so the backend has been offering `Rename element` that no gesture on that canvas can reach. That is the C4 relationship defect one size larger, and the work is *adding selection to a canvas*, with inline rename following from it. Folded into an adoption task it produces an adopter that renders correctly in tests and does nothing in the product — which is exactly what happened with C4 relationships. Its own task, landing before any Wardley editor work.

**Selection is checked before editor work, per module, as an ordering rule.** Each adopter's first step is to establish whether the label's owner can be selected — elements and connections separately — and to record the finding. That ordering is the one that would have caught C4 the first time, and it is cheap: it is a grep for `elementSelectionOf` and a look at what the connection component is given.

### Modular design principles

The editor renders a textbox. The helper computes a rectangle. The canvas supplies geometry. The module decides what is renameable. Adoption touches only the last two.

## Components and Interfaces

### Component 1 — `labelPlacement.ts` (new, shared, pure, ~40 lines)

The three functions above, taking plain numbers and returning `LabelPlacement`. No React, no module vocabulary. Extracted from C4's two private helpers, which become its first callers; the extraction is proven by C4's existing placement tests passing **unchanged**, on the same criterion the `useContextPromptEntry` extraction used.

### Component 2 — one adopter per module

Each is the same three edits and nothing else:

1. **Provider** — pass `target.ElementId` as `ContextInputRequest`'s trailing argument on the qualifying prompts, and on no others.
2. **Canvas** — `useRegisterInlineLabelPlacement` with a resolver memoized on the model, calling a Component 1 helper; render `InlineLabelEditor` for a marked prompt naming something it draws; commit an open editor at each gesture start by taking the focus.
3. **Readme** — record what was marked, and for a module judged out, why.

Per module, what is in scope and what each needs first:

| Module | In scope | Needed first |
| --- | --- | --- |
| timeline | element label, relation label | nothing |
| dependency-graph | element label, relation label | nothing |
| databricks | task key, bundle name | nothing; **connections out** until they can be selected |
| azure-pipeline | display name | `F2` forwarding |
| wardley-map | element name | **context-channel selection**, as its own preceding task |

### Component 3 — selection for wardley-map (its own task)

`WardleyCanvas` gains what every other canvas has: `useContextConnection`, `useContextSelection`, an element click that pushes `elementSelectionOf`, a right-click that opens the shared menu, and `F2` through `structuralShortcutFor`. Built from the pattern already in the tree, not invented. Its acceptance is that a Wardley element can be selected and its backend actions run **before** any editor work is attempted there — and its value is wider than this specification, because every Wardley action becomes reachable, not only rename.

### Component 4 — the guard

`noPrivateLabelEditors.test.ts` is unchanged and already covers modules added later: it walks every module client and fails on any raw text field, with no import escape. Nothing to add.

## Data Models

`LabelPlacement` is unchanged — a rectangle in canvas units plus the text it replaces. The three helpers all return it, which is why an adopter's resolver is a few lines.

## Error Handling

| Scenario | Handling |
| --- | --- |
| Marked prompt, no canvas can place it | Dialog, silently — unchanged behaviour |
| Module marks a label with no single authored value | A specification error, caught in review by Requirement 1; the code cannot detect it |
| Adopter needs a change to shared code | Stop-and-report (Requirement 2.2) |
| Connection label unmeasurable (`getBBox` absent) | Per-character estimate, as today |

## Testing Strategy

### Per adopter

The marked-versus-unmarked contrast in one provider test; a placement test that a wrong placement fails; and the canvas tests for the reach it gained (`F2`, selection) where it gained any.

### Verification by sabotage

Every guard is perturbed and seen to fail before it is accepted (Requirement 9.1). On `inline-rename` three tests passed against broken code — one whose window React batched away, one whose blur jsdom never delivered, and one that omitted `StrictMode`, outside which the bug could not exist — and the third was deleted rather than kept. The shapes that worked mount the real registry, canvas and shell together rather than stubbing either side.

### Coverage

The mechanical requirement-coverage diff is run both ways: against the tasks before implementing, and against the finished code afterwards.

## Considered and declined

**`ContextAction.unavailable_reason` for the excluded modules.** The field exists and is the right tool for "shown but not choosable", but it does not fit either kind of exclusion here. The RDF readings are not missing a rename — they have one, and it opens a dialog; offering a disabled second entry would remove nothing and explain something the user has not asked. ansible-structure, helm-charts and sparql have no rename to disable, and inventing one so that it can be greyed out puts specification state into the product. Where a module is out **for now** rather than in principle — databricks' connections, wardley before its selection lands — the honest record is this document and the module readme, not a permanently disabled menu entry.

## Deviations and notes

**The count is fourteen canvases across ten modules, not seventeen files.** databricks' `JobCanvas`, `BundleCanvas` and `PipelineCanvas` are four-line wrappers around one `DatabricksCanvas`, so one task covers three readings. Tasks are per module for that reason.

**Timeline and dependency-graph draw with identical shared components**, which is why the second adopter is close to a copy of the first and why either can go first. Timeline is the recommendation, on the single ground that its connection labels exercise the midpoint helper hardest.
