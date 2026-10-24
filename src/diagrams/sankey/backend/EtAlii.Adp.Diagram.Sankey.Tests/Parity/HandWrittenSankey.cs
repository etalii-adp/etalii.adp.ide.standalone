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
/// document for document and element for element.
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
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<LineDocument, SankeyModel> entry, string elementId)
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
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<LineDocument, SankeyModel> entry, string elementId)
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
}
