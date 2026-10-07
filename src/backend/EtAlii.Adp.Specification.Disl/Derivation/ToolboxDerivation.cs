using System.Text.Json;

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
        if (!specification.Root.TryGetProperty("toolbox", out var toolbox) || toolbox.ValueKind != JsonValueKind.Object) return tools;

        var empty = new DislDiagram(specification);
        var variables = new Dictionary<string, object?> { ["self"] = empty, ["diagram"] = empty, ["env"] = new DislEnv().ToCel() };
        if (toolbox.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            Groups(groups, "/toolbox/groups");
        }
        return tools;

        void Groups(JsonElement list, string pointer)
        {
            var index = 0;
            foreach (var group in list.EnumerateArray())
            {
                var at = DislJson.Pointer(pointer, index++);
                var id = DislJson.String(group, "id") ?? "";
                if (group.TryGetProperty("tools", out var entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    var position = 0;
                    foreach (var entry in entries.EnumerateArray())
                    {
                        var entryAt = DislJson.Pointer(DislJson.Pointer(at, "tools"), position++);
                        if (entry.ValueKind == JsonValueKind.String)
                        {
                            var name = entry.GetString()!;
                            if (toolbox.TryGetProperty("tools", out var library) && library.TryGetProperty(name, out var shared))
                            {
                                tools.Add(Tool(specification, ids, shared, DislJson.Pointer("/toolbox/tools", name), name, id, variables));
                            }
                        }
                        else if (entry.ValueKind == JsonValueKind.Object)
                        {
                            tools.Add(Tool(specification, ids, entry, entryAt, DislJson.String(entry, "id") ?? "", id, variables));
                        }
                    }
                }
                if (group.TryGetProperty("groups", out var nested) && nested.ValueKind == JsonValueKind.Array) Groups(nested, DislJson.Pointer(at, "groups"));
            }
        }
    }

    private static DerivedTool Tool(DislSpecification specification, WireIdMap ids, JsonElement tool, string pointer, string name, string group, IReadOnlyDictionary<string, object?> variables)
    {
        var creates = DislJson.String(tool, "creates");
        var type = creates is null ? null : specification.Metamodel.TypeOf(creates);
        var typeLabel = type is null ? name : DislJson.String(type.Json, "label") ?? type.Name;
        var label = tool.TryGetProperty("label", out var message)
            ? DislEvaluation.Message(specification, message, DislJson.Pointer(pointer, "label"), DislContexts.Element, variables, typeLabel)
            : typeLabel;
        var icon = DislJson.String(tool, "icon") ?? (type is null ? null : DislJson.String(type.Json, "icon")) ?? "";
        return new DerivedTool(name, group, ids.ToolId(name), creates, label, icon, Doc(tool), ids.DropActionId(name));
    }

    /// <summary>A <c>doc</c> (§2.4) as one text: a string, or an object's summary.</summary>
    internal static string Doc(JsonElement owner) =>
        owner.TryGetProperty("doc", out var doc)
            ? doc.ValueKind == JsonValueKind.String ? doc.GetString()! : DislJson.String(doc, "summary") ?? ""
            : "";
}
