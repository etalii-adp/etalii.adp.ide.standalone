using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// The property rows of a selected variable, link or loop, contributed as data the panel renders
/// without understanding (causal-loop-diagram Requirement 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>A loop shows two polarities where they disagree, and one where they do not.</b> The stated
/// one is what the author's identifier claims; the computed one is what the arrows around the
/// cycle actually say. Requirement 3.3 forbids correcting one with the other, so the grid is
/// where a reader meets the disagreement without opening the problems panel - and the second row
/// appears only when there is something to see, because a permanent "Stated: reinforcing"
/// alongside "Computed: reinforcing" is noise that trains a reader to stop looking.
/// </para>
/// <para>
/// <b>A weight is recorded and never evaluated</b> (Requirement 8.3). It is parsed only far
/// enough to refuse text the document could not carry back, and blank clears it: this diagram
/// type states structure and simulates nothing, so the number is an author's annotation and
/// nothing here reads it as a coefficient.
/// </para>
/// <para>
/// <b>Every row that cannot be edited says why</b> (Requirement 8.5). A link's two ends are the
/// clearest case: they are the link's identity, so changing one would be making a different
/// link, and the row says that rather than presenting a box that silently does nothing.
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
    public const string LinkToProperty = "causal-loop.link-to";

    /// <summary>What the link asserts about the direction of the effect.</summary>
    public const string LinkPolarityProperty = "causal-loop.link-polarity";

    /// <summary>Whether the effect is marked as delayed.</summary>
    public const string LinkDelayedProperty = "causal-loop.link-delayed";

    /// <summary>The author's annotation of strength. Recorded, never evaluated.</summary>
    public const string LinkWeightProperty = "causal-loop.link-weight";

    /// <summary>An optional note on the link.</summary>
    public const string LinkLabelProperty = "causal-loop.link-label";

    /// <summary>A loop's identifier, as written.</summary>
    public const string LoopIdentifierProperty = "causal-loop.loop-identifier";

    /// <summary>A loop's descriptive name.</summary>
    public const string LoopNameProperty = "causal-loop.loop-name";

    /// <summary>What the arrows around the cycle say. Never editable: it is arithmetic.</summary>
    public const string LoopComputedProperty = "causal-loop.loop-computed";

    /// <summary>What the identifier claims, shown only where it differs from the arithmetic.</summary>
    public const string LoopStatedProperty = "causal-loop.loop-stated";

    /// <summary>The cycle the loop runs through.</summary>
    public const string LoopVariablesProperty = "causal-loop.loop-variables";

    private const string IdentityGroup = "Identity";
    private const string CausalityGroup = "Causality";
    private const string FeedbackGroup = "Feedback";

    private const string EndsAreTheIdentity =
        "A link is identified by the two variables it joins. Draw a new link on the canvas rather than changing an end here.";

    private const string ComputedFromTheArrows =
        "Counted from the polarity of the links around the cycle. Change an arrow to change this.";

    private const string MembershipOnTheDiagram =
        "The cycle a loop claims is edited on the canvas, where the path can be seen.";

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

        var entry = documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return Rows([]);
        }

        var model = entry.Model;

        if (VariableOf(model, target.ElementId) is { } variable)
        {
            return Rows(
            [
                new ContextPropertyDefinition(VariableLabelProperty, "Label", variable.Label, Group: IdentityGroup),
                new ContextPropertyDefinition(VariableIdProperty, "Identifier", variable.Id, Group: IdentityGroup),
            ]);
        }

        if (LinkOf(model, target.ElementId) is { } link)
        {
            return Rows(
            [
                new ContextPropertyDefinition(
                    LinkFromProperty, "Cause (from)", link.From,
                    ReadOnlyReason: EndsAreTheIdentity, Group: IdentityGroup),
                new ContextPropertyDefinition(
                    LinkToProperty, "Effect (to)", link.To,
                    ReadOnlyReason: EndsAreTheIdentity, Group: IdentityGroup),
                new ContextPropertyDefinition(LinkLabelProperty, "Note", link.Label, Group: IdentityGroup),

                // Polarity as a list rather than a line: there are exactly three things a link
                // can say, and one of them is that nobody said.
                new ContextPropertyDefinition(
                    LinkPolarityProperty, "Polarity", NameOf(link.Polarity),
                    ContextPropertyEditor.Choice, Group: CausalityGroup,
                    Candidates: [NameOf(CausalLoopPolarity.Positive), NameOf(CausalLoopPolarity.Negative), NameOf(CausalLoopPolarity.Unstated)]),
                new ContextPropertyDefinition(
                    LinkDelayedProperty, "Delayed", link.Delayed ? "true" : "false",
                    ContextPropertyEditor.Toggle, Group: CausalityGroup),
                new ContextPropertyDefinition(
                    LinkWeightProperty, "Weight", WeightText(link.Weight), Group: CausalityGroup),
            ]);
        }

        if (LoopOf(model, target.ElementId) is { } loop)
        {
            var computed = LoopPolarity.Of(model, loop.Variables);
            var stated = loop.ClaimsReinforcing;
            var disagrees = stated is not null
                && computed is LoopPolarityResult.Reinforcing or LoopPolarityResult.Balancing
                && stated != (computed == LoopPolarityResult.Reinforcing);

            List<ContextPropertyDefinition> rows =
            [
                new ContextPropertyDefinition(
                    LoopIdentifierProperty, "Identifier", loop.Identifier, Group: IdentityGroup),
                new ContextPropertyDefinition(LoopNameProperty, "Name", loop.Name, Group: IdentityGroup),
                new ContextPropertyDefinition(
                    LoopComputedProperty, "Computed polarity", NameOf(computed),
                    ReadOnlyReason: ComputedFromTheArrows, Group: FeedbackGroup),
            ];

            // Only where they differ: a stated row that always agrees is noise, and noise is
            // what a reader learns to skip past.
            if (disagrees)
            {
                rows.Add(new ContextPropertyDefinition(
                    LoopStatedProperty,
                    "Stated polarity",
                    stated == true ? "reinforcing" : "balancing",
                    ReadOnlyReason: $"'{loop.Identifier}' claims this, and the arrows say {NameOf(computed)}. Neither is corrected for you: change the identifier or change an arrow.",
                    Group: FeedbackGroup));
            }

            rows.Add(new ContextPropertyDefinition(
                LoopVariablesProperty, "Runs through", string.Join(" → ", loop.Variables),
                ReadOnlyReason: MembershipOnTheDiagram, Group: FeedbackGroup));

            return Rows(rows);
        }

        return Rows([]);
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

    /// <summary>The weight as the grid shows it: empty where none was written.</summary>
    /// <remarks>
    /// Absent and zero are different things - an unweighted link is not a link of weight zero -
    /// so an unwritten weight is an empty row rather than a "0" the author never typed.
    /// </remarks>
    private static string WeightText(double? weight) =>
        weight is { } value ? value.ToString(CultureInfo.InvariantCulture) : "";

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

    private static string NameOf(CausalLoopPolarity polarity) => polarity switch
    {
        CausalLoopPolarity.Positive => "positive",
        CausalLoopPolarity.Negative => "negative",
        _ => "unstated",
    };

    private static string NameOf(LoopPolarityResult polarity) => polarity switch
    {
        LoopPolarityResult.Reinforcing => "reinforcing",
        LoopPolarityResult.Balancing => "balancing",
        _ => Undecidable,
    };

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(
        IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
