using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests.Parity;

/// <summary>
/// The palette, the context menus and the property rows as the module wrote them by hand before they were
/// derived from the DISL definition (runtime plan step S19a), kept verbatim as the oracle the derived ones
/// are compared with element for element (decision D6).
/// </summary>
internal static class HandWrittenAbm
{
    /// <summary>The palette, as <c>AbmToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        Item(AbmNodeKinds.Sequence, "Do in order", "mdi-arrow-right-bold-outline"),
        Item(AbmNodeKinds.Fallback, "Try in order", "mdi-help-rhombus-outline"),
        Item(AbmNodeKinds.Parallel, "Do together", "mdi-call-split"),
        Item(AbmNodeKinds.Retry, "Retry", "mdi-replay"),
        Item(AbmNodeKinds.Repeat, "Repeat until", "mdi-repeat"),
        Item(AbmNodeKinds.Guard, "Only while", "mdi-shield-outline"),
        Item(AbmNodeKinds.Approval, "Ask approval before", "mdi-account-check-outline"),
        Item(AbmNodeKinds.Check, "Check", "mdi-help-circle-outline"),
        Item(AbmNodeKinds.Action, "Do", "mdi-play-outline"),
        Item(AbmNodeKinds.Ask, "Ask the user", "mdi-account-question-outline"),
        Item(AbmNodeKinds.Delegate, "Delegate", "mdi-account-arrow-right-outline"),
    ];

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>AbmContextActionProvider.DiscoverAsync</c> answered it after its origin check.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(AbmDocumentEntry entry, string elementId)
    {
        var model = entry.Model;

        ContextActionGroupDefinition arrange = new(
        [
            new ContextActionDefinition(
                AbmContextActionProvider.ArrangeActionId, "Arrange diagram", "mdi-sitemap-outline", null,
                model.Nodes.Count > 0, "There is nothing to arrange until this behavior model has a node."),
        ]);

        if (model.NodeOf(elementId) is { } node)
        {
            var siblings = Siblings(model, node);
            var place = siblings.ToList().FindIndex(sibling => sibling.Id == node.Id);
            List<ContextActionGroupDefinition> groups =
            [
                new(
                [
                    new ContextActionDefinition(AbmContextActionProvider.RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(AbmContextActionProvider.EditNotesActionId, "Edit notes…", "mdi-note-text-outline"),
                    new ContextActionDefinition(AbmContextActionProvider.MoveEarlierActionId, "Move earlier", "mdi-arrow-left", new ContextShortcutDefinition("Alt+Up"), place > 0, place > 0 ? "" : "It is already the first of its siblings."),
                    new ContextActionDefinition(AbmContextActionProvider.MoveLaterActionId, "Move later", "mdi-arrow-right", new ContextShortcutDefinition("Alt+Down"), place < siblings.Count - 1, place < siblings.Count - 1 ? "" : "It is already the last of its siblings."),
                    new ContextActionDefinition(AbmContextActionProvider.RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ];

            if (node.TakesAnotherChild)
            {
                groups.Add(new([.. AbmNodeKinds.All.Select(kind => new ContextActionDefinition(AbmContextActionProvider.AddActionId(kind.Id), $"Add child: {Menu(kind)}", "mdi-plus"))]));
            }

            groups.Add(arrange);
            return groups;
        }

        if (GestureIds.TryParsePlacement(elementId, out _, out _))
        {
            return [new([.. AbmNodeKinds.All.Select(kind => new ContextActionDefinition(AbmContextActionProvider.AddActionId(kind.Id), $"Add {Menu(kind)} here", "mdi-plus"))]), arrange];
        }

        if (GestureIds.TryParseRelation(elementId, out _, out _))
        {
            return [new([new ContextActionDefinition(AbmContextActionProvider.ConnectChildActionId, "Move under this node", "mdi-file-tree-outline")])];
        }

        return [];
    }

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>AbmContextPropertyProvider.DescribeAsync</c> answered them after its origin check.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(AbmDocumentEntry entry, string elementId)
    {
        if (entry.Model.NodeOf(elementId) is not { } node)
        {
            return [];
        }

        List<ContextPropertyDefinition> rows =
        [
            new(AbmContextPropertyProvider.KindProperty, "Kind", Choice(node.Kind), ContextPropertyEditor.Choice, Group: "Node", Candidates: [.. KindsFor(node).Select(kind => Choice(kind.Id))]),
            new(AbmContextPropertyProvider.LabelProperty, "Label", node.Label, Group: "Node"),
        ];
        if (node.Kind == AbmNodeKinds.Retry)
        {
            rows.Add(new(AbmContextPropertyProvider.AttemptsProperty, "Attempts", node.RetryCount.ToString(CultureInfo.InvariantCulture), Group: "Node"));
        }

        rows.Add(new(AbmContextPropertyProvider.NotesProperty, "Notes", node.Notes, ContextPropertyEditor.Text, Group: "Node"));
        rows.Add(new(AbmContextPropertyProvider.PlaceProperty, "Place", node.Id, ReadOnlyReason: "A node's place follows from where it sits in the tree.", Group: "Node"));
        return rows;
    }

    private static ToolboxItemDefinition Item(string kind, string label, string icon) =>
        new($"abm.toolbox.{kind}", label, icon, Describe(kind), AbmContextActionProvider.AddActionId(kind));

    private static string Describe(string kind)
    {
        var meaning = AbmNodeKinds.Of(kind).Meaning;
        return char.ToUpperInvariant(meaning[0]) + meaning[1..] + ". Drop it below the node it belongs under.";
    }

    private static IEnumerable<AbmNodeKind> KindsFor(AbmNode node) => AbmNodeKinds.All.Where(kind => kind.Category switch
    {
        AbmNodeCategory.Leaf => node.ChildIds.Count == 0,
        AbmNodeCategory.Decorator => node.ChildIds.Count <= 1,
        _ => true,
    });

    private static string Choice(string kind) => kind == AbmNodeKinds.Retry ? "Retry" : AbmNodeKinds.Of(kind).Keyword;

    private static IReadOnlyList<AbmNode> Siblings(AbmModel model, AbmNode node) =>
        node.ParentId is { } parentId ? model.ChildrenOf(model.NodeOf(parentId)!) : model.Roots;

    private static string Menu(AbmNodeKind kind) => kind.Id == AbmNodeKinds.Retry ? "Retry" : kind.Keyword;
}
