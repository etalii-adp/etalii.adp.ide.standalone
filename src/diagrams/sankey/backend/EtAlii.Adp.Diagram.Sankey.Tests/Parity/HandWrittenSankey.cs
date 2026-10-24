using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using Actions = EtAlii.Adp.Diagram.Sankey.SankeyContextActionProvider;
using Properties = EtAlii.Adp.Diagram.Sankey.SankeyContextPropertyProvider;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows as the module wrote them by hand before they were derived
/// from the DISL definition (sankey-disl-fbl task 6), kept verbatim as the oracle the derived ones are compared with,
/// document for document and element for element, and the rules the findings are compared with.
/// </summary>
internal static class HandWrittenSankey
{
    private const string IdentityGroup = "Identity";
    private const string LookGroup = "Look";
    private const string AmountGroup = "Amount";

    /// <summary>The palette, as the hand-written <c>SankeyToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        new("sankey.toolbox.node", "Node", "mdi-chart-sankey", "Something a quantity flows into, out of, or through. Drop it in the column it belongs to.", Actions.AddActionId),
    ];

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>SankeyContextActionProvider.Discover</c> answered it.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<SankeyBody, SankeyModel> entry, string elementId)
    {
        var model = entry.Model;
        var thickness = Thickness(model);

        if (model.NodeOf(elementId) is not null)
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(Actions.RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(Actions.RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
                thickness,
            ];
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            var atZero = (flow.Value ?? 0) <= 0;
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(Actions.IncreaseActionId, "Increase value", "mdi-plus-circle-outline", new ContextShortcutDefinition("+")),
                    new ContextActionDefinition(Actions.DecreaseActionId, "Decrease value", "mdi-minus-circle-outline", new ContextShortcutDefinition("-"), !atZero, "The value is already zero."),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(Actions.RemoveActionId, "Remove flow", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
                thickness,
            ];
        }

        if (GestureIds.TryParsePlacement(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition([new ContextActionDefinition(Actions.AddActionId, "Add node here", "mdi-plus")]),
                thickness,
            ];
        }

        return GestureIds.TryParseRelation(elementId, out _, out _)
            ? [new ContextActionGroupDefinition([new ContextActionDefinition(Actions.ConnectActionId, "Add flow", "mdi-arrow-right-thin")])]
            : [];
    }

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>SankeyContextPropertyProvider.Describe</c> answered them.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<SankeyBody, SankeyModel> entry, string elementId)
    {
        var model = entry.Model;

        if (model.NodeOf(elementId) is { } node)
        {
            var layout = SankeyLayout.Of(model);
            var value = layout.Values.GetValueOrDefault(node.Id);
            var columns = layout.ColumnOrder.Count;
            return
            [
                new ContextPropertyDefinition(Properties.NameProperty, "Name", node.Name, Group: IdentityGroup),
                new ContextPropertyDefinition(Properties.DescriptionProperty, "Description", node.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition(Properties.ColorProperty, "Colour", ColorChoice(node.Color), ContextPropertyEditor.Choice, Group: LookGroup, Candidates: ColorChoices(node.Color)),
                new ContextPropertyDefinition(Properties.CustomColorProperty, "Custom colour", SankeyColors.IsHex(node.Color) ? node.Color : "", Group: LookGroup),
                new ContextPropertyDefinition(Properties.ColumnProperty, "Column", node.Column is { } column ? DslNumber.Format(column) : Properties.AutoColumn, ContextPropertyEditor.Choice, Group: LookGroup,
                    Candidates: [Properties.AutoColumn, .. Enumerable.Range(1, Math.Max(columns, node.Column ?? 0) + 1).Select(c => DslNumber.Format(c))]),
                new ContextPropertyDefinition(Properties.ValueProperty, "Value", DslNumber.Format(value), ReadOnlyReason: "A node's value is what flows through it; change a flow's value instead.", Group: AmountGroup),
                new ContextPropertyDefinition(Properties.NoteProperty, "Note", node.Note, Group: AmountGroup),
                new ContextPropertyDefinition(Properties.FormatProperty, "Format", node.Format, Group: AmountGroup),
            ];
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            return
            [
                new ContextPropertyDefinition(Properties.DescriptionProperty, "Description", flow.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new ContextPropertyDefinition(Properties.ColorProperty, "Colour", ColorChoice(flow.Color), ContextPropertyEditor.Choice, Group: LookGroup, Candidates: ColorChoices(flow.Color)),
                new ContextPropertyDefinition(Properties.CustomColorProperty, "Custom colour", SankeyColors.IsHex(flow.Color) ? flow.Color : "", Group: LookGroup),
                new ContextPropertyDefinition(Properties.ValueProperty, "Value", DslNumber.Format(flow.Value), Group: AmountGroup),
                new ContextPropertyDefinition(Properties.StepProperty, "Step", DslNumber.Format(flow.Step ?? StepSankeyValueCommandHandler.DefaultStep(flow.Value)), Group: AmountGroup),
            ];
        }

        return [];
    }

    /// <summary>What removing <paramref name="elementId"/> asked first, as <c>SankeyContextActionProvider.RunAsync</c> asked it for a node.</summary>
    public static RemovalConfirmation? RemovalConfirmation(SankeyModel model, string elementId)
    {
        if (model.NodeOf(elementId) is not { } node)
        {
            return null;
        }

        var going = model.Flows.Count(candidate => candidate.From == node.Id || candidate.To == node.Id);
        return going == 0
            ? null
            : new RemovalConfirmation(
                "Remove",
                going == 1
                    ? "Removing this node also removes the 1 flow to or from it."
                    : $"Removing this node also removes the {going} flows to or from it.",
                "Remove");
    }

    /// <summary>
    /// What applies to the whole diagram, offered wherever the reader is so it stays in the ribbon: arranging it,
    /// and thicker and thinner, each disabled at its end of the range.
    /// </summary>
    private static ContextActionGroupDefinition Thickness(SankeyModel model) => new(
    [
        new ContextActionDefinition(
            Actions.ArrangeActionId, "Arrange diagram", "mdi-sitemap-outline", null,
            model.Nodes.Count > 0, "There is nothing to arrange until this diagram has a node."),
        new ContextActionDefinition(
            Actions.ThickerActionId, "Thicker bands", "mdi-arrow-expand-vertical", null,
            model.Settings.Thickness < SankeyGeometry.MaximumScale, "The bands are already as thick as they go."),
        new ContextActionDefinition(
            Actions.ThinnerActionId, "Thinner bands", "mdi-arrow-collapse-vertical", null,
            model.Settings.Thickness > SankeyGeometry.MinimumScale, "The bands are already as thin as they go."),
    ]);

    private static string ColorChoice(string color) =>
        color.Length == 0 ? Properties.DefaultColor : SankeyColors.IsHex(color) ? Properties.CustomColor : color;

    private static IReadOnlyList<string> ColorChoices(string color) =>
    [
        Properties.DefaultColor,
        .. SankeyColors.Palette,
        .. SankeyColors.IsHex(color) ? new[] { Properties.CustomColor } : [],
    ];

    /// <summary>Everything wrong with a model already parsed, as <c>SankeyValidator.Validate</c> found it.</summary>
    public static IReadOnlyList<SankeyBreach> Breaches(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<SankeyBreach> breaches =
        [
            .. model.Problems.Select(problem => new SankeyBreach(SankeyRuleIds.UnreadableEntry, problem.Message, problem.Line, true)),
            .. model.Nodes
                .Where(node => node.Id.Length == 0)
                .Select(node => new SankeyBreach(SankeyRuleIds.MissingId, "A node has no id and is not drawn.", node.Range.Start, false)),
        ];

        var entries = model.Nodes.Where(node => node.Id.Length > 0).Select(node => (node.Id, What: "node", node.Range.Start))
            .Concat(model.Flows.Select(flow => (flow.Id, What: "flow", flow.Range.Start)))
            .ToList();

        breaches.AddRange(entries
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Skip(1))
            .Select(entry => new SankeyBreach(
                SankeyRuleIds.DuplicateId,
                entry.What == "flow" && entry.Id.Contains("->", StringComparison.Ordinal)
                    ? $"A flow from `{entry.Id.Replace("->", "` to `", StringComparison.Ordinal)}` is written twice; only the first is drawn - add their values, or give each an id."
                    : $"The id `{entry.Id}` is used more than once; only its first entry is drawn.",
                entry.Start,
                false)));

        var nodes = model.Nodes.Select(node => node.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in model.Flows)
        {
            if (flow.From.Length > 0 && flow.From == flow.To)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.SelfFlow, $"The flow from `{flow.From}` runs to itself and is not drawn.", flow.Range.Start, false));
                continue;
            }

            var missing = new[] { flow.From, flow.To }.Where(end => !nodes.Contains(end)).ToList();
            foreach (var end in missing)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.DanglingFlow,
                    end.Length == 0 ? "A flow is missing an end and is not drawn." : $"A flow names `{end}`, which is not a node here; it is not drawn.",
                    flow.Range.Start, false));
            }

            if (missing.Count > 0)
            {
                continue;
            }

            if (flow.Value is null)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.MissingValue, $"The flow from `{flow.From}` to `{flow.To}` states no value, so it is drawn as a hairline.", flow.Range.Start, true));
            }
            else if (flow.Value < 0)
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.NegativeValue, $"The flow from `{flow.From}` to `{flow.To}` has a negative value; it is drawn as zero.", flow.Range.Start, true));
            }

            if (!SankeyColors.IsKnown(flow.Color))
            {
                breaches.Add(new SankeyBreach(SankeyRuleIds.UnknownColor, UnknownColor(flow.Color), flow.Range.Start, true));
            }
        }

        breaches.AddRange(model.Nodes
            .Where(node => !SankeyColors.IsKnown(node.Color))
            .Select(node => new SankeyBreach(SankeyRuleIds.UnknownColor, UnknownColor(node.Color), node.Range.Start, true)));

        var layout = SankeyLayout.Of(model);
        breaches.AddRange(layout.Flows
            .Where(flow => layout.Bands[flow.Id].Backward)
            .Select(flow => new SankeyBreach(
                SankeyRuleIds.BackwardFlow,
                $"The flow from `{flow.From}` to `{flow.To}` runs back to a column at or before its own, so it loops round instead of reading left to right.",
                flow.Range.Start,
                true)));

        return [.. breaches.OrderBy(breach => breach.Line)];
    }

    private static string UnknownColor(string color) =>
        $"`{color}` is not a colour this diagram knows - one of {string.Join(", ", SankeyColors.Palette)}, or #rrggbb; it is drawn as if it stated none.";
}

/// <summary>One thing wrong with a document, with the zero-based line it was found on, as the hand-written rules reported it.</summary>
internal sealed record SankeyBreach(string RuleId, string Message, int Line, bool IsWarning);

/// <summary>The rules a <c>.skv</c> document is checked against, each with its id.</summary>
internal static class SankeyRuleIds
{
    public const string UnreadableEntry = "sankey.unreadable";
    public const string MissingId = "sankey.missing-id";
    public const string DuplicateId = "sankey.duplicate-id";
    public const string DanglingFlow = "sankey.dangling-flow";
    public const string SelfFlow = "sankey.self-flow";
    public const string MissingValue = "sankey.missing-value";
    public const string NegativeValue = "sankey.negative-value";
    public const string UnknownColor = "sankey.unknown-color";
    public const string BackwardFlow = "sankey.backward-flow";

    /// <summary>Every rule id.</summary>
    public static IReadOnlyList<string> All { get; } = [UnreadableEntry, MissingId, DuplicateId, DanglingFlow, SelfFlow, MissingValue, NegativeValue, UnknownColor, BackwardFlow];
}
