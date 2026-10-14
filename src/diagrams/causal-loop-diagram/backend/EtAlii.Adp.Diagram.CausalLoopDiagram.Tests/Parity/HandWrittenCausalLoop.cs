using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows as the module wrote them by hand before they were
/// derived from the DISL definition (causal-loop-disl-fbl task 5), kept verbatim as the oracle the derived
/// ones are compared with, record for record.
/// </summary>
/// <remarks>
/// Copied from <c>CausalLoopToolboxProvider.Items</c>, <c>CausalLoopContextActionProvider.DiscoverAsync</c> and
/// <c>CausalLoopContextPropertyProvider.DescribeAsync</c> after their body checks, with the ids those classes
/// kept private written out. It goes with the parser (task 6).
/// </remarks>
internal static class HandWrittenCausalLoop
{
    private const string RenameLoopActionId = "causal-loop.rename-loop";
    private const string LinkToProperty = "causal-loop.link-to";
    private const string LinkLabelProperty = "causal-loop.link-label";
    private const string LoopNameProperty = "causal-loop.loop-name";

    private const string IdentityGroup = "Identity";
    private const string CausalityGroup = "Causality";
    private const string FeedbackGroup = "Feedback";

    private const string EndsAreTheIdentity =
        "A link is identified by the two variables it joins. Draw a new link on the canvas rather than changing an end here.";

    private const string ComputedFromTheArrows =
        "Counted from the polarity of the links around the cycle. Change an arrow to change this.";

    private const string CycleSeparator = " → ";

    /// <summary>The palette, as <c>CausalLoopToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        new(
            "causal-loop.toolbox.variable",
            "Variable",
            "mdi-circle-outline",
            "Drop on the canvas to declare a quantity the system has.",
            CausalLoopContextActionProvider.AddVariableActionId),
        new(
            "causal-loop.toolbox.link",
            "Causal link",
            "mdi-arrow-right-thin",
            "Drop on a variable to state that it affects another. It arrives stating the same direction; change it to the opposite from the link's own menu.",
            CausalLoopContextActionProvider.AddLinkActionId),
        new(
            "causal-loop.toolbox.loop",
            "Feedback loop",
            "mdi-sync",
            "Drop on a variable to claim the loop that runs through it. Whether it reinforces or balances is counted from the links rather than taken from the label.",
            CausalLoopContextActionProvider.AddLoopActionId),
    ];

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>DiscoverAsync</c> answered it after its body check.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<CausalLoopDocument, CausalLoopModel> entry, string? elementId)
    {
        if (!entry.IsUsable)
        {
            return [];
        }

        if (CausalLoopSelection.RelationOf(elementId) is not null)
        {
            return [new ContextActionGroupDefinition(
                [new ContextActionDefinition(CausalLoopContextActionProvider.ConnectActionId, "Add link", "mdi-arrow-right-thin")])];
        }

        if (CausalLoopSelection.IsPlacement(elementId))
        {
            var arrangeable = entry.Model.Variables.Count > 1;

            return [new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(CausalLoopContextActionProvider.AddVariableActionId, "Add variable", "mdi-plus-circle-outline"),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.ArrangeActionId, "Arrange diagram", "mdi-graph-outline", null,
                    arrangeable,
                    "There is nothing to arrange until this diagram has two variables."),
            ])];
        }

        if (CausalLoopSelection.VariableOf(elementId) is { } variable)
        {
            return VariableActions(entry.Model, variable);
        }

        if (CausalLoopSelection.LinkOf(elementId) is { } ends)
        {
            return LinkActions(entry.Model, ends.From, ends.To);
        }

        if (CausalLoopSelection.LoopOf(elementId) is { } loop)
        {
            return LoopActions(entry.Model, loop);
        }

        return [];
    }

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>DescribeAsync</c> answered them after its body check.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<CausalLoopDocument, CausalLoopModel> entry, string? elementId)
    {
        if (!entry.IsUsable)
        {
            return [];
        }

        var model = entry.Model;

        if (VariableOf(model, elementId) is { } variable)
        {
            return
            [
                new ContextPropertyDefinition(CausalLoopContextPropertyProvider.VariableLabelProperty, "Label", variable.Label, Group: IdentityGroup),
                new ContextPropertyDefinition(CausalLoopContextPropertyProvider.VariableIdProperty, "Identifier", variable.Id, Group: IdentityGroup),
            ];
        }

        if (LinkOf(model, elementId) is { } link)
        {
            return
            [
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkFromProperty, "Cause (from)", link.From,
                    ReadOnlyReason: EndsAreTheIdentity, Group: IdentityGroup),
                new ContextPropertyDefinition(
                    LinkToProperty, "Effect (to)", link.To,
                    ReadOnlyReason: EndsAreTheIdentity, Group: IdentityGroup),
                new ContextPropertyDefinition(LinkLabelProperty, "Note", link.Label, Group: IdentityGroup),
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkPolarityProperty, "Polarity", NameOf(link.Polarity),
                    ContextPropertyEditor.Choice, Group: CausalityGroup,
                    Candidates: [NameOf(CausalLoopPolarity.Positive), NameOf(CausalLoopPolarity.Negative), NameOf(CausalLoopPolarity.Unstated)]),
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkDelayedProperty, "Delayed", link.Delayed ? "true" : "false",
                    ContextPropertyEditor.Toggle, Group: CausalityGroup),
                new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LinkWeightProperty, "Weight", WeightText(link.Weight), Group: CausalityGroup),
            ];
        }

        if (LoopOf(model, elementId) is { } loop)
        {
            var computed = LoopPolarity.Of(model, loop.Variables);
            var stated = loop.ClaimsReinforcing;
            var disagrees = stated is not null
                && computed is LoopPolarityResult.Reinforcing or LoopPolarityResult.Balancing
                && stated != (computed == LoopPolarityResult.Reinforcing);

            List<ContextPropertyDefinition> rows =
            [
                new(
                    CausalLoopContextPropertyProvider.LoopIdentifierProperty, "Identifier", loop.Identifier, Group: IdentityGroup),
                new(LoopNameProperty, "Name", loop.Name, Group: IdentityGroup),
                new(
                    CausalLoopContextPropertyProvider.LoopComputedProperty, "Computed polarity", NameOf(computed),
                    ReadOnlyReason: ComputedFromTheArrows, Group: FeedbackGroup),
            ];

            if (disagrees)
            {
                rows.Add(new ContextPropertyDefinition(
                    CausalLoopContextPropertyProvider.LoopStatedProperty,
                    "Stated polarity",
                    stated == true ? "reinforcing" : "balancing",
                    ReadOnlyReason: $"'{loop.Identifier}' claims this, and the arrows say {NameOf(computed)}. Neither is corrected for you: change the identifier or change an arrow.",
                    Group: FeedbackGroup));
            }

            rows.Add(new ContextPropertyDefinition(
                CausalLoopContextPropertyProvider.LoopVariablesProperty, "Runs through", CycleText(loop.Variables),
                ContextPropertyEditor.Choice, Group: FeedbackGroup,
                Candidates: [.. MembershipChoices(model, loop)]));

            return rows;
        }

        return [];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> VariableActions(CausalLoopModel model, string variable)
    {
        var known = model.Declares(variable);
        var reason = known ? "" : CausalLoopWriter.NoSuchVariable;
        var count = CausalLoopWriter.CountVariableRemoval(model, variable);

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(CausalLoopContextActionProvider.AddLinkActionId, "Add link from here…", "mdi-arrow-right-thin", null, known, reason),
                new ContextActionDefinition(CausalLoopContextActionProvider.AddLoopActionId, "Claim a loop from here…", "mdi-sync", null, known, reason),
                new ContextActionDefinition(CausalLoopContextActionProvider.RenameVariableActionId, "Rename…", "mdi-rename-outline", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.RemoveVariableActionId,
                    count > 1 ? $"Remove variable (with {count - 1} references)" : "Remove variable",
                    "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"),
                    known,
                    reason),
            ]),
        ];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> LinkActions(CausalLoopModel model, string from, string to)
    {
        var link = model.Links.FirstOrDefault(candidate => candidate.From == from && candidate.To == to);
        var known = link is not null;
        var reason = known ? "" : CausalLoopWriter.NoSuchLink;

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.MakePositiveActionId, "Same direction (+)", "mdi-plus", null,
                    known && link!.Polarity != CausalLoopPolarity.Positive,
                    known ? "This link already states +." : reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.MakeNegativeActionId, "Opposite direction (−)", "mdi-minus", null,
                    known && link!.Polarity != CausalLoopPolarity.Negative,
                    known ? "This link already states −." : reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.ToggleDelayActionId,
                    known && link!.Delayed ? "Not delayed" : "Delayed",
                    "mdi-timer-sand", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.FlipCurvatureActionId,
                    known && link!.Flipped ? "Curve back the other way" : "Flip the curve",
                    "mdi-vector-curve", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.RemoveLinkActionId, "Remove link", "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"), known, reason),
            ]),
        ];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> LoopActions(CausalLoopModel model, string identifier)
    {
        var known = model.Loops.Any(loop => loop.Identifier == identifier);
        var reason = known ? "" : CausalLoopWriter.NoSuchLoop;

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(RenameLoopActionId, "Rename loop…", "mdi-rename-outline", null, known, reason),
                new ContextActionDefinition(
                    CausalLoopContextActionProvider.RemoveLoopActionId, "Remove loop", "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"), known, reason),
            ]),
        ];
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

    private static IEnumerable<string> MembershipChoices(CausalLoopModel model, CausalLoopLoop loop)
    {
        yield return CycleText(loop.Variables);

        var claimed = model.Loops
            .Select(other => CycleFinder.CanonicalSignature(other.Variables))
            .ToHashSet(StringComparer.Ordinal);
        var members = loop.Variables.ToHashSet(StringComparer.Ordinal);

        foreach (var cycle in CycleFinder.Find(model).Cycles)
        {
            if (cycle.Any(members.Contains) && !claimed.Contains(CycleFinder.CanonicalSignature(cycle)))
            {
                yield return CycleText(cycle);
            }
        }
    }

    private static string CycleText(IReadOnlyList<string> variables) => string.Join(CycleSeparator, variables);

    private static string WeightText(double? weight) =>
        weight is { } value ? value.ToString(CultureInfo.InvariantCulture) : "";

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
        _ => CausalLoopContextPropertyProvider.Undecidable,
    };
}
