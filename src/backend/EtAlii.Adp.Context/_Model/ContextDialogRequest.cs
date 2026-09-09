namespace EtAlii.Adp.Context;

/// <summary>A dialog asking the user for a single value, described entirely as data.</summary>
/// <param name="InlineLabelElementId">
/// The element whose visible label this value IS, when it is one - which lets a canvas render
/// the prompt in place of that label instead of as a dialog. Empty, the default, means an
/// ordinary dialog and is the right answer for most prompts: the same shape asks for run-if
/// values, cluster keys, prefix declarations and predicate IRIs, none of which is the text on
/// screen. Only the provider knows which of its own prompts is which, which is why this is said
/// here rather than guessed from an action id by shared client code.
/// <para>
/// Trailing and defaulted on purpose: every existing construction across the modules keeps
/// compiling untouched, so a module adopts inline editing by adding one argument to one call
/// rather than by being migrated.
/// </para>
/// </param>
/// <param name="CommitActionId">
/// The action this value commits under, when the prompt is asked by one action and applied by
/// another. Empty, the default, means the action the user invoked - which is every prompt that
/// existed before this field, because a prompt normally belongs to the action that raised it.
/// <para>
/// What it is for: <b>create the thing, then edit its label in place.</b> A provider that adds
/// an element can dispatch the add at once, name it from its siblings, and return an input
/// request naming the NEW element and its own rename action - so the user gets an inline editor
/// over a node that exists instead of a dialog about a node that does not. Committing then
/// renames rather than adding a second one, which is what happens if the invoked action is
/// allowed to stand.
/// </para>
/// <para>
/// It stays inside the backend and never reaches the wire: the client only needs to know which
/// interaction it is answering, and the interaction is where this is remembered.
/// </para>
/// </param>
public sealed record ContextInputRequest(
    string Title,
    string Icon,
    string FieldLabel,
    string InitialValue,
    string ConfirmLabel,
    string InlineLabelElementId = "",
    string CommitActionId = "");

/// <summary>A dialog asking the user to confirm or cancel, described entirely as data.</summary>
public sealed record ContextConfirmationRequest(
    string Title,
    string Icon,
    string Message,
    string ConfirmLabel,
    bool Danger);
