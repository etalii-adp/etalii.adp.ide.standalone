using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The palette, the context menus, the property rows and the deletion confirmation as the module wrote
/// them by hand before they were derived from the DISL definition (functional-decomposition-graph-disl-fbl
/// task 6), kept verbatim as the oracle the derived ones are compared with, document for document and
/// element for element.
/// </summary>
internal static class HandWrittenFdg
{
    /// <summary>The palette, as the hand-written <c>FdgToolboxProvider.Items</c> listed it.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox { get; } =
    [
        Item(FdgElementTypes.UiElement, "UI element", "mdi-application-outline", "A screen, or a part of one. Drop it where it belongs; its parent is a connection drawn to it."),
        Item(FdgElementTypes.Action, "Action", "mdi-gesture-tap", "Something the user does on a screen. A screen owns it."),
        Item(FdgElementTypes.DataElement, "Data element", "mdi-database-outline", "Something the application keeps. A screen, an action or other data owns it."),
        Item(FdgElementTypes.Function, "Function", "mdi-cog-outline", "Something that runs behind the screens. Anything but a Comment may own it."),
        Item(FdgElementTypes.Comment, "Comment", "mdi-note-text-outline", "A note on the graph. It connects to nothing."),
    ];

    /// <summary>The context menu of <paramref name="elementId"/>, as <c>FdgContextActionProvider.Discover</c> answered it.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<LineDocument, FdgModel> entry, string elementId)
    {
        var model = entry.Model;

        if (model.ElementOf(elementId) is { } element)
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition("fdg.rename", element.IsComment ? "Edit text…" : "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition("fdg.remove", "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ];
        }

        if (model.ConnectionOf(elementId) is not null)
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition("fdg.rename-connection", "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition("fdg.disconnect", "Remove connection", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ];
        }

        if (GestureIds.TryParsePlacement(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition(
                    [.. FdgElementTypes.All.Select(type => new ContextActionDefinition("fdg.add." + type, $"Add {Titled(type)} here", "mdi-plus"))]),
            ];
        }

        if (GestureIds.TryParseRelation(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition(
                    [.. FdgConnectionTypes.All.Select(relation => new ContextActionDefinition("fdg.connect." + relation, Titled(relation), "mdi-ray-start-arrow"))]),
            ];
        }

        return [];
    }

    /// <summary>The property rows of <paramref name="elementId"/>, as <c>FdgContextPropertyProvider.Describe</c> answered them.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<LineDocument, FdgModel> entry, string elementId)
    {
        var model = entry.Model;

        if (model.ElementOf(elementId) is { } element)
        {
            List<ContextPropertyDefinition> rows = element.IsComment
                ? [new ContextPropertyDefinition("fdg.text", "Text", element.Text, ContextPropertyEditor.Text, Group: "Identity")]
                : [new ContextPropertyDefinition("fdg.name", "Name", element.Name, Group: "Identity")];
            rows.Add(new ContextPropertyDefinition("fdg.description", "Description", element.Description, ContextPropertyEditor.Text, Group: "Identity"));
            rows.Add(new ContextPropertyDefinition("fdg.width", "Width", DslNumber.Format(element.Width), Group: "Size"));
            if (element.IsComment)
            {
                rows.Add(new ContextPropertyDefinition("fdg.height", "Height", DslNumber.Format(element.DrawnHeight), Group: "Size"));
            }

            return rows;
        }

        if (model.ConnectionOf(elementId) is { } connection)
        {
            return
            [
                new ContextPropertyDefinition("fdg.connection-name", "Name", connection.Name, Group: "Identity"),
                new ContextPropertyDefinition("fdg.description", "Description", connection.Description, ContextPropertyEditor.Text, Group: "Identity"),
            ];
        }

        return [];
    }

    /// <summary>What removing <paramref name="elementId"/> asked first, as <c>FdgContextActionProvider.RunAsync</c> worded it; null when it asked nothing.</summary>
    public static RemovalConfirmation? Confirmation(DocumentEntry<LineDocument, FdgModel> entry, string elementId)
    {
        var model = entry.Model;
        if (model.ElementOf(elementId) is not { } element)
        {
            return null;
        }

        var going = model.Connections.Count(connection => connection.From == element.Id || connection.To == element.Id);
        return going == 0
            ? null
            : new RemovalConfirmation(
                "Remove",
                going == 1
                    ? "Removing this element also removes the 1 connection to or from it."
                    : $"Removing this element also removes the {going} connections to or from it.",
                "Remove");
    }

    private static ToolboxItemDefinition Item(string type, string label, string icon, string description) =>
        new($"fdg.toolbox.{type}", label, icon, description, "fdg.add." + type);

    private static string Titled(string word) => word.Replace('-', ' ');
}
