namespace EtAlii.Adp.Common;

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
public sealed record ContextInputRequest(
    string Title,
    string Icon,
    string FieldLabel,
    string InitialValue,
    string ConfirmLabel,
    string InlineLabelElementId = "");

/// <summary>A dialog asking the user to confirm or cancel, described entirely as data.</summary>
public sealed record ContextConfirmationRequest(
    string Title,
    string Icon,
    string Message,
    string ConfirmLabel,
    bool Danger);
