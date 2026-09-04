# Requirements Document

## Introduction

This spec covers the findings of a consistency scan over **`src/client/src/**`** — the shell, its panels, the context channel, the canvas library, the auth and editor layers, and the pages. It is one lane of a five-lane scan; findings that belong to another lane are recorded as crossings at the end rather than followed.

**Thirteen findings: four defects and nine drift items.** They are separated because they deserve different treatment. A defect is something a user can hit today; drift is something that will cost the next person time. The four defects are one class with one cause, which is the most useful thing this scan found: **the context channel's async methods reject on any backend fault, and every call site consumes them as if they cannot.**

## Alignment with Product Vision

[product.md](../../steering/product.md)'s **"Honest about what it knows"** is the standard the defects fail. A row that shows an edit as applied when nothing was written, and a row that silently stops accepting edits, are both the application asserting something untrue about the document. [tech.md](../../steering/tech.md)'s command discipline says every edit is one undo away; an edit that never reached the backend is not on the history at all, and the user has no way to know.

## Requirements

### Requirement 1 — A failed property write must not look like a successful one

**User Story:** As a user editing a property, I want to be told when my edit did not land, so that I do not carry on believing the document says something it does not.

**The defect.** `ContextConnectionProvider.setProperty` ([ContextConnectionProvider.tsx:326](../../../src/client/src/shell/context/ContextConnectionProvider.tsx)) awaits `client.setProperty` with no `try`/`catch`, so it rejects whenever the backend is unreachable, the session has expired, or the stream is mid-reconnect. Three call sites consume it as though it cannot:

- [PropertyRow.tsx:146](../../../src/client/src/shell/panels/PropertyRow.tsx) — the select/dropdown path: `void onCommit(next).then((failure) => { setError(failure); if (failure.length > 0) setDraft(property.value); })`. On rejection the callback never runs, so **no error is shown and the draft keeps the user's new value** — the row displays the edit as applied while nothing was written.
- [PropertyRow.tsx:177](../../../src/client/src/shell/panels/PropertyRow.tsx) — the checkbox path, identical shape and identical consequence.
- [PropertyRow.tsx:64-68](../../../src/client/src/shell/panels/PropertyRow.tsx) — the text-input path is worse. `commit()` does `committing.current = true; const failure = await onCommit(draft); committing.current = false;` with no `try`/`finally`. On rejection **the guard stays latched**, and every later edit on that row returns early at line 49 without writing. One transient fault leaves that row permanently unwritable for the session, silently.

#### Acceptance Criteria

1. WHEN a property write rejects THEN the row SHALL show an error naming that the value was not saved, and SHALL restore the displayed value to what the document actually holds.
2. WHEN a property write rejects THEN the in-flight guard SHALL be released, so a retry is possible without remounting the row.
3. WHEN either happens THEN a test SHALL drive it by rejecting the commit, and SHALL fail against today's code.

### Requirement 2 — A failed property read must not leave the grid asserting stale values

**User Story:** As a user, I want the property grid to say when it could not read, rather than continuing to show what was true a minute ago.

**The defect.** `describeProperties` ([ContextConnectionProvider.tsx:314](../../../src/client/src/shell/context/ContextConnectionProvider.tsx)) awaits its call with no `try`/`catch`; [PropertyGridPanel.tsx:102](../../../src/client/src/shell/panels/PropertyGridPanel.tsx) consumes it as `void describeRef.current().then((described) => { ... })` with no `.catch`. On rejection this is an unhandled promise rejection, and the grid keeps rendering the previous selection's properties with nothing to say they are stale.

#### Acceptance Criteria

1. WHEN a describe rejects THEN the grid SHALL show an unavailable state naming the reason rather than stale rows.
2. WHEN a describe rejects THEN no unhandled rejection SHALL reach the window.

### Requirement 3 — A prompt dialog must not freeze its verdict on a failed validation

**User Story:** As a user in a dialog, I want validation to keep working, or to tell me it stopped.

**The defect.** `onPropose` ([ContextConnectionProvider.tsx:387](../../../src/client/src/shell/context/ContextConnectionProvider.tsx)) awaits `client.proposeInput` with no `try`/`catch`. Two call sites consume it without a `.catch`: [ChoicePromptDialog.tsx:141](../../../src/client/src/shell/context/ChoicePromptDialog.tsx) (`void onPropose(...).then(setVerdict)`) and [ContextPromptHost.tsx:180](../../../src/client/src/shell/context/ContextPromptHost.tsx). On rejection the verdict never updates, so the dialog keeps showing the last answer — which may enable a confirm button for a value the backend never accepted.

#### Acceptance Criteria

1. WHEN a proposal rejects THEN the dialog SHALL present the input as unvalidated rather than keeping the previous verdict.
2. WHEN a proposal rejects THEN the confirm affordance SHALL NOT remain enabled on the strength of a stale verdict.

### Requirement 4 — The context channel's failure mode should be decided once, not at each call site

**User Story:** As a developer adding a context-channel call, I want the rejection behaviour to be settled by the channel, so that I cannot forget it.

The three requirements above are one cause with three symptoms, and fixing them call-site by call-site leaves the next caller to rediscover it. Every method on the context channel — `describeProperties`, `setProperty`, `execute`, `onPropose`, `onSubmit` — has the same shape: `await client.x(...)` and return the response. Two of them already have neighbours that do catch ([ContextConnectionProvider.tsx:222](../../../src/client/src/shell/context/ContextConnectionProvider.tsx) and [:433](../../../src/client/src/shell/context/ContextConnectionProvider.tsx) both use `.catch(() => {})` for advisory calls), so the file already holds both conventions with nothing saying which applies when.

#### Acceptance Criteria

1. WHEN the channel's methods are reviewed THEN each SHALL either resolve to a value that expresses failure (the `ActionOutcome` shape several already return) or document that it rejects and why that is right for it.
2. WHEN a method resolves-with-failure THEN its callers SHALL NOT need a `.catch` for correctness.
3. WHEN a method is advisory and deliberately swallows faults THEN a comment SHALL say so, as the two existing sites do.

### Requirement 5 — Drift: one way to acquire a service client

Three shapes are in use for the same job. Four sites use `useMemo(() => createClient(S, transport), [transport])` — [AuthContext.tsx:49](../../../src/client/src/auth/AuthContext.tsx), [ProjectGridPage.tsx:27](../../../src/client/src/pages/ProjectGridPage.tsx), [ContextConnectionProvider.tsx:196](../../../src/client/src/shell/context/ContextConnectionProvider.tsx), [ExplorerTreePanel.tsx:477](../../../src/client/src/shell/panels/ExplorerTreePanel.tsx). Two use `useRef(createClient(S, transport))` — [useDiagramStream.ts:63](../../../src/client/src/diagrams/useDiagramStream.ts), [useToolboxItems.ts:17](../../../src/client/src/shell/panels/useToolboxItems.ts). One constructs inline per call — [LoginPage.tsx:22](../../../src/client/src/pages/LoginPage.tsx).

The `useRef` form has a real if small cost: the `createClient` argument is evaluated on **every render** and the result discarded, and `useDiagramStream` backs every open diagram tab, re-rendering on every delta. It is not a correctness bug — `transport` is memoised on a `[]`-stable callback and reads its token through a ref, so it never changes identity and the ref can never go stale. That is worth recording, because it is the reason the `useRef` form is safe and the reason nobody has noticed it.

#### Acceptance Criteria

1. WHEN a component needs a service client THEN it SHALL use one documented shape.
2. WHEN the choice is made THEN a comment SHALL record that `transport` is identity-stable, so the next reader does not have to re-derive why any of these work.

### Requirement 6 — Drift: placeholder markup is duplicated rather than shared

`PanelPlaceholder` ([PanelPlaceholder.tsx:32](../../../src/client/src/shell/panels/PanelPlaceholder.tsx)) renders `div.panel-placeholder > p.panel-placeholder-title + p.panel-placeholder-description`. Two real panels rebuild that markup by hand: [PropertyGridPanel.tsx:131](../../../src/client/src/shell/panels/PropertyGridPanel.tsx) and [DiagramTabsPanel.tsx:162](../../../src/client/src/shell/panels/DiagramTabsPanel.tsx).

This is deliberately filed as drift rather than a defect, and the nuance matters: `PanelPlaceholder` carries a `data-future-spec` attribute and a `TODO(diagram-ide-mockup)` saying the branch is scaffolding to be replaced. A real panel reusing it as-is would inherit mockup semantics. The finding is that the *markup contract* is copied three ways with nothing holding it together, not that the two panels should import a mockup component.

#### Acceptance Criteria

1. WHEN a panel shows an empty state THEN it SHALL use one shared component whose contract is not the mockup scaffolding.

### Requirement 7 — Drift: exports that nothing outside their file uses

Three symbols are exported and referenced only inside their own file: `normalizeLineEndings` ([TextEditorPanel.tsx:37](../../../src/client/src/editors/TextEditorPanel.tsx)), `descriptionFor` ([ChoicePromptDialog.tsx:19](../../../src/client/src/shell/context/ChoicePromptDialog.tsx)), `suggestionFor` ([ChoicePromptDialog.tsx:39](../../../src/client/src/shell/context/ChoicePromptDialog.tsx)).

A caution for whoever acts on this: **lane-scoped searches lie about this category.** `isTextTarget` and `structuralShortcutFor` in [canvas/interaction.ts](../../../src/client/src/canvas/interaction.ts) look unused inside `src/client/src` and are imported by ten files under `src/diagrams/*/client/`. Only a repository-wide search settles it, which is how the three above were confirmed.

#### Acceptance Criteria

1. WHEN a symbol is used only within its own file THEN it SHALL NOT be exported, unless a test imports it — in which case a comment SHALL say so.

### Requirement 8 — Drift: an index key where the list is a selection path

[PropertyGridPanel.tsx:151](../../../src/client/src/shell/panels/PropertyGridPanel.tsx) keys each level `key={index}`, and `chain` is the selection path, whose length and contents change whenever the selection does.

Recorded as drift rather than a defect after checking the mitigation: the rows inside are keyed by `property.id` ([PropertyGridPanel.tsx:164](../../../src/client/src/shell/panels/PropertyGridPanel.tsx)), so a changed property set remounts them. The residual risk is narrow — two selections producing the same property id at the same depth while a row is mid-edit, where `PropertyRow`'s sync effect is guarded by `if (!editing)` and would not overwrite the draft.

#### Acceptance Criteria

1. WHEN a level is keyed THEN the key SHALL be derived from the level's identity rather than its position.

## Non-Functional Requirements

### Reliability

- Each defect in Requirements 1-3 earns a test that drives the rejecting path and fails against today's code, per CLAUDE.md's rule that a found bug leaves a guard behind.

### Code Architecture and Modularity

- Requirement 4 is the durable half. Requirements 1-3 are symptoms; fixing only those leaves the cause.

## What this scan did not find, and one class that is unguarded

**Checked and clean.** Every `addEventListener` in the lane has a matching `removeEventListener` in the same file. No duplicated interface or type declarations within the lane. No effect in the lane runs unguarded before an early return in the way `AnsibleCanvas` does — the closest candidate, [TextEditorPanel.tsx:55](../../../src/client/src/editors/TextEditorPanel.tsx), marks tab-dirty with `false` before the `failed`/`loading` returns, which is a no-op rather than a fault; the two effects below it both guard on `model.loaded`.

**The unguarded class.** Nothing anywhere fails a build, a test or a review when a promise from the context channel is consumed without a rejection path. That is precisely how four of these arrived, in four different files, written at different times. It is the same shape as the phantom-token class — invisible to review, uniform across the codebase, and cheaply guarded once someone decides what the rule is. A lint rule (`no-floating-promises` with `void` not counted as handling) or a channel that cannot reject would both close it; the choice belongs with Requirement 4.

## Crossings, not followed

- **Nine of fourteen module stream hooks bypass the shared `useDiagramStream`.** Five use it (ansible-structure, azure-pipeline, c4, mindmap, wardley-map); nine hand-roll the same transport, retry and state — databricks, dependency-graph, helm-charts, rdf, owl, shacl, skos, sparql, timeline. The shared hook is in this lane ([diagrams/useDiagramStream.ts](../../../src/client/src/diagrams/useDiagramStream.ts)); the copies are in Agent 4's. Recorded here because the abstraction's adoption is a property of the abstraction. **Disclosure: `useSparqlStream` is one of the nine, and this agent wrote it earlier today by copying `useRdfStream` without checking whether a shared hook already existed.** That is the copied-file class doing exactly what Agent 5 described, committed by the person now reporting it.

## Sources

Scanned at `develop` = `469b1fbd`. Conventions checked against `src/.editorconfig`'s TypeScript sections; nothing that file documents is reported here.
