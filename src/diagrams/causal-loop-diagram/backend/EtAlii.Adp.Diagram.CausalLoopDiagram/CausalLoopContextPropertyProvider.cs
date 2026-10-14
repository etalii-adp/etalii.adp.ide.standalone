using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The property rows of a selected variable, link or loop, contributed as data the panel renders
/// without understanding (causal-loop-diagram Requirement 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rows are the definition's.</b> Which rows a selection shows, their labels, values, groups,
/// choices and the reason a row cannot be edited are derived from the bundled DISL definition's forms by
/// <see cref="CausalLoopDefinition"/> (causal-loop-disl-fbl Requirement 3.1); this provider holds no
/// rule of its own about them.
/// </para>
/// <para>
/// <b>A weight is recorded and never evaluated</b> (Requirement 8.3). It is parsed only far
/// enough to refuse text the document could not carry back, and blank clears it.
/// </para>
/// <para>
/// <b>An edit is still the module's command</b>, refused before it is made where the value could
/// not be written, and dispatched through the history stack until the body is written through the binding.
/// A loop's membership is accepted only as one of the cycles <see cref="CycleFinder"/> finds through its
/// variables that no other loop claims, or its own.
/// </para>
/// </remarks>
public sealed class CausalLoopContextPropertyProvider(
    IHistoryStackStore historyStacks, ICausalLoopDocumentStore documents) : IContextPropertyProvider
{
    /// <summary>What a reader sees on a variable.</summary>
    public const string VariableLabelProperty = "causal-loop.variable-label";

    /// <summary>The identifier links and loops refer to a variable by.</summary>
    public const string VariableIdProperty = "causal-loop.variable-id";

    /// <summary>The cause end of a link.</summary>
    public const string LinkFromProperty = "causal-loop.link-from";

    /// <summary>The effect end of a link.</summary>
    private const string LinkToProperty = "causal-loop.link-to";

    /// <summary>What the link asserts about the direction of the effect.</summary>
    public const string LinkPolarityProperty = "causal-loop.link-polarity";

    /// <summary>Whether the effect is marked as delayed.</summary>
    public const string LinkDelayedProperty = "causal-loop.link-delayed";

    /// <summary>The author's annotation of strength. Recorded, never evaluated.</summary>
    public const string LinkWeightProperty = "causal-loop.link-weight";

    /// <summary>An optional note on the link.</summary>
    private const string LinkLabelProperty = "causal-loop.link-label";

    /// <summary>A loop's identifier, as written.</summary>
    public const string LoopIdentifierProperty = "causal-loop.loop-identifier";

    /// <summary>A loop's descriptive name.</summary>
    private const string LoopNameProperty = "causal-loop.loop-name";

    /// <summary>What the arrows around the cycle say. Never editable: it is arithmetic.</summary>
    public const string LoopComputedProperty = "causal-loop.loop-computed";

    /// <summary>What the identifier claims, shown only where it differs from the arithmetic.</summary>
    public const string LoopStatedProperty = "causal-loop.loop-stated";

    /// <summary>The cycle the loop runs through.</summary>
    public const string LoopVariablesProperty = "causal-loop.loop-variables";

    /// <summary>How a cycle is written in the "Runs through" row: its variables, in order.</summary>
    private const string CycleSeparator = " → ";

    /// <summary>The value shown where the arithmetic cannot be taken.</summary>
    internal const string Undecidable = "undecidable";

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(
        ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return Rows([]);
        }

        // Derived from the DISL definition's forms, with the row ids of its x-cld block; nothing on a
        // document that could not be read.
        return Rows(CausalLoopDefinition.Rows(documents.GetOrLoad(target.ResolvedFullPath), target.ElementId));
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target, string propertyId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var entry = documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return ContextPropertyResult.Failure(
                "This causal loop diagram could not be read, so nothing can be edited until it is fixed.");
        }

        // Refused before any command is made, so the message names the value the user typed
        // rather than reporting a command failure - and so nothing unreadable is ever written.
        if (propertyId == LinkWeightProperty && ParseWeight(value) is null && value.Trim().Length > 0)
        {
            return ContextPropertyResult.Failure(
                $"'{value}' is not a number. A weight is an annotation this diagram records and never evaluates; leave it empty to remove it.");
        }

        if (propertyId == LinkPolarityProperty && ParsePolarity(value) is null)
        {
            return ContextPropertyResult.Failure($"'{value}' is not a polarity a link can state.");
        }

        if (propertyId == LoopVariablesProperty && LoopOf(entry.Model, target.ElementId) is { } chosenFor)
        {
            if (CycleChosen(entry.Model, chosenFor, value) is null)
            {
                return ContextPropertyResult.Failure(
                    $"'{value}' is not a cycle this loop can run through: choose one the arrows form through its variables that no other loop claims.");
            }

            // Choosing what is already there changes nothing, so it lands nothing on the undo stack.
            if (value == CycleText(chosenFor.Variables))
            {
                return ContextPropertyResult.Success;
            }
        }

        // The identifier is written as one word on the statement line, so it cannot hold
        // whitespace or a quote; the label may, because it is quoted. Refused before writing.
        if (propertyId == VariableIdProperty && (value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)))
        {
            return ContextPropertyResult.Failure(CausalLoopWriter.UnusableName);
        }

        var command = CommandFor(entry.Model, target, propertyId, value);
        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        // Through the project's history and out through the delta stream - never written here.
        var result = await historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static ICommand? CommandFor(
        CausalLoopModel model, ContextTarget target, string propertyId, string value)
    {
        var body = target.ResolvedFullPath;

        if (VariableOf(model, target.ElementId) is { } variable)
        {
            return propertyId switch
            {
                VariableLabelProperty => new SetVariableLabelCommand(body, variable.Id, value),
                // The identifier is what links and loops refer to, so restating it carries them.
                VariableIdProperty => new RenameVariableCommand(body, variable.Id, value),
                _ => null,
            };
        }

        if (LinkOf(model, target.ElementId) is { } link)
        {
            return propertyId switch
            {
                LinkLabelProperty => new SetLinkLabelCommand(body, link.From, link.To, value),
                LinkWeightProperty => new SetLinkWeightCommand(body, link.From, link.To, ParseWeight(value)),
                LinkDelayedProperty => new SetLinkDelayCommand(
                    body, link.From, link.To, string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)),
                LinkPolarityProperty when ParsePolarity(value) is { } polarity =>
                    new SetLinkPolarityCommand(body, link.From, link.To, polarity),
                _ => null,
            };
        }

        if (LoopOf(model, target.ElementId) is { } loop)
        {
            return propertyId switch
            {
                LoopNameProperty => new SetLoopNameCommand(body, loop.Identifier, value),

                // The identifier is the claim, so setting it is how an author accepts the
                // arithmetic - or insists on their own reading.
                LoopIdentifierProperty => new SetLoopIdentifierCommand(body, loop.Identifier, value),
                LoopVariablesProperty when CycleChosen(model, loop, value) is { } cycle =>
                    new SetLoopMembershipCommand(body, loop.Identifier, cycle),
                _ => null,
            };
        }

        return null;
    }

    private static CausalLoopVariable? VariableOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.VariableOf(elementId) is { } id
            ? model.Variables.FirstOrDefault(variable => variable.Id == id)
            : null;

    private static CausalLoopLink? LinkOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.LinkOf(elementId) is { } ends
            ? model.Links.FirstOrDefault(link => link.From == ends.From && link.To == ends.To)
            : null;

    private static CausalLoopLoop? LoopOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.LoopOf(elementId) is { } identifier
            ? model.Loops.FirstOrDefault(loop => loop.Identifier == identifier)
            : null;

    /// <summary>The cycle a "Runs through" value names, or null where the row did not offer it.</summary>
    private static IReadOnlyList<string>? CycleChosen(CausalLoopModel model, CausalLoopLoop loop, string value) =>
        MembershipChoices(model, loop).FirstOrDefault(choice => choice.Text == value).Cycle;

    /// <summary>
    /// What a loop may run through, each with the text the row shows for it: the loop's own cycle
    /// first, as the author wrote it, then each elementary cycle through any of its variables that
    /// no loop claims yet, in the cycle finder's stable order.
    /// </summary>
    /// <remarks>
    /// The current membership is offered even where the arrows no longer close it, so the row's
    /// value is always one of its choices; the validator reports that case separately. A found
    /// cycle that is the loop's own, entered at another variable, is the same loop and is not
    /// offered twice.
    /// </remarks>
    private static List<(string Text, IReadOnlyList<string> Cycle)> MembershipChoices(CausalLoopModel model, CausalLoopLoop loop)
    {
        List<(string Text, IReadOnlyList<string> Cycle)> choices = [(CycleText(loop.Variables), loop.Variables)];

        var claimed = model.Loops
            .Select(other => CycleFinder.CanonicalSignature(other.Variables))
            .ToHashSet(StringComparer.Ordinal);
        var members = loop.Variables.ToHashSet(StringComparer.Ordinal);

        foreach (var cycle in CycleFinder.Find(model).Cycles)
        {
            if (cycle.Any(members.Contains) && !claimed.Contains(CycleFinder.CanonicalSignature(cycle)))
            {
                choices.Add((CycleText(cycle), cycle));
            }
        }

        return choices;
    }

    private static string CycleText(IReadOnlyList<string> variables) => string.Join(CycleSeparator, variables);

    /// <summary>The weight a row was given, or null for none - which includes blank.</summary>
    private static double? ParseWeight(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
            ? weight
            : null;

    private static CausalLoopPolarity? ParsePolarity(string value) => value.Trim().ToUpperInvariant() switch
    {
        "POSITIVE" or "+" or "S" => CausalLoopPolarity.Positive,
        "NEGATIVE" or "-" or "O" => CausalLoopPolarity.Negative,
        "UNSTATED" or "" => CausalLoopPolarity.Unstated,
        _ => null,
    };

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(
        IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
