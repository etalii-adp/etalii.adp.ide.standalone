# Requirements Document

## Introduction

The Properties panel shows **what is selected and what can be changed about it**: the selection's chain as the backend resolved it, and — for the innermost level — a list of properties contributed by whichever module owns that kind of thing, each rendered by an editor the panel does not have to understand. Editing a value there is an ordinary project edit: it travels as a command, lands on the project's history, and is one undo away.

> **Status: this spec follows the implementation rather than preceding it.** The property grid was built on `claude/property-grid` and merged to `develop` (`2b70a8e`) without a spec — the seam ([`IContextPropertyProvider`](../../../../src/backend/EtAlii.Adp.Backend/Context/IContextPropertyProvider.cs)), the resolver, the `DescribeProperties`/`SetProperty` RPCs, the panel ([`PropertyGridPanel.tsx`](../../../../src/client/src/shell/panels/PropertyGridPanel.tsx), [`PropertyRow.tsx`](../../../../src/client/src/shell/panels/PropertyRow.tsx)) and two providers (mindmap, C4) all exist and are tested. This document is the specification that work is measured against, written retroactively so the contract is fixed **before its third consumer** — the [`azure-pipeline-diagram`](../azure-pipeline-diagram/requirements.md) spec, whose Requirement 13 binds to exactly this contract — rather than remaining implied by code. Where a requirement is already met, it pins behaviour so it cannot regress silently; where it is not (Requirement 9), it says so explicitly and names the spec that owes it.

The panel's design question was never "how do we render a name field" but **where per-type knowledge lives**. The context stream's `ContextLevelDetail`/`ElementDetail` carries `{ text, has_children, folded, linked }` — a fixed, mindmap-shaped set in a core message. Widening it per type would put every type's vocabulary into core, exactly what [structure.md](../../../steering/structure.md) forbids. The property grid's answer is the same one the context menu and the toolbox already gave: **described-as-data contributions through a per-scope provider seam**, with `ElementDetail` left as the minimal common denominator (a display name for the level header) and everything type-specific arriving as `ContextProperty` rows.

**Dependencies:** [`context-service`](../context-service/requirements.md) (the `ContextTarget`/scope routing, `TryResolveTargetAsync`, and the provider registration pattern), [`diagram-undo-redo`](../diagram-undo-redo/requirements.md) (the history every edit lands on), [`mindmap-diagram`](../mindmap-diagram/requirements.md) and the C4 module (the first two providers). **Dependents:** [`azure-pipeline-diagram`](../azure-pipeline-diagram/requirements.md) Requirement 13 (the third provider, and the one that owes the `Choice` editor).

## Alignment with Product Vision

* [product.md](../../../steering/product.md)'s **"A familiar surface"**: every IDE has a property grid, and with it the expectations this spec meets — label/value rows, groups, greyed-but-visible values, commit on Enter or blur, Escape to abandon.
* product.md's **"Files are the source of truth"**: the grid shows what the document says and writes through the same commands every other surface uses; it never holds a private copy of the element, and a pushed change (another connection's edit, an undo) wins over anything the grid believed.
* [tech.md](../../../steering/tech.md)'s **Commands rule**: every functional state change is an `ICommand` dispatched through `IHistoryStack`. A property edit is a functional state change; therefore it is a command, and therefore it is one undo away.
* tech.md's **frontend-backend synchronization**: properties are described by the backend and rendered by the client. The client decides nothing — not editability, not editors, not grouping.
* [structure.md](../../../steering/structure.md)'s **Core vs diagram-type plugins**: core routes by scope and renders data; what a "technology", a "condition" or a "note" is lives in the module that owns the type, behind one registered interface.

## Requirements

### Requirement 1 — The panel shows the selection's chain, and edits only its innermost level

**User Story:** As an architect, I want the Properties panel to show me what I have selected — including the path that led there — and to let me edit the thing itself, not its context.

#### Acceptance Criteria

1. WHEN nothing is selected THEN the panel SHALL show a placeholder saying so, not an empty grid.
2. WHEN a selection is pushed THEN the panel SHALL render one section per level of the chain, outermost first, titled by that level's display name — an element's text, else the last path segment.
3. WHEN a level is not the innermost THEN the panel SHALL show it as context only — path and kind — and SHALL NOT offer its properties for editing: a folder above a diagram is where the diagram lives, and two editable "Name" rows at once is a good way to change the wrong one.
4. WHEN the innermost level is rendered THEN the panel SHALL show the properties contributed for it (Requirement 3), grouped per Requirement 7, each as a row with the label and value its provider supplied.
5. WHEN a hierarchy level is rendered THEN the panel SHALL show its project-relative path and its kind (file/folder), read from the pushed `ContextLevelDetail` — never from a lookup of its own.

### Requirement 2 — A property is data the panel renders without understanding

**User Story:** As a developer of EtAlii.Adp, I want a property described entirely by the backend, so a new diagram type's properties appear in the panel without the panel changing.

#### Acceptance Criteria

1. WHEN a property is contributed THEN it SHALL carry: a stable **id** (module-prefixed like an action id, e.g. `c4.description`), a **label** in the provider's words, the current **value as text**, an **editor** (Requirement 8), a **read-only reason** (Requirement 4), and an optional **group** (Requirement 7) — the `ContextProperty` message; nothing else crosses the wire.
2. WHEN the panel renders a row THEN it SHALL use only that data. The shell SHALL contain no knowledge of any property id, any label, or any type's vocabulary — the same rule the context menu and the toolbox already obey.
3. WHEN a property is **absent** from the document THEN the provider SHALL NOT contribute it, rather than contributing it with an empty value; a property that is **present but empty** SHALL be contributed with an empty value. The two are different states of the file, they render differently, and no sentinel value stands in for either.
4. WHEN a `Toggle` property is contributed THEN its value SHALL be the text `"true"` or `"false"`; every other editor's value carries itself.

### Requirement 3 — Contribution through a per-scope provider, resolved like actions

**User Story:** As a developer of a diagram type, I want to contribute my elements' properties by registering one implementation, so core learns nothing about my type.

#### Acceptance Criteria

1. WHEN a module contributes properties THEN it SHALL implement `IContextPropertyProvider` — `Scope`, `DescribeAsync(target)`, `SetAsync(target, propertyId, value)` — and register it in its own `Add<Module>` extension as one line. The seam mirrors `IContextActionProvider`: per **scope**, not per origin, because the target arrives already resolved and the provider decides for itself whether the element is one of its own.
2. WHEN properties are asked for THEN `ContextPropertyResolver` SHALL consult every provider registered for the target's scope, in registration order, and concatenate what they describe. The resolver SHALL know no property id of its own.
3. WHEN a provider receives a target THEN `ContextTarget.ResolvedFullPath` SHALL already be resolved and containment-checked by the service layer; a provider SHALL never resolve a location itself.
4. WHEN the target no longer exists — the element left the document, the file left the disk — THEN the provider SHALL describe no properties at all, and a `SetAsync` against it SHALL refuse with a sentence rather than throw.
5. WHEN the client asks THEN it SHALL use the `DescribeProperties` and `SetProperty` RPCs, following the action RPCs' source rule: an explicit `ContextSource` names the target, no source means the connection's current selection, and a source that resolves to nothing is answered emptily — a caller learns nothing about what it may not see.
6. WHEN properties are kept apart from actions THEN that separation SHALL be deliberate: an action is a verb offered, a property is a value that is already there, and a module may well have one and not the other.

### Requirement 4 — Read-only is a reason, and the reason is a contract

**User Story:** As a reader of a pipeline stage whose text lives in a template, I want to see its values and be told why I cannot change them here — and I want "cannot" to be true, not a suggestion to my UI.

#### Acceptance Criteria

1. WHEN a property cannot currently be edited THEN it SHALL be contributed with a non-empty **`ReadOnlyReason`** — a sentence saying why, and where the value would have to be changed instead — rather than being omitted. This mirrors how an unavailable action carries `UnavailableReason`: silence leaves the reader wondering whether the tool is broken.
2. WHEN the panel renders a read-only property THEN it SHALL show the value uneditable with the reason beside it.
3. WHEN a write arrives for a property its owner described as read-only THEN `ContextPropertyResolver` SHALL refuse it with that reason **server-side**, whatever the client claimed — a stale grid, or a caller that is not the grid at all, cannot write a value the provider said was unwritable.
4. WHEN a write arrives for a property no provider describes THEN the resolver SHALL refuse it, naming the id.
5. WHEN a property is state the history cannot own — the mindmap's per-connection fold state is the existing case — THEN the provider SHALL contribute it read-only with a reason pointing at the surface that does own it, because a grid that promises every edit is one undo away must not offer an edit that would not be.

### Requirement 5 — Every edit is a command, one undo away

**User Story:** As an architect, I want a value I change in the grid to behave like any other edit: undoable, seen by everyone viewing the diagram, and identical to the same change made anywhere else.

#### Acceptance Criteria

1. WHEN an editable property is committed THEN the provider's `SetAsync` SHALL dispatch an `ICommand` through the project's `IHistoryStack` — the **same command** the canvas or a context action uses for that change, where one exists (the mindmap's Text row and its "Rename…" action are one implementation and one undo entry).
2. WHEN the command lands THEN the resulting document change SHALL reach every connection viewing that document as ordinary pushes, and the grid SHALL re-read its properties from the push (Requirement 6.4) rather than trusting what it sent.
3. WHEN a value is rejected — by the provider or by the command — THEN the result SHALL carry a sentence for the user, the document SHALL be unchanged, and the panel SHALL put the old value back and show the reason: a field that silently keeps a rejected value is worse than one that refuses out loud.
4. WHEN `SetAsync` refuses THEN it SHALL refuse as a result, not an exception — the same discipline `CommandResult` already imposes.

### Requirement 6 — Commit on Enter or blur, never per keystroke; the push wins

**User Story:** As a user typing a description, I want my keystrokes to stay mine until I commit — and I want what the backend actually accepted to be what I end up looking at.

#### Acceptance Criteria

1. WHEN a value is edited THEN the panel SHALL write it on **Enter or on losing focus, once per committed edit, and never while typing**. Per-keystroke writes would put one undo entry — and, for a type whose file is executable configuration, one on-disk state — per character typed.
2. WHEN Escape is pressed THEN the edit SHALL be abandoned and the last pushed value restored, with nothing written.
3. WHEN focus leaves a field whose value was not changed THEN nothing SHALL be written: no history entry for having clicked somewhere.
4. WHEN the row is not being edited THEN the pushed value SHALL win — another connection may have changed it, and an undo certainly will have. While the field is focused it holds what the user typed; the moment it is not, it shows what the backend last said.
5. WHEN an edit commits successfully THEN the grid SHALL NOT update itself optimistically: the change lands on the document, the document change is pushed, and the properties are read again — so the grid never shows its own guess about what the backend did.

### Requirement 7 — Grouping, for selections with more properties than one list reads well

**User Story:** As a reader of a stage that carries identity, ordering and execution concerns at once, I want the rows sorted under headings that match the question I came with.

#### Acceptance Criteria

1. WHEN a property carries a group THEN the panel SHALL render it under that heading; properties with no group come first, ungrouped.
2. WHEN groups are ordered THEN each SHALL appear where its first property appeared in the providers' combined order — the provider's order is the display order, and the panel imposes none of its own.

### Requirement 8 — Editors: three today, widened by the consumer that needs more

**User Story:** As a developer of a new diagram type, I want the set of editors to grow when a type actually needs one, as a backward-compatible contract change — not speculatively.

#### Acceptance Criteria

1. WHEN a property is described THEN its editor SHALL be one of the contract's editors: **`Line`** (one line of text), **`Text`** (several), **`Toggle`** (yes/no). These three exist because the mindmap and C4 providers consume them today.
2. WHEN the panel meets an editor value it does not know THEN it SHALL fall back to rendering the value as text, read-only — a forward-compatibility stance, since widening the enum is a backward-compatible proto change.
3. WHEN a future type needs a constrained choice THEN a **`Choice` editor carrying provider-supplied candidates** is the planned widening, owed by the first consumer that needs it — [`azure-pipeline-diagram`](../azure-pipeline-diagram/requirements.md) Requirement 13.14 (`dependsOn`) — and SHALL NOT be added before that consumer exists. The same applies to genuinely list-valued properties: no delimiter convention is invented here; the shape is decided against working code and a second consumer.

### Requirement 9 — What this spec deliberately does not cover *(not implemented, and not owed here)*

1. **`Choice` editors and list values** — owed by `azure-pipeline-diagram` (Requirement 8.3).
2. **Property search/filtering** within the grid — no consumer has enough properties to need it; the first that does should specify it.
3. **Widening `ElementDetail`** — explicitly rejected rather than deferred: per-type element data arrives as properties through the provider seam, and `ElementDetail` stays the minimal common denominator (display text and structural flags for the level header). A type that finds `ElementDetail` too small is pointed here, not at the proto.

## Non-Functional Requirements

* **No new streams.** Properties ride the existing request/response RPCs plus the existing `Watch` push for change notification; the panel opens nothing of its own.
* **Isolation.** A provider that throws in `DescribeAsync` must not take the panel down: the service layer answers with what it has and logs the rest. *(Pinned here; verify against the resolver when the design is written — a throwing provider currently propagates.)*
* **Registration order is deterministic**, so two connections to one project see the same rows in the same order.
* **Testing:** the seam is testable without gRPC (`ContextPropertyResolver` against stub providers — [`ContextPropertyResolver.Tests`](../../../../src/backend/EtAlii.Adp.Backend.Tests/Unit%20Tests/Context/ContextPropertyResolver.Tests.cs) exists); each provider is testable against its own document store; the panel against a mocked connection ([`PropertyGridPanel.test.tsx`](../../../../src/client/src/shell/panels/PropertyGridPanel.test.tsx) exists).
