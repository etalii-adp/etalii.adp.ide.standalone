# Design Document

## Overview

The Add dialog learns one condition: when the chosen type registers a folder, the name is not the user's to give, and the registration is written as the bare `.adp` inside that folder. Thirteen shipped registrations that carry a name are renamed to match, and two guards keep the shape from coming back.

**The most important finding of this design is that the read path needs no change at all.** Requirement 4 asks that a user's existing `infrastructure/structure.adp` keep opening, and it will, because production already routes by content rather than by name:

- `DiagramFilePair.IsRegistrationFile` is exactly `path.EndsWith(".adp")` (`DiagramFilePair.cs:278`), which a bare `.adp` satisfies as readily as a named one.
- `HierarchyModel`'s folder-subject probe routes *whatever* registration it finds and asks the definition — `_router.Route(...) is DiagramRouted { Definition.HasFolderSubject: true }` (`HierarchyModel.cs:645`). The file name never enters the decision.

So tolerance is free, and the only place in the tree that ties folder-subject-ness to a file *name* is a test stand-in (`EntryDiagramStates.Tests.cs:105`). This design constrains creation and leaves reading exactly as it is; tightening the read path would be a separate change the user would have to ask for.

The survey in the requirements was re-measured against develop `e2e854f7` and holds: two folder-subject definitions (`ansible/structure`, `helm/chart`), 116 `.adp` files, 14 folder-subject registrations, 1 already bare, **13 to correct**.

## Steering Document Alignment

### Technical Standards (tech.md)

- **The backend owns the filesystem; the client renders prompts described as data.** The new condition therefore ships as two fields on an existing message, not as logic the client works out for itself. The client learns *that* a name is not wanted and *what to say instead*; it never learns what a folder-subject type is.
- **No new dialog.** Requirement 1.1 asks for a condition inside the existing choice dialog, and the contract already carries per-option data for exactly this reason.
- **One predicate, no module list.** `DiagramDefinition.HasFolderSubject` (`DiagramDefinition.cs:92`) is the whole test, so a third folder-subject module inherits the behaviour with no new code — and the guard test derives its expectations from the deployed catalog for the same reason.

### Project Structure (structure.md)

Touched: `src/api/context.proto`; `src/backend/EtAlii.Adp.Backend/Hierarchy/` and `Context/_Model/`; `src/client/src/shell/context/`; the example trees under `src/diagrams/*/examples/` and `src/examples/`. No new project, no new folder.

## Code Reuse Analysis

### Existing Components to Leverage

- **`ContextOption.suggested_value`** (`context.proto:337`) with `ContextOptionNode.SuggestedValue` and the client's `suggestionFor` / `descriptionFor` helpers (`ChoicePromptDialog.tsx:19,45`). This is the precedent the requirements pointed at: per-option data computed while the prompt is built, so picking an option costs no round trip. The new fields sit beside it and are read the same way.
- **`DiagramOptionTree.Build(definitions, suggest)`** already takes a per-definition callback and already skips groups when filling it (`DiagramOptionTree.cs:95`). The annotation seam replaces that callback rather than adding a third.
- **`ContextAction.unavailable_reason`** (`context.proto:294`, commented there as the disabled item's tooltip) and its backend counterpart `ContextActionDefinition(available, reason)`, used by this very provider for the empty-catalog case (`AddDiagramContextActionProvider.cs:83`). An option that cannot be chosen reuses that field's **name and meaning** rather than inventing a second idiom for the same thing one message over.
- **`DiagramFileRouter.Route` and `HasFolderSubjectTypes`** (`DiagramFileRouter.cs:63`) — how a registration's type is discovered, including the catalog probe that keeps deployments without folder-subject types from paying any reads.
- **`CreateDiagramFileCommand`** — the same command both existing branches use, which is what makes the new file one undo away.
- **`DiagramFileName.Extension`** — the one spelling of `.adp`.

### Integration Points

- `AddDiagramContextActionProvider.ExecuteAsync` (prompt) and `CommitAsync` (write), whose check order the class comment says is deliberately pinned by tests; this design inserts into that order rather than rearranging it.
- `ChoicePromptDialog`, which already supports a prompt with no name field at all — that is how registering an existing file works (`NameField: null`, `AddDiagramContextActionProvider.cs:169`). What is new is suppression that varies *within* one prompt.
- `ExampleRegistrationTests`, which walks both example trees and must stay green across the renames.

## Architecture

### The contract: two fields on `ContextOption`

Requirement 1.5 forbids a round trip when the selection changes, so the client must already hold the answer for every option before the user clicks. `name_field` is a property of the whole prompt (`context.proto:361`) while the need for a name belongs to the option, so the override rides the option. Field numbers 8 and 9 are the next free ones.

```
message ContextOption {
  // ... 1-7 unchanged ...

  // Non-empty: this option takes no name, and this sentence is shown where the name
  // field would be. Empty: the prompt's name_field applies, exactly as before.
  string name_suppressed_reason = 8;

  // Non-empty: the option is shown but cannot be chosen, and this sentence says why -
  // the same field name and meaning ContextAction already carries for a disabled item.
  string unavailable_reason = 9;
}
```

Each field carries the condition **and** its explanation in one value, so a client cannot render the suppression without rendering the reason. Requirement 1.3 then holds by construction rather than by convention: there is no way to express "no name" without saying why.

`ContextOptionNode` gains the mirror, keeping the record's all-optional tail:

```csharp
public sealed record ContextOptionNode(
    string Id,
    string Label,
    bool Selectable,
    IReadOnlyList<ContextOptionNode>? Children = null,
    string SuggestedValue = "",
    string Description = "",
    string Icon = "",
    string NameSuppressedReason = "",
    string UnavailableReason = "");
```

No new RPC and no new stream — two added fields on a message that already flows down the open Watch stream.

### Building the prompt

`DiagramOptionTree.Build`'s `Func<DiagramOrigin, string>? suggest` becomes one annotation callback, so the three per-option values are decided in one place instead of three:

```csharp
public sealed record ContextOptionAnnotations(
    string SuggestedValue = "",
    string NameSuppressedReason = "",
    string UnavailableReason = "");

public static IReadOnlyList<ContextOptionNode> Build(
    IReadOnlyList<DiagramDefinition> definitions,
    Func<DiagramDefinition, ContextOptionAnnotations>? annotate = null)
```

Taking the `DiagramDefinition` rather than the `DiagramOrigin` is what lets the callback read `HasFolderSubject` at all. Groups keep getting nothing, as they do today. Both existing call sites are updated: the file-registration branch passes no callback, and the folder branch passes one.

For a folder target, the provider computes the folder's existing folder-subject registration **once** and then annotates each definition:

| definition | suggested_value | name_suppressed_reason | unavailable_reason |
|---|---|---|---|
| document-subject | `DiagramFileName.Suggest(...)`, as today | empty | empty |
| folder-subject, folder unregistered | empty — there is nothing to suggest | the sentence below | empty |
| folder-subject, folder already registered | empty | the sentence below | names the existing file |

The two sentences, which are the user-visible half of this spec:

- **Suppressed name**: *"This type registers the folder itself, so the registration is created as `.adp` inside it."*
- **Unavailable**: *"This folder is already registered by `structure.adp`."* — naming the file the user can go and look at, whether it is the bare `.adp` or the legacy named one (Requirement 2.3).

The prompt-level `NameField` stays exactly as it is. It has to: file-subject types still need it, and the whole point of the per-option override is that one dialog serves both.

### Finding an existing registration

```csharp
/// The folder-subject registration already in this folder, or null. Name-agnostic by
/// design: it routes what it finds, so the legacy named shape counts as taken too.
private string? FolderSubjectRegistrationIn(string folder)
```

It short-circuits on `_router.HasFolderSubjectTypes` before touching the disk — the same probe `HierarchyModel` uses — then lists the folder's own `*.adp` files (top level only, no recursion) and routes each until one answers `HasFolderSubject`. Cost is one directory listing plus the reads of that folder's registrations, and zero in a deployment with no folder-subject types.

This needs `DiagramFileRouter` in the provider's constructor, which it does not have today. The service is already registered — `HierarchyModel` takes it — so container resolution is unaffected; the change is confined to the two test files that construct the provider by hand (`AddDiagramContextActionProvider.Tests.cs`, `AddDiagramContextActionProvider.Registration.Tests.cs`). This is deliberately a required dependency rather than an optional one: an optional router would silently degrade the collision check to "never collides", which is the failure mode Requirement 2.2 exists to prevent.

### Committing

`CommitAsync`'s existing order is kept — folder re-checked, option resolved, then the name — with the folder-subject case branching after the option is known:

1. **Folder still exists** — unchanged.
2. **Option resolves to a definition** — unchanged.
3. **If `definition.HasFolderSubject`**:
   - The submitted `text` is **ignored**, not refused. A stale client that never hid the field sends its own suggestion, and refusing would strand the user in front of a dialog that cannot succeed. A non-empty `text` is logged at Warning, because it means a client is out of step and that should be visible rather than silent.
   - `ValidateName` is skipped entirely. It must be: it rejects an empty base name with "Enter a name." precisely so that nothing creates a file called `.adp` (`AddDiagramContextActionProvider.cs:195`) — the exact file this path now creates on purpose.
   - `FolderSubjectRegistrationIn(folder)` is consulted again. Non-null means refusal, naming the existing file; nothing is overwritten. Re-checking here rather than trusting the greyed-out option is what makes Requirement 2.2 hold against a race or a scripted client.
   - `fileName = DiagramFileName.Extension` — the bare `.adp`, and the one spelling of it.
4. **Otherwise** the file-subject path runs untouched, including its suggestion and its sibling handling.
5. The sibling branch cannot apply to a folder-subject type: discovery refuses `HasFolderSubject && HasDocumentSibling` outright (`DiagramDefinitionDiscovery.cs:75`). The branch is left where it is, unreachable for these types by construction rather than by a new condition.
6. The same `CreateDiagramFileCommand` writes it, so the new registration is one undo away like every other.

### The client

`ChoicePromptDialog` gains one derived value beside the two it already has:

```ts
/** Why the selected option takes no name, or empty when it takes one. */
function nameSuppressionFor(options: ContextOption[], id: string | null): string
```

- When it returns a sentence, the name field's row renders that sentence in its place and submit sends no name. `canSubmit` (`ChoicePromptDialog.tsx:146`) must stop requiring a name in that state, or the dialog would offer no name and then refuse to submit without one.
- Switching back to a file-subject option restores the field, with its `suggestedValue`, through the existing `touched` logic (`ChoicePromptDialog.tsx:167`) — no round trip, no dialog close.
- An option carrying `unavailableReason` renders greyed and is not choosable, with the reason shown the way an unavailable action's is. Double-click-to-submit (`ChoicePromptDialog.tsx:321`) must respect it too.

### The migration

Thirteen `git mv`es, listed in the requirements' survey, in both the module and showcase copies. Content is untouched — this is a rename, so the MIME line and its terminator stay byte-identical (Requirement 3.2). The example-replication guard is gone, so the two copies need not move in one commit; both must nonetheless end up right, because both are material a reader opens.

### The test stand-in at `EntryDiagramStates.Tests.cs:105`

`static bool DeclaresFolderSubject(string registration) => registration == "structure.adp";` is a test's stand-in for routing, not a production rule — production reads the MIME (see the Overview). It is therefore **kept as coverage of the legacy named shape**, and a bare-`.adp` case is added beside it, so both shapes are pinned and Requirement 4.1 has a test rather than a promise.

## Data Models

Nothing persisted changes. A registration is still one MIME line in one file; only the file's name changes, and only for folder-subject types. The model changes are the two proto fields, their `ContextOptionNode` mirror, and the `ContextOptionAnnotations` record that carries them out of the option-tree builder.

## Error Handling

### Error Scenarios

1. **A name is submitted for a folder-subject type** (stale or scripted client): ignored, logged at Warning, bare `.adp` written. The user's Add succeeds, which is the kinder failure.
2. **The folder is already registered**: refused with a sentence naming the existing registration; nothing overwritten. Offered as unavailable in the dialog first, so the refusal is the backstop rather than the user's first encounter with it.
3. **An unreadable registration sits in the folder**: it routes to `DiagramUnreadable`, which is not a folder-subject type, so the Add proceeds. Neutral, never an exception out of the scan — the same posture `HierarchyModel` takes for the same reason.
4. **Unknown option id**: existing behaviour, unchanged — logged and refused with "That diagram type is not available."
5. **The folder vanished while the dialog was open**: existing behaviour, unchanged.

## Testing Strategy

### Unit Testing

- `DiagramOptionTree`: a folder-subject definition carries a suppression sentence and no suggestion; a document-subject one carries a suggestion and no suppression; groups carry neither.
- `AddDiagramContextActionProvider`: the created file is exactly `.adp` (Requirement 5.2); a submitted name is ignored rather than honoured; a folder already holding a bare `.adp` refuses, and so does one holding a legacy named folder-subject registration; the file-subject path is unchanged, suggestion included.
- `EntryDiagramStates`: the legacy named shape and the bare shape both resolve.

### Integration Testing

Add `ansible/structure` to a folder through the context flow end to end, assert the folder holds `.adp` and the entry's diagram state changes, then undo and assert it is gone.

### Client Testing

`ChoicePromptDialog`: selecting a folder-subject option replaces the field with its sentence and submits with no name; switching back restores the field and its suggestion without a round trip; an option with `unavailableReason` cannot be chosen by click or double-click.

### End-to-End / Guard Testing

A test walking `src/diagrams/*/examples/` and `src/examples/` fails, naming the file, if any folder-subject registration carries a name (Requirement 5.1). Folder-subject-ness comes from the deployed catalog, not a hard-coded module list, so a third such module is covered the day it arrives.

The four gates stay green: `dotnet test` and `dotnet format style --verify-no-changes --severity info` from `src/backend`, `npm test` and `npm run typecheck` from `src/client`.

## Sequencing

Requirement 6, made concrete. Two phases, because the renames touch example trees another agent owns:

- **Phase 1 — the dialog** (Requirements 1, 2, 5.2): proto, `ContextOptionNode`, `DiagramOptionTree`, the provider, the client, and the creation guard. Touches no example file and can land whenever it is ready.
- **Phase 2 — the renames** (Requirements 3, 5.1): the thirteen `git mv`es and the shipped-examples guard, after `ansible-refinements` has finished with the ansible tree. A rename racing another agent's edits to the same folders is a merge conflict by construction, and waiting costs less than resolving one.
