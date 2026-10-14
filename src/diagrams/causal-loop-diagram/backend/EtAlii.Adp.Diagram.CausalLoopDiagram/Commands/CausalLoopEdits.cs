using System.Globalization;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// What every edit command of this module shares besides its run: the sentences it refuses with, each in
/// today's wording (causal-loop-disl-fbl Requirement 5.6), the checks behind them, the ids the definition
/// gives what an edit names, and the one way a change is written - as a <see cref="DislChange"/> through
/// <see cref="CausalLoopDefinition.Apply"/>, which the binding plans as splices (Requirement 5.1).
/// </summary>
/// <remarks>
/// <b>Nothing here builds a statement.</b> Which words a variable, a link or a loop is written with, where
/// a new one goes and which references a rename carries are the binding's; what stays here is what the
/// command was asked and why it may not be done.
/// </remarks>
public static class CausalLoopEdits
{
    /// <summary>What every edit of a document that could not be read is told.</summary>
    public const string Unreadable = "This causal loop diagram could not be read, so nothing can be edited until it is fixed.";

    /// <summary>What a gesture is told when the thing it names is not in the document.</summary>
    public const string NoSuchVariable = "No variable of that name is in this diagram, so there is nothing to change.";

    /// <inheritdoc cref="NoSuchVariable" />
    public const string NoSuchLink = "No link between those two variables is in this diagram, so there is nothing to change.";

    /// <inheritdoc cref="NoSuchVariable" />
    public const string NoSuchLoop = "No loop of that identifier is in this diagram, so there is nothing to change.";

    /// <summary>A name that would collide with one already stated.</summary>
    public const string AlreadyDeclared = "A variable of that name is already declared in this diagram.";

    /// <summary>A name the format cannot round-trip.</summary>
    public const string UnusableName = "A name needs at least one character and no whitespace, because a statement is read as words on one line.";

    /// <summary>A label or loop name the format cannot round-trip: it is written between quotes and has no escape for one.</summary>
    public const string UnusableText = "A label or loop name cannot hold a double quote, because it is written between double quotes.";

    /// <summary>A link drawn twice.</summary>
    public const string LinkTaken = "That link is already stated in this diagram.";

    /// <summary>A loop identifier used twice.</summary>
    public const string LoopTaken = "A loop of that identifier is already stated in this diagram.";

    /// <summary>A loop through nothing.</summary>
    private const string LoopThroughNothing = "A loop runs through at least one variable.";

    /// <summary>A loop through a variable the document does not declare.</summary>
    private static string NotDeclared(string missing) => $"'{missing}' is not declared in this diagram, so a loop cannot run through it.";

    /// <summary>A name the line-oriented format can round-trip: one word, at least one character.</summary>
    public static bool IsUsableName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > 0 && !name.Any(char.IsWhiteSpace) && !name.Contains('"', StringComparison.Ordinal);
    }

    /// <summary>A label or loop name the format can round-trip: no double quote.</summary>
    public static bool IsUsableText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return !text.Contains('"', StringComparison.Ordinal);
    }

    /// <summary>The definition's id of the variable written <paramref name="name"/>.</summary>
    public static string VariableId(string name) => $"variable:{name}";

    /// <summary>The definition's id of the link from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static string LinkId(string from, string to) => $"link:{from}|{to}";

    /// <summary>The definition's id of the loop <paramref name="identifier"/>.</summary>
    public static string LoopId(string identifier) => $"loop:{identifier}";

    /// <summary>
    /// A free (id, label) pair for a variable nobody named - what the toolbox drop and the canvas "Add
    /// variable" reach for: the lowest number no variable is named for, as the definition's
    /// <c>addVariableHere</c> names it.
    /// </summary>
    public static (string Id, string Label) NextVariableName(CausalLoopModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        for (var number = 1; number < 100000; number++)
        {
            var id = string.Create(CultureInfo.InvariantCulture, $"variable{number}");
            if (!model.Declares(id))
            {
                return (id, string.Create(CultureInfo.InvariantCulture, $"Variable {number}"));
            }
        }

        return ("variable", "Variable");
    }

    /// <summary>The stored form of <paramref name="polarity"/>: none for one nobody wrote.</summary>
    public static string? Stored(CausalLoopPolarity polarity) => polarity switch
    {
        CausalLoopPolarity.Positive => "positive",
        CausalLoopPolarity.Negative => "negative",
        _ => null,
    };

    /// <summary>Writes <paramref name="changes"/> into <paramref name="body"/> as one edit.</summary>
    private static EditOutcome Write(CausalLoopBody body, params DislChange[] changes) =>
        CausalLoopDefinition.Apply(body, new DislTransaction(changes, [], null));

    /// <summary>Sets <paramref name="attributes"/> on the element <paramref name="id"/> of <paramref name="type"/>.</summary>
    public static EditOutcome Set(CausalLoopBody body, string id, string type, params (string Name, object? Value)[] attributes) =>
        Write(body, new DislChange.Set(id, type, Values(attributes)));

    /// <summary>Removes the element <paramref name="id"/>, and what the binding's removal cascades to.</summary>
    public static EditOutcome Remove(CausalLoopBody body, string id) => Write(body, new DislChange.Remove(id));

    /// <summary>Whether <paramref name="model"/> states a link from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static bool HasLink(CausalLoopModel model, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Links.Any(link => string.Equals(link.From, from, StringComparison.Ordinal) && string.Equals(link.To, to, StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="model"/> states a loop of <paramref name="identifier"/>.</summary>
    public static bool HasLoop(CausalLoopModel model, string identifier)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Loops.Any(loop => string.Equals(loop.Identifier, identifier, StringComparison.Ordinal));
    }

    /// <summary>Why a loop may not run through <paramref name="variables"/>; null when it may.</summary>
    public static string? MembershipRefusal(CausalLoopModel model, IReadOnlyList<string> variables)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(variables);
        if (variables.Count < 1) return LoopThroughNothing;
        return variables.FirstOrDefault(variable => !model.Declares(variable)) is { } missing ? NotDeclared(missing) : null;
    }

    /// <summary>The variables a loop runs through, as the elements the definition names them by.</summary>
    public static List<object?> Members(IReadOnlyList<string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        return [.. variables.Select(variable => (object?)VariableId(variable))];
    }

    /// <summary>Why the variable <paramref name="name"/> labelled <paramref name="label"/> may not be declared; null when it may.</summary>
    public static string? AdditionRefusal(CausalLoopModel model, string name, string label)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!IsUsableName(name)) return UnusableName;
        if (model.Declares(name)) return AlreadyDeclared;
        return IsUsableText(label) ? null : UnusableText;
    }

    /// <summary>Declares the variable <paramref name="name"/> with <paramref name="label"/>, where the binding places a new one.</summary>
    public static EditOutcome AddVariable(CausalLoopBody body, string name, string label) =>
        Write(body, new DislChange.Create("Variable", VariableId(name), Values(("name", name), ("label", Text(label))), null, null));

    /// <summary>Claims the loop <paramref name="identifier"/> named <paramref name="title"/> through <paramref name="variables"/>, where the binding places a new one.</summary>
    public static EditOutcome AddLoop(CausalLoopBody body, string identifier, string title, IReadOnlyList<string> variables) =>
        Write(body, new DislChange.Create("Loop", LoopId(identifier), Values(("identifier", identifier), ("title", title), ("members", Members(variables))), null, null));

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    /// <summary>Text the user cleared, as no value, so the binding removes the word rather than writing it empty.</summary>
    public static string? Text(string text) => text.Length > 0 ? text : null;
}
