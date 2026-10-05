using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The palette and the context menus as the module wrote them by hand before they were derived from
/// the DISL definition (runtime plan step S10), kept verbatim as the oracle the derived ones are
/// compared with element for element (decision D6).
/// </summary>
internal static class HandWrittenGhg
{
    /// <summary>The palette, as <c>GhgToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        new(
            "ghg.toolbox.trend",
            "Trend",
            "mdi-arrow-right-bold-box-outline",
            "A trend through the hype cycle. Drop it where it starts; it is a year long with all four phases.",
            GhgContextActionProvider.AddTrendActionId),
        new(
            "ghg.toolbox.trigger",
            "Trigger",
            "mdi-circle-slice-8",
            "A moment in time that set trends off - an invention, a political moment, a disaster. Drop it where it happened; draw influences from it.",
            GhgContextActionProvider.AddTriggerActionId),
        new(
            "ghg.toolbox.note",
            "Note",
            "mdi-note-text-outline",
            "A remark of your own, placed where it applies. Drop it and start typing.",
            GhgContextActionProvider.AddNoteActionId),
    ];

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>GhgContextActionProvider.DiscoverAsync</c> answered it after its body check.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(GhgDocumentEntry entry, string elementId)
    {
        if (!entry.IsUsable)
        {
            return [];
        }

        var model = entry.Model;

        var arrange = new ContextActionGroupDefinition(
        [
            new ContextActionDefinition(
                GhgContextActionProvider.ArrangeActionId, "Arrange diagram", "mdi-sitemap-outline", null,
                model.Trends.Count + model.Triggers.Count + model.Notes.Count > 0,
                "There is nothing to arrange until this graph has a trend."),
        ]);
        IReadOnlyList<ContextActionGroupDefinition> WithArrange(IReadOnlyList<ContextActionGroupDefinition> groups) => [.. groups, arrange];
        if (GhgEdits.TrendOf(model, elementId) is { } trend)
        {
            List<ContextActionDefinition> actions =
            [
                new(GhgContextActionProvider.RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            ];
            if (trend.DraggedEnds.Any(boundary => boundary is not null))
            {
                actions.Add(new(GhgContextActionProvider.EvenPhasesActionId, "Even phases", "mdi-arrow-split-vertical"));
            }

            actions.Add(new(GhgContextActionProvider.RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")));
            return WithArrange([new ContextActionGroupDefinition(actions)]);
        }

        if (GhgEdits.TriggerOf(model, elementId) is not null)
        {
            return WithArrange(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(GhgContextActionProvider.RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(GhgContextActionProvider.RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (GhgEdits.NoteOf(model, elementId) is not null)
        {
            return WithArrange(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(GhgContextActionProvider.RenameActionId, "Edit text…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(GhgContextActionProvider.RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (GhgEdits.InfluenceOf(model, elementId) is not null)
        {
            return WithArrange(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(GhgContextActionProvider.DisconnectActionId, "Remove influence", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (GestureIds.TryParsePlacement(elementId, out _, out _))
        {
            return WithArrange(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(GhgContextActionProvider.AddTrendActionId, "Add trend here", "mdi-plus"),
                    new ContextActionDefinition(GhgContextActionProvider.AddTriggerActionId, "Add trigger here", "mdi-circle-slice-8"),
                    new ContextActionDefinition(GhgContextActionProvider.AddNoteActionId, "Add note here", "mdi-note-text-outline"),
                ]),
            ]);
        }

        if (GhgGestures.TryParseRelation(elementId, out _, out _, out _, out _))
        {
            return [new ContextActionGroupDefinition([new ContextActionDefinition(GhgContextActionProvider.ConnectActionId, "Influence", "mdi-ray-start-arrow")])];
        }

        return [];
    }
}
