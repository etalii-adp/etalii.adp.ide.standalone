using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

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
/// <b>So is what an edited row does</b> (causal-loop-disl-fbl Requirement 7.2): the row's form item at commit,
/// through <see cref="FormCommit"/>, refuses the value or says what it writes - a weight's and a polarity's
/// <c>parse</c>, a loop's members chosen among the item's <c>options</c> and judged by its <c>validate</c> - and
/// an identifier is judged by the definition's change rules. What is written is then dispatched as the
/// module's command for that row, through the history stack, as it always was.
/// </para>
/// <para>
/// The scope, the could-not-be-read guard and the dispatch are the shared frame's (code-structure
/// FR-034); this says which rows a selection has and which command a value is.
/// </para>
/// </remarks>
public sealed class CausalLoopContextPropertyProvider(
    IHistoryStackStore historyStacks, ICausalLoopDocumentStore documents)
    : DiagramPropertyProvider<CausalLoopBody, CausalLoopModel>(historyStacks, documents)
{
    /// <summary>What a reader sees on a variable.</summary>
    public const string VariableLabelProperty = "causal-loop.variable-label";

    /// <summary>The identifier links and loops refer to a variable by.</summary>
    public const string VariableIdProperty = "causal-loop.variable-id";

    /// <summary>The cause end of a link.</summary>
    public const string LinkFromProperty = "causal-loop.link-from";

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

    /// <summary>The value shown where the arithmetic cannot be taken.</summary>
    internal const string Undecidable = "undecidable";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the DISL definition's forms, with the row ids of its x-cld block; nothing on a
    /// document that could not be read.
    /// </remarks>
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string elementId) =>
        CausalLoopDefinition.Rows(entry, elementId);

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        // The row's form item at commit: refused before any command is made, so the message names the value
        // the user typed rather than reporting a command failure, and nothing unreadable is ever written.
        var model = entry.Model;
        var committed = CausalLoopDefinition.Row(entry.Document, target.ElementId, propertyId, value);
        if (committed is { WasApplied: false })
        {
            return DiagramPropertyChange.Refused(committed.Refusal!);
        }

        // The identifier is written as one word on the statement line: the definition's variableNameUsable says which.
        // A name already taken is refused by the command, as it always was, so the record dispatched stays today's.
        if (propertyId == VariableIdProperty && CausalLoopDefinition.ChangeRefusal(entry, target.ElementId, "name", value, "variableNameUsable") is { } unusable)
        {
            return DiagramPropertyChange.Refused(unusable);
        }

        // Choosing the members a loop already runs through changes nothing, so it lands nothing on the undo stack.
        if (propertyId == LoopVariablesProperty && committed is not null && LoopOf(model, target.ElementId) is { } chosenFor
            && Written(committed, "members") is IReadOnlyList<object?> chosen && chosen.SequenceEqual(chosenFor.Variables))
        {
            return DiagramPropertyChange.Unchanged;
        }

        // Through the project's history and out through the delta stream - never written here.
        return committed is not null && CommandFor(model, target, propertyId, committed) is { } command
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.");
    }

    /// <summary>The module's command that writes what <paramref name="committed"/> writes to the row <paramref name="propertyId"/>.</summary>
    private static ICommand? CommandFor(
        CausalLoopModel model, ContextTarget target, string propertyId, DislTransaction committed)
    {
        var body = target.ResolvedFullPath;

        if (VariableOf(model, target.ElementId) is { } variable)
        {
            return propertyId switch
            {
                VariableLabelProperty => new SetVariableLabelCommand(body, variable.Id, Text(committed, "label")),
                // The identifier is what links and loops refer to, so restating it carries them.
                VariableIdProperty => new RenameVariableCommand(body, variable.Id, Text(committed, "name")),
                _ => null,
            };
        }

        if (LinkOf(model, target.ElementId) is { } link)
        {
            return propertyId switch
            {
                LinkLabelProperty => new SetLinkLabelCommand(body, link.From, link.To, Text(committed, "note")),
                LinkWeightProperty => new SetLinkWeightCommand(body, link.From, link.To, Written(committed, "weight") as double?),
                LinkDelayedProperty => new SetLinkDelayCommand(body, link.From, link.To, Written(committed, "delayed") is true),
                LinkPolarityProperty => new SetLinkPolarityCommand(body, link.From, link.To, Written(committed, "polarity") switch
                {
                    "positive" => CausalLoopPolarity.Positive,
                    "negative" => CausalLoopPolarity.Negative,
                    _ => CausalLoopPolarity.Unstated,
                }),
                _ => null,
            };
        }

        if (LoopOf(model, target.ElementId) is { } loop)
        {
            return propertyId switch
            {
                LoopNameProperty => new SetLoopNameCommand(body, loop.Identifier, Text(committed, "title")),

                // The identifier is the claim, so setting it is how an author accepts the
                // arithmetic - or insists on their own reading.
                LoopIdentifierProperty => new SetLoopIdentifierCommand(body, loop.Identifier, Text(committed, "identifier")),
                LoopVariablesProperty when Written(committed, "members") is IReadOnlyList<object?> members =>
                    new SetLoopMembershipCommand(body, loop.Identifier, [.. members.Select(member => member as string ?? "")]),
                _ => null,
            };
        }

        return null;
    }

    /// <summary>The value <paramref name="committed"/> writes to <paramref name="attribute"/>; null for none.</summary>
    private static object? Written(DislTransaction committed, string attribute) =>
        CausalLoopDefinition.Writes(committed, attribute, out var value) ? value : null;

    /// <summary>The text <paramref name="committed"/> writes to <paramref name="attribute"/>; empty for none.</summary>
    private static string Text(DislTransaction committed, string attribute) => Written(committed, attribute) as string ?? "";

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
}
