# Design Document

## Overview

The requirements established that this is not a vocabulary to be built but **an escape hatch to be closed**: thirteen built-in shapes exist and all render, and all twenty-eight element types in thirteen modules reach past them for a `CustomShapeRef`. They also established *why*, in four measured gaps — **multiple labels, decorations, a notation background, and declared actions with enablement**.

This design says what the library surface is, concretely enough to be built without a second design, and how its sufficiency is proven before any module migrates.

**One mechanism underlies all four additions: the binding.** A binding is a declared path into the model element, optionally composed and formatted, producing a value. It is what replaces *"a function computing the property"* — the phrase Requirement 2.5 uses — and it is the reason the four gaps close together rather than as four unrelated features. A label's text is a binding. A decoration's presence is a binding. An action's enablement is a binding. Without it, each gap needs its own escape.

## Steering Document Alignment

### Technical Standards (tech.md)

- **Consumers are pure subscribers.** A module declares; the library draws and dispatches. A module never asks what the canvas is doing.

### Project Structure (structure.md)

- **Canvas infrastructure carries no diagram-type knowledge.** Every addition below takes paths, numbers and enum values. A property whose name contains a diagram type is a design error, and the guard in Component 6 fails on one.
- **Dependency direction** — module to library, never back.

## Architecture

### The binding, and why it comes first

```
Binding := { path: "payload.name" }                     a field
         | { path: "payload.rows", each: Binding }      a collection
         | { template: "{payload.predicate}: {payload.value}" }
         | { path: "...", when: Condition }             conditional
```

Three properties make it sufficient for what the renderers do today and safe for shared code:

- **It is data**, so it crosses a validation boundary, can be checked by a guard, and can be serialised.
- **It resolves against the element the library already holds** — `DiagramModelElement` plus the module's `payload` — so it needs no module code at resolution time.
- **It cannot call anything.** A binding that could invoke a function would be the escape hatch under a new name, which is the failure this specification exists to end.

`fit(text, width)` — the truncation helper every card renderer calls by hand — becomes a property on the text declaration rather than a function a module invokes.

### Addition 1 — Text: `labels`, replacing the single `label`

The measured need, from `owl-card`, which is the richest and therefore the specification: a header line, a badge line **present only when the model's badge list is non-empty**, and **one line per entry in `node.rows`**, each composed as `predicate: value annotation`, each truncated to the box width, plus a tooltip.

So a text declaration carries:

| Property | Serves |
| --- | --- |
| `text` | a Binding — field, template, or collection with `each` |
| `slot` or `offset` | where it sits: a named slot (`header`, `body`, `footer`) or explicit x/y |
| `stack` | for a collection: line height and starting offset, replacing `rowsStart + (index+1) * ROW_HEIGHT` |
| `typography` | size, weight, slant, colour — the user's own list, and `LabelTypography` already carries three of the four |
| `editable` | in place through the shared editor, exactly as `LabelRule.editable` means today |
| `truncate` | to the box, replacing the hand-called `fit` |
| `when` | conditional presence, replacing `badges.length > 0 ? … : null` |
| `tooltip` | the `<title>` every card renderer writes |

**The collection case is the load-bearing one.** A fixed three-slot design — which `styled-box`'s name/type/description already is — would close the gap for c4 and leave the RDF family exactly where it is. Ten renderers hand-roll `<text>`; the ones that hand-roll *loops* are why this must bind to a collection rather than enumerate slots.

`LabelRule` is superseded by `labels`. A single-label module writes one entry, so migration is mechanical.

### Addition 2 — Decorations: `decorations` on an element type

The five renderers using no shared component draw four things between them: a **stub** (a path from a point to nowhere, with a label), a **badge** (a path plus centred text), an **annotation** (bare positioned text), and a **target** (a circle plus a label). These are ornaments attached to an element, not shapes of their own — which is why they never fitted the shape vocabulary and had to escape it.

A decoration declares a **glyph** — `line`, `path`, `circle`, `rect`, or a named marker — with its geometry bound, its class, and an optional text. It is deliberately a small closed set: a decoration vocabulary that could express anything would be a renderer.

**Where the boundary sits, stated because an implementer will test it:** a decoration is drawn *relative to its element* and carries no hit-testing, no gesture and no anchor. Anything needing those is an element type, and declaring it as one is the answer.

### Addition 3 — Background: `background` on the definition

Two instances make this a category rather than a special case: `wardley-map` draws axes, evolution stage bands, attitude regions, notes and annotations — **twenty-one of its twenty-four raw-SVG lines** — and `timeline` draws a ruler in a file of its own.

The background is a declared list of **bands** (a labelled region between two coordinates), **axes** (a line with end labels and a rotated title), **gridlines**, and **regions** bound to model collections. It draws beneath everything, takes no gestures, and is `aria-hidden` — which is what `wardley-chrome` already is.

**This addition is the one most likely to be argued as out of scope.** It is in scope because the alternative is that wardley keeps a renderer, and *"declarative except for the hard one"* is the specification the user did not ask for.

### Addition 4 — Actions: `actions` on the definition and on element types

The measured need: eleven canvases hand-write a structural shortcut list with **four different spellings of the same intent**, nine synthesise `{ key: "Delete", … }` to name an action, and six wire nothing — so *"this type has no rename"* and *"nobody wired one"* are indistinguishable.

An action declares:

| Property | Serves |
| --- | --- |
| `id` | the backend action or shortcut it invokes — the module's own vocabulary, passed through |
| `invokedBy` | `shortcut` (a key), `menu`, `gesture`, or several |
| `appliesTo` | element types, connections, or the diagram |
| `enabled` | a Binding — so enablement varies with the model without a function |
| `label` | what the menu shows |

**The line the user drew, made structural:** the declaration says an action exists, what invokes it, what it applies to and when it is enabled. **The handler that runs when it fires stays imperative** — that is the permitted carve-out, and it is now the *only* thing in a module's client that is code. `structuralShortcutFor` and the synthesised keystroke both disappear: the library owns the keyboard, because the declaration tells it which keys mean what.

Enablement already half-exists — `dragging`, `draggable`, `deletable`, `label.editable`, `connectOnRightDrag`, endpoint constraints, `layout.modes`. **Those are not replaced.** They are the same idea stated per-concern, and the design keeps them, because a mechanism that already reads correctly should not be relitigated to satisfy a symmetry nobody asked for. `enabled` bindings extend the idea to actions, which is where it stops today.

### What crosses the wire, and a nuance on the user's ruling

The user chose **client and wire**, on the option stating that *"the backend sends data already shaped for direct consumption, so the mapping disappears rather than becoming declarative."* The design honours that with one split, which is reported rather than absorbed:

- **The structural half of the mapping goes to the wire.** Identity, type, position, size and connection endpoints are the same concepts in every module and are what `DiagramModel` already is. Sending them shaped removes the bulk of the 665 lines outright.
- **The semantic half stays a binding.** Which payload field is the header, which collection are the rows, which flag drives a status colour — this is each module's own vocabulary, and the wire would have to carry the module's payload anyway. Declaring the binding is how the client knows what to draw with it.

**Why not push the semantic half over too:** it would mean each module's backend deciding presentation, which inverts the dependency `structure.md` sets and puts "which line is the header" in a `.proto`. The nuance is flagged to the user rather than decided silently, because their ruling's wording could be read as asking for both halves.

## Components and Interfaces

| # | Component | Where | Requirements |
| --- | --- | --- | --- |
| 1 | The binding resolver | `canvas/library/definition/binding.ts` | 2.5 |
| 2 | `labels` and its layout | `definition/` + `DiagramCanvas` | 2.2, 2.3, 3.1 |
| 3 | `decorations` | `definition/` + `DiagramCanvas` | 3.2 |
| 4 | `background` | `definition/` + `DiagramCanvas` | 3.3 |
| 5 | `actions` and the keyboard | `definition/` + `DiagramCanvas` | 2.6, 2.7, 2.8, 3.4 |
| 6 | The conformance guard | `canvas/library/` | 7.1–7.6 |
| 7 | The sufficiency proof | this specification's own artifact | 4.1–4.4 |
| 8 | Per-module migration | thirteen modules | 9.1–9.6 |

Components 1–5 are the shared-library change **Requirement 3.6 sequences before any module migration.** They land as their own body of work, gated, before the reference module is touched.

### Component 7 — how sufficiency is proven, and what happens when it is not

Requirement 4 demands sufficiency *proven against all sixteen canvases before migration*, and the requirements said a failure is *"a finding to report before anyone migrates, not a module left imperative"*. The procedure:

**The artifact is a translation table**, committed in this specification's folder, with one row per element type — twenty-eight rows — naming: the module, the current custom shape, the built-in that replaces it, the labels it needs, the decorations, and any property that does not yet exist.

**It is produced before Component 8 begins and after Components 1–5 are designed**, so it is a check on the design rather than a plan for the migration.

**When a row cannot be filled**, the procedure is fixed and does not involve judgement under pressure:

1. The gap is recorded in the table with the module and what it needs.
2. **The vocabulary is extended centrally** — Requirement 1.5's stop-and-report — and the table re-checked.
3. **A module is never marked as an exception**, and the migration does not begin with an unfilled row.

**The table is also the migration's order and its acceptance**: a module is done when its rows are satisfied and its tests pass unchanged.

### Component 6 — the guard

One shared check walking every module client, in the `sharedStylesheetImports` shape: **fail once naming every offender**, with both canary shapes — a floor on modules walked, and a named member asserted present. It fails a module client that contains a render function, a shortcut key list, a synthesised key event, or raw SVG.

**Keyed by artifact, per file that owns the property**, so `databricks`' three wrappers over one inner canvas cannot misreport — the trap that caught four people this week.

**Its limit stated in its own doc-comment**: it reads text, so a renderer under an unrecognisable name goes unseen. It catches the copy, which is how this actually happens.

**Requirement 7.6 is discharged here**: `BUILT_IN_SHAPES` carries a comment describing a guard and a toolbox that do not exist. This design creates the guard, so the comment becomes true; the toolbox half is corrected or removed rather than left as a second imagined consumer.

### Component 8 — the migration

**The reference module is `dependency-graph`.** Justified: it declares one element type and one route, its shape wraps a single shared component with no raw SVG, it carries a full action set (F2, Insert, Tab, Enter and a synthesised Delete), and at 382 lines it is close to the median. It exercises labels, actions and the shape swap without also being the first test of decorations or background. **`dotnet-dependency-graph` is deliberately not the reference** despite being smaller: it was under active change while this was written.

Then, in order and each its own landing: the single-label modules, the multi-label RDF family, the decoration cases, and `wardley-map` last as the background's proof.

## Error Handling

| Scenario | Handling |
| --- | --- |
| A binding path does not resolve | Draws nothing and reports through the existing validation path; a missing field is a declaration defect, not a crash. |
| A declaration is invalid | `assertValidDiagramDefinition` rejects it at mount, as it does today — the validation boundary already exists. |
| A module needs a property that does not exist | Requirement 1.5: stop-and-report, central addition, table re-checked. Never a local exception. |
| A migrated module draws differently | Requirement 8.2: a defect in the vocabulary or the declaration, not an accepted cost. |

## Testing Strategy

- **Requirement 8.1 is the master gate and it is nearly free**: every migrated module's existing tests pass unchanged, or the change is evidence of a behaviour change.
- The binding resolver, the label layout and the action dispatch are unit-tested in the library, where they are pure.
- **The guard is seen to fail** against a deliberately non-compliant module before it is accepted.
- The manual check (Requirement 8.4) covers one diagram per shape family in a real browser, because typography moving from stylesheets into declared properties is a visible-outcome change.

## Considered and declined

**A fixed set of label slots.** Simpler, and it closes the gap for c4 and nothing else: the RDF family draws one line per collection entry, so slots would leave the largest group of offenders exactly where they are.

**Letting a binding call a module function.** It would make every gap trivially closable and would be the escape hatch renamed. Declined categorically.

**Replacing `draggable`/`deletable`/`label.editable` with a general `enabled` mechanism.** Symmetry for its own sake; those read correctly, are already declarative, and rewriting them adds risk to thirteen migrations for no gain.

**Pushing the semantic half of the mapping over the wire.** It would put "which line is the header" in a `.proto` and invert the dependency direction. Flagged to the user as a nuance on their ruling rather than decided silently.

**Keeping `CustomShapeRef` for wardley alone.** *"Declarative except the hard one"* is not what was asked for, and wardley's shapes are trivial — it is the background that is hard, which Addition 3 answers directly.

## Deviations and notes

**None against the approved requirements.** The wire split under *What crosses the wire* is a reading of Requirement 5.2's *"the design SHALL state what crosses and why"*, not a departure from it — but the user's chosen option said the mapping should *disappear* rather than become declarative, and half of it becomes a binding here, so it is raised rather than assumed.

**The approved requirements carried four gaps that nobody challenged in review.** They are treated here as approved rather than as unchallenged: the design closes all four, and a later reader should not read silence as scope to drop one.
