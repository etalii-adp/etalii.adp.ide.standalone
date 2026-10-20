namespace EtAlii.Adp.Specification.Disl;

/// <summary>One palette tool as a host shows it (DISL §7.2), its ids mapped by a <see cref="WireIdMap"/>.</summary>
/// <param name="Name">The tool's id in the specification.</param>
/// <param name="Group">The id of the tool group it is in.</param>
/// <param name="Id">The palette id.</param>
/// <param name="Creates">The type it creates, or null.</param>
/// <param name="Label">Its text: its own label, else the created type's.</param>
/// <param name="Icon">Its icon: its own, else the created type's; empty when neither has one.</param>
/// <param name="Description">Its <c>doc</c>, the tooltip.</param>
/// <param name="DropActionId">The action a drop runs, or null when the map names none.</param>
public sealed record DerivedTool(string Name, string Group, string Id, string? Creates, string Label, string Icon, string Description, string? DropActionId);

/// <summary>
/// The palette of a specification (DISL §7.1, §7.2): every tool of every group, nested groups
/// depth first, in declaration order, a tool referenced by id taken from the toolbox library.
/// </summary>
/// <remarks>
/// A palette reads no diagram, so a tool's label is evaluated with <c>self</c> bound to an empty one;
/// a <c>visible</c> or <c>enabled</c> condition is the host's to evaluate where it has a diagram.
/// </remarks>
public static class ToolboxDerivation
{
    public static IReadOnlyList<DerivedTool> Derive(DislSpecification specification, WireIdMap ids)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(ids);

        var tools = new List<DerivedTool>();
        var toolbox = specification.Toolbox;
        if (toolbox.NotAnObject is not null || toolbox.Palette.Count == 0) return tools;

        var empty = new DislDiagram(specification);
        var variables = new Dictionary<string, object?> { ["self"] = empty, ["diagram"] = empty, ["env"] = new DislEnv().ToCel() };
        foreach (var item in toolbox.Palette)
        {
            switch (item)
            {
                case DislPaletteItem.Malformed malformed:
                    throw new InvalidOperationException(malformed.Message);
                case DislPaletteItem.Entry entry:
                    tools.Add(Tool(specification, ids, entry.Tool, entry.Key ?? "", entry.Group, variables));
                    break;
            }
        }
        return tools;
    }

    private static DerivedTool Tool(DislSpecification specification, WireIdMap ids, DislTool tool, string name, string group, IReadOnlyDictionary<string, object?> variables)
    {
        if (tool.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        var type = tool.Creates is null ? null : specification.Metamodel.TypeOf(tool.Creates);
        var typeLabel = type is null ? name : type.Label ?? type.Name;
        var label = tool.Label?.Text(variables, typeLabel) ?? typeLabel;
        var icon = tool.Icon ?? type?.Icon ?? "";
        return new DerivedTool(name, group, ids.ToolId(name), tool.Creates, label, icon, tool.Doc, ids.DropActionId(name));
    }
}
