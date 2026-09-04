# Design Document

## Overview

One editor component, drawn inside the canvas's own `svg` as a `foreignObject` at the label's coordinates. The backend marks the prompts whose value *is* the visible label; a mounted canvas says whether it can place that label; the shell's dialog host stands down when it can and renders the dialog exactly as today when it cannot.

The design's central choice is the coordinate system. Because the editor lives in canvas units rather than screen pixels, panning and zooming carry it with its element for free — Requirement 5.1 is satisfied by where the editor is mounted, not by code that tracks anything.

## Steering Document Alignment

### Technical Standards (tech.md)

- **"Consumers are pure subscribers … a consumer that needs more information asks for it to be carried on the selection rather than resolving it again itself."** This is the whole justification for the prompt marker. The client needs to know whether a value is the visible label; it asks for that to be carried, rather than inferring it from action ids.
- **"Extend the chain, don't add a parallel message."** The marker is a field on the existing `InputDialogPrompt`, not a new prompt kind. Every existing consumer keeps working; an unmarked prompt is byte-identical to today's.
- **Commands.** Nothing changes: the inline editor calls `SubmitInteraction`, the provider's `CommitAsync` dispatches the same command it dispatches now, and one rename is one undo. This specification adds no write path.

### Project Structure (structure.md)

- **"Core code must never depend on a specific diagram type."** The editor lives in the central canvas library and names no module. Which labels are editable is decided by the module that owns the label, on the backend.
- **"Contracts vs implementation."** The one contract change is additive and optional on both sides: an old client ignores the field, a module that never sets it keeps its dialog.

## Code Reuse Analysis

### Existing Components to Leverage

- **`ContextPromptHost`'s propose/submit/cancel contract** — already revision-stamped and debounced at 200 ms. The inline editor is a second presentation of it, not a second path.
- **`useDebouncedValue`** — the same debounce, unchanged.
- **`isTextTarget` (`canvas/interaction.ts`)** — already returns true for an `input`, and every canvas already consults it before treating a keystroke as structural. Requirement 8.4 therefore needs no new code: keys typed into the editor stop being canvas keys the moment the editor has focus.
- **`structuralShortcutFor` and the existing `F2` forwarding** — **nine** canvases now forward `F2`, and the backend already owns the key-to-action table. Requirement 8.2 needs no canvas change at all. (The requirements said six; the RDF family's ontology, shapes and scheme canvases have landed since, which is the pattern Requirement 3.6 anticipated.)
- **`DiagramToolboxContext`** — the exact idiom for the placement registry below: a mounted canvas registers, the shell reads, unmount withdraws.
- **`noPrivateScrollbars.test.ts`** — the exact idiom for Requirement 9.3's guard, including its honest statement of what a text-reading guard cannot catch.

### Integration Points

- `src/api/context.proto` — one nested message and one field.
- `ContextInputRequest` and `ContextService.Actions.ToProto` — one optional parameter, mapped.
- `ShellPromptHost` — consults the registry before rendering.
- Each adopting canvas — registers a resolver and renders the editor; nothing else.

## Architecture

Four seams, in dependency order.

### 1. The marker (backend and contract)

```proto
message InlineLabelEdit { ElementId element_id = 1; }
message InputDialogPrompt { /* ...unchanged... */ InlineLabelEdit inline_label_edit = 6; }
```

`ContextInputRequest` gains a trailing `string InlineLabelElementId = ""`. A provider sets it from `target.ElementId`, which it already holds; every existing construction compiles unchanged, which is how Requirement 2.5's per-module adoption is achieved.

**Why a message naming an element rather than a boolean.** The prompt arrives at a shell-level host that has no memory of what the action targeted, and an action invoked on one thing can prompt about another. Naming the element makes the prompt self-describing, and leaves room to grow without a second oneof.

**Why not `ContextSelectionAction.RENAME_REQUEST`.** That value exists in the contract and is used by nothing. It describes a gesture a surface performed, not a property of a prompt the backend sent — using it would have the client announce an intent and then assume the prompt that came back matches it. That assumption is the guess this design exists to remove.

### 2. Placement (client, shell)

`InlineLabelPlacementContext`, beside `DiagramToolboxContext`. A mounted canvas registers one function:

```ts
(elementId: string) => LabelPlacement | null
// LabelPlacement: { x, y, width, height } in canvas units - the label's own box
```

`ShellPromptHost` renders nothing when the prompt is an `inputDialog` carrying a marker **and** a registered resolver answers non-null; otherwise it renders the dialog, unchanged. The canvas renders the editor when **its own** resolver answers non-null. Both consult the same registered function, so the two cannot disagree about whether a prompt is inline-able.

Registration happens at mount rather than per prompt, so the decision is available in the first render that sees the prompt and no dialog flashes before the editor appears.

A resolver answers null — and the dialog is therefore shown — when the id is not on this canvas, when the element is not currently drawn, when the label has no measurable box, and **when the canvas's own selection holds more than one member**. That last case is Requirement 6.3's decision, taken once here rather than per canvas, and it is deliberately the option that needs no module change.

### 3. The editor (client, central canvas library)

`src/client/src/canvas/label/InlineLabelEditor.tsx`. Rendered by the canvas inside its `svg`:

- `foreignObject` at the placement rectangle, holding one `input`.
- On mount: focus, select all. On unmount: focus returns to the canvas.
- Enter commits; Escape abandons; blur commits; a refusal keeps it open with the text intact; a value equal to the original cancels without dispatching.
- Typing is local state, so no element re-renders per keystroke.

The revision/verdict discipline currently inside `InputPromptDialog` moves into a shared hook, `useContextPromptEntry`, used by both presentations — the only way Requirement 1.4 and the "interchangeable" non-functional requirement can hold as the two evolve.

### 4. Reach

Nothing new. The context menu and `F2` already run the action; the action already answers with an input prompt; the prompt now carries a marker.

### Modular Design Principles

The editor renders a textbox and reports intent. It does not know what a rename is, cannot decide what is renameable, and imports nothing from any module. A canvas supplies only geometry and text. A module supplies only the marker.

## Components and Interfaces

| Component | Where | Responsibility |
| --- | --- | --- |
| `InlineLabelEdit` | `src/api/context.proto` | Says this prompt edits the visible label of a named element |
| `ContextInputRequest.InlineLabelElementId` | backend `Context/_Model` | The module's way of saying it |
| `InlineLabelPlacementContext` | `shell/panels` | Mounted canvases' placement resolvers |
| `InlineLabelEditor` | `canvas/label` | The textbox, its keys, its focus, its refusal |
| `useContextPromptEntry` | `shell/context` | Revision, debounce and verdict, shared by both presentations |

## Data Models

`LabelPlacement` is a rectangle in canvas units plus the text it replaces. Canvases with a boxed label (every shared element component) already hold it. Connection labels do not: `InteractiveBezierConnection` draws a bare `text` at the midpoint with no box, so the canvas measures the rendered node with `getBBox()` and falls back to the per-character estimate the shared element components already use.

## Error Handling

| Scenario | Handling |
| --- | --- |
| Prompt marked, no canvas can place it | Dialog, silently (R2.4, R7.3) |
| Value refused by the module | Shown against the open editor, text intact (R4.5) |
| Element removed mid-edit | The resolver answers null, the editor unmounts, the interaction is cancelled, and the user is told through the prompt host's existing notice — extended from one cause to two (R5.4) |
| Element scrolled out of view | **Follows.** It is in canvas coordinates, so following is what already happens; committing on a scroll would surprise a user who scrolled only to look at something (R5.2) |
| A gesture begins while editing | Commit first (R5.5) — one call at the top of each gesture's start handler |
| Backend build without the field | Field absent, prompt unmarked, dialog as today |

## Testing Strategy

### Unit Testing

`InlineLabelEditor`: Enter commits, Escape abandons and dispatches nothing, blur commits, a refusal keeps it open with the text intact, an unchanged value dispatches nothing, focus returns to the canvas (R9.1). `ShellPromptHost`: an unmarked prompt renders the dialog, a marked prompt no resolver can place renders the dialog, a marked prompt a resolver can place renders neither (R9.2).

### Integration Testing

Backend: a provider setting the marker produces a prompt carrying it, and one that does not produces today's prompt exactly.

### The guard (R9.3)

`noPrivateLabelEditors.test.ts`, in the same folder and the same shape as `noPrivateScrollbars.test.ts`: no `src/diagrams/*/client` source may render an editable text field on a canvas without importing `@client/canvas/label`. Its limit is the same one that test states about itself, and should be stated the same way.

### Manual (R9.4)

One `tests.md` entry — rename inline, see a refusal inline, pan mid-edit — **executable rather than pending**: it runs against a local developer build using the checked-in `admin`/`changeme` placeholder, per CLAUDE.md's sign-in ruling. Focus, caret and blur are what jsdom models least faithfully, and `getBBox()` is not implemented there at all, so the measured placement has no unit coverage by construction and this check is where it is verified.

## Deviations and notes

**A decorated label is still one authored value.** C4 renders a relationship as `description [technology]`, sometimes order-prefixed. Requirement 3.2 puts relations in scope and Requirement 3.4 puts projections out, and this label is arguably both. The rule this design adopts: the editor replaces the whole rendered label *visually* and edits the single authored value the module names in `initial_value`. Decoration around one authored value is chrome. A label with no single authored value beneath it — type badges, a computed summary — is not markable at all. This is a line the requirements left implicit, and it is drawn here rather than left to each module.

**Requirement 5.2 asked for a choice and this design makes it: follow, not commit.** Stated here so it is one rule rather than one per canvas.

**No derivative folders**, per Requirement 1.5. There is one editor and one placement contract.
