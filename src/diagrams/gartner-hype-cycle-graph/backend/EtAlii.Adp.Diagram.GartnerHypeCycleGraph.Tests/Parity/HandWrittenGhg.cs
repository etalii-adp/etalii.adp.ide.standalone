using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows as the module wrote them by hand before they were derived from
/// the DISL definition (runtime plan steps S10 and S11), kept verbatim as the oracle the derived ones are
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

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>GhgContextPropertyProvider.DescribeAsync</c> answered them after its body check (runtime plan step S11).</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(GhgDocumentEntry entry, string elementId)
    {
        const string identityGroup = "Identity";
        const string timeGroup = "Time";
        const string phasesGroup = "Phases";
        const string endsGroup = "Ends";
        var readOnly = entry.IsUsable ? "" : "The graph could not be read, so it cannot be edited.";
        var model = entry.Model;

        if (GhgEdits.TrendOf(model, elementId) is { } trend)
        {
            List<ContextPropertyDefinition> rows =
            [
                new(GhgContextPropertyProvider.NameProperty, "Name", trend.Name, ReadOnlyReason: readOnly, Group: identityGroup),
                new(GhgContextPropertyProvider.DescriptionProperty, "Description", trend.Description, ContextPropertyEditor.Text, readOnly, identityGroup),
                new(GhgContextPropertyProvider.TagsProperty, "Tags", string.Join(", ", trend.Tags), ContextPropertyEditor.Tags, readOnly, identityGroup, TagsOf(model)),
                new(GhgContextPropertyProvider.StartProperty, "Start", Month(trend.Start), ReadOnlyReason: readOnly, Group: timeGroup),
                new(GhgContextPropertyProvider.StopProperty, "Stop", Month(trend.Stop), ReadOnlyReason: readOnly, Group: timeGroup),
                new(GhgContextPropertyProvider.PhasesProperty, "Phases", GhgContextPropertyProvider.PhaseCandidates[trend.VisiblePhases - 1], ContextPropertyEditor.Slider, readOnly, phasesGroup, GhgContextPropertyProvider.PhaseCandidates),
            ];

            var drawn = GhgPhases.BoundariesOf(trend);
            for (var index = 0; index < drawn.Count; index++)
            {
                rows.Add(new(GhgContextPropertyProvider.BoundaryProperties[index], $"{GhgPhases.Titles[index]} ends", GhgScale.FormatMonth(drawn[index]), ReadOnlyReason: readOnly, Group: phasesGroup));
            }

            rows.AddRange(InfluenceRows(model, trend));
            return rows;
        }

        if (GhgEdits.TriggerOf(model, elementId) is { } trigger)
        {
            return
            [
                new(GhgContextPropertyProvider.NameProperty, "Name", trigger.Name, ReadOnlyReason: readOnly, Group: identityGroup),
                new(GhgContextPropertyProvider.DescriptionProperty, "Description", trigger.Description, ContextPropertyEditor.Text, readOnly, identityGroup),
                new(GhgContextPropertyProvider.TagsProperty, "Tags", string.Join(", ", trigger.Tags), ContextPropertyEditor.Tags, readOnly, identityGroup, TagsOf(model)),
                new(GhgContextPropertyProvider.DateProperty, "Date", Month(trigger.Date), ReadOnlyReason: readOnly, Group: timeGroup),
            ];
        }

        if (GhgEdits.NoteOf(model, elementId) is { } note)
        {
            return
            [
                new(GhgContextPropertyProvider.TextProperty, "Text", note.Text, ContextPropertyEditor.Text, readOnly, identityGroup),
                new(GhgContextPropertyProvider.SizeProperty, "Size", note is { Width: { } width, Height: { } height } ? SetGhgNoteSizeCommand.Format(width, height) : "", ReadOnlyReason: readOnly, Group: identityGroup),
            ];
        }

        if (GhgEdits.InfluenceOf(model, elementId) is { } influence)
        {
            const string shown = "Where it is attached; drag the end on the canvas to move it.";
            return
            [
                new(GhgContextPropertyProvider.DescriptionProperty, "Description", influence.Description, ContextPropertyEditor.Text, readOnly, identityGroup),
                new(GhgContextPropertyProvider.FromProperty, "From", Describe(model, influence.From, influence.FromEnd), ReadOnlyReason: shown, Group: endsGroup),
                new(GhgContextPropertyProvider.ToProperty, "To", Describe(model, influence.To, influence.ToEnd), ReadOnlyReason: shown, Group: endsGroup),
                new(GhgContextPropertyProvider.FromAttachmentProperty, "From attachment", influence.FromEnd.ToString(), ReadOnlyReason: readOnly, Group: endsGroup),
                new(GhgContextPropertyProvider.ToAttachmentProperty, "To attachment", influence.ToEnd.ToString(), ReadOnlyReason: readOnly, Group: endsGroup),
            ];
        }

        return [];
    }

    private static IEnumerable<ContextPropertyDefinition> InfluenceRows(GhgModel model, GhgTrend trend)
    {
        const string shown = "Draw, reattach or delete an influence on the canvas.";
        for (var phase = 0; phase < GhgPhases.Titles.Count; phase++)
        {
            var leaving = model.Influences
                .Where(influence => influence.From == trend.Id && influence.FromEnd.PhaseIndex == phase)
                .Select(influence => Describe(model, influence.To, influence.ToEnd))
                .ToList();
            var arriving = model.Influences
                .Where(influence => influence.To == trend.Id && influence.ToEnd.PhaseIndex == phase)
                .Select(influence => Describe(model, influence.From, influence.FromEnd))
                .ToList();

            var drawn = phase < trend.VisiblePhases;
            if (!drawn && leaving.Count == 0 && arriving.Count == 0)
            {
                continue;
            }

            var group = drawn ? GhgPhases.Titles[phase] : $"{GhgPhases.Titles[phase]} (hidden)";
            yield return new(GhgContextPropertyProvider.InfluencesProperties[phase], "Influence", List(leaving), ContextPropertyEditor.Text, shown, group);
            yield return new(GhgContextPropertyProvider.InfluencedByProperties[phase], "Influenced by", List(arriving), ContextPropertyEditor.Text, shown, group);
        }
    }

    private static string List(IReadOnlyList<string> entries) => entries.Count == 0 ? GhgContextPropertyProvider.NoInfluences : string.Join("\n", entries);

    private static string Describe(GhgModel model, string id, GhgEnd end)
    {
        if (GhgEdits.TrendOf(model, id) is null && GhgEdits.TriggerOf(model, id) is { } trigger)
        {
            return trigger.Name.Length > 0 ? trigger.Name : id;
        }

        var trend = GhgEdits.TrendOf(model, id);
        var name = trend is { Name.Length: > 0 } ? trend.Name : id;
        var phase = end.PhaseIndex >= 0 ? GhgPhases.Titles[end.PhaseIndex] : end.Phase;
        return $"{name} · {phase}";
    }

    private static IReadOnlyList<string> TagsOf(GhgModel model) =>
        [.. model.Trends.SelectMany(trend => trend.Tags).Concat(model.Triggers.SelectMany(trigger => trigger.Tags)).Distinct(StringComparer.Ordinal)];

    private static string Month(int? month) => month is { } value ? GhgScale.FormatMonth(value) : "";
}
