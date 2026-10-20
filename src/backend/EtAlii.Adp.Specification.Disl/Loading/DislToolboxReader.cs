using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads <c>toolbox</c> (DISL §7.1 to §7.3) into a <see cref="DislToolbox"/>: the palette, the library and the context
/// menus, every expression with the program the loader compiled at its pointer.
/// </summary>
/// <remarks>
/// Reading never refuses and never reports: what the runtime threw on, skipped or read as absent when it read the
/// JSON at run time is read as a record that does the same when it is used.
/// </remarks>
internal static class DislToolboxReader
{
    public static DislToolbox Read(DislSpecification specification, JsonElement root)
    {
        if (!root.TryGetProperty("toolbox", out var toolbox)) return DislToolbox.None;
        if (DislFieldReader.NotAnObject(toolbox) is { } malformed) return new DislToolbox([], null, [], malformed);

        List<(string Name, DislTool Tool)>? library = null;
        string? libraryMalformed = null;
        if (toolbox.TryGetProperty("tools", out var tools))
        {
            libraryMalformed = DislFieldReader.NotAnObject(tools);
            if (libraryMalformed is null) library = [.. tools.EnumerateObject().Select(tool => (tool.Name, Tool(specification, tool.Value, JsonPointer.Append("/toolbox/tools", tool.Name))))];
        }

        var palette = new List<DislPaletteItem>();
        if (toolbox.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array) Groups(groups, "/toolbox/groups");

        var menus = new List<DislMenuSet>();
        if (toolbox.TryGetProperty("contextMenus", out var sets) && sets.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var set in sets.EnumerateArray()) menus.Add(Set(specification, set, JsonPointer.Append("/toolbox/contextMenus", index++)));
        }
        return new DislToolbox(palette, library, menus, null);

        // Each group's tools, then its nested groups, depth first; a reference into the library is resolved here.
        void Groups(JsonElement list, string pointer)
        {
            var index = 0;
            foreach (var group in list.EnumerateArray())
            {
                var at = JsonPointer.Append(pointer, index++);
                if (DislFieldReader.NotAnObject(group) is { } notAnObject)
                {
                    palette.Add(new DislPaletteItem.Malformed(null, notAnObject));
                    continue;
                }

                var id = JsonProperties.String(group, "id") ?? "";
                if (group.TryGetProperty("tools", out var entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    var position = 0;
                    foreach (var entry in entries.EnumerateArray())
                    {
                        var entryAt = JsonPointer.Append(JsonPointer.Append(at, "tools"), position++);
                        if (entry.ValueKind == JsonValueKind.String)
                        {
                            var name = entry.GetString()!;
                            if (libraryMalformed is not null) palette.Add(new DislPaletteItem.Malformed(name, libraryMalformed));
                            else if (library?.FirstOrDefault(candidate => candidate.Name == name).Tool is { } shared) palette.Add(new DislPaletteItem.Entry(id, name, shared));
                        }
                        else if (entry.ValueKind == JsonValueKind.Object)
                        {
                            palette.Add(new DislPaletteItem.Entry(id, JsonProperties.String(entry, "id"), Tool(specification, entry, entryAt), pointer == "/toolbox/groups" ? entryAt : null));
                        }
                    }
                }
                if (group.TryGetProperty("groups", out var nested) && nested.ValueKind == JsonValueKind.Array) Groups(nested, JsonPointer.Append(at, "groups"));
            }
        }
    }

    private static DislTool Tool(DislSpecification specification, JsonElement tool, string pointer)
    {
        if (DislFieldReader.NotAnObject(tool) is { } malformed) return new DislTool { NotAnObject = malformed };
        return new DislTool
        {
            Id = JsonProperties.String(tool, "id"),
            Creates = JsonProperties.String(tool, "creates"),
            Label = tool.TryGetProperty("label", out var label) ? DislFieldReader.Message(specification, label, JsonPointer.Append(pointer, "label")) : null,
            Icon = JsonProperties.String(tool, "icon"),
            Doc = tool.TryGetProperty("doc", out var doc) ? (doc.ValueKind == JsonValueKind.String ? doc.GetString()! : JsonProperties.String(doc, "summary") ?? "") : "",
            Kind = JsonProperties.String(tool, "kind"),
            Operation = JsonProperties.String(tool, "operation"),
            After = JsonProperties.String(tool, "after"),
            Initial = Initial(specification, tool, JsonPointer.Append(pointer, "initial"), JsonProperties.String(tool, "creates")),
            CreateSource = tool.TryGetProperty("createSource", out var source) ? End(specification, source, JsonPointer.Append(pointer, "createSource")) : null,
            CreateTarget = tool.TryGetProperty("createTarget", out var target) ? End(specification, target, JsonPointer.Append(pointer, "createTarget")) : null,
            Drop = tool.TryGetProperty("drop", out var drop) && drop.ValueKind == JsonValueKind.Object ? Drop(specification, drop, JsonPointer.Append(pointer, "drop")) : null,
        };
    }

    /// <summary>
    /// The <c>initial</c> values of <paramref name="owner"/>, whose pointer is <paramref name="pointer"/>, for a node of
    /// <paramref name="type"/>; none when they are not an object.
    /// </summary>
    private static List<DislToolInitial> Initial(DislSpecification specification, JsonElement owner, string pointer, string? type) =>
    [
        .. DislJson.Members(owner, "initial").Select(property => property.Value.ValueKind == JsonValueKind.Object
            ? new DislToolInitial(property.Name, DislFieldReader.Expression(specification, property.Value, JsonPointer.Append(pointer, property.Name)), null)
            : new DislToolInitial(property.Name, null, Literal(specification.Metamodel, type, property.Name, property.Value))),
    ];

    /// <summary>
    /// An initial value as attribute <paramref name="name"/> of <paramref name="type"/> reads it; null when the type is
    /// not a node type that has it, which a drop refuses before it reads the value.
    /// </summary>
    private static object? Literal(DislMetamodel metamodel, string? type, string name, JsonElement value) =>
        type is not null && metamodel.TypeOf(type) is { IsRelation: false } created && created.Attributes.TryGetValue(name, out var attribute)
            ? DislLiterals.FromJson(value, attribute.Type, attribute.Many, metamodel.DataTypes)
            : null;

    /// <summary>A <c>createSource</c> or <c>createTarget</c>: a type name, <c>ask</c>, or an object with a <c>type</c> and <c>initial</c> values.</summary>
    private static DislToolEnd End(DislSpecification specification, JsonElement end, string pointer) =>
        new(
            end.ValueKind == JsonValueKind.String ? end.GetString() : JsonProperties.String(end, "type"),
            end.ValueKind == JsonValueKind.String && end.GetString() == "ask",
            Initial(specification, end, JsonPointer.Append(pointer, "initial"), end.ValueKind == JsonValueKind.String ? end.GetString() : JsonProperties.String(end, "type")));

    /// <summary>A tool's <c>drop</c>; a target that is not an object is on nothing, so it is left out.</summary>
    private static DislToolDrop Drop(DislSpecification specification, JsonElement drop, string pointer)
    {
        var targets = new List<DislDropTarget>();
        if (drop.TryGetProperty("targets", out var declared) && declared.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var target in declared.EnumerateArray())
            {
                var at = JsonPointer.Append(JsonPointer.Append(pointer, "targets"), index++);
                if (target.ValueKind != JsonValueKind.Object) continue;
                targets.Add(new DislDropTarget(JsonProperties.StringOrStrings(target, "on"), JsonProperties.String(target, "kind") ?? "operation", JsonProperties.String(target, "operation"), Arguments(specification, target, at)));
            }
        }

        var refusalAt = JsonPointer.Append(pointer, "refusal");
        IReadOnlyList<DislReason>? refusal = !drop.TryGetProperty("refusal", out var declaredRefusal) ? null
            : declaredRefusal.ValueKind == JsonValueKind.Array ? DislFieldReader.Reasons(specification, declaredRefusal, refusalAt)
            : [DislFieldReader.Reason(specification, declaredRefusal, refusalAt)];
        return new DislToolDrop(JsonProperties.String(drop, "place"), targets, JsonProperties.String(drop, "elsewhere") ?? "create", refusal);
    }

    /// <summary>The <c>args</c> of a drop target or a context tool, Expressions, in order; none when they are not an object.</summary>
    private static List<(string Name, DislExpressionField Value)> Arguments(DislSpecification specification, JsonElement owner, string pointer)
    {
        var argumentsAt = JsonPointer.Append(pointer, "args");
        return [.. DislJson.Members(owner, "args").Select(argument => (argument.Name, DislFieldReader.Expression(specification, argument.Value, JsonPointer.Append(argumentsAt, argument.Name))))];
    }

    private static DislMenuSet Set(DislSpecification specification, JsonElement set, string pointer)
    {
        if (DislFieldReader.NotAnObject(set) is { } malformed) return new DislMenuSet(false, [], null, [], malformed);

        var tools = new List<DislMenuTool>();
        if (set.TryGetProperty("tools", out var declared) && declared.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var tool in declared.EnumerateArray()) tools.Add(MenuTool(specification, tool, JsonPointer.Append(JsonPointer.Append(pointer, "tools"), index++)));
        }
        return new DislMenuSet(
            set.TryGetProperty("for", out _),
            JsonProperties.StringOrStrings(set, "for"),
            set.TryGetProperty("when", out var when) ? DislFieldReader.Expression(specification, when, JsonPointer.Append(pointer, "when")) : null,
            tools,
            null);
    }

    private static DislMenuTool MenuTool(DislSpecification specification, JsonElement tool, string pointer)
    {
        if (DislFieldReader.NotAnObject(tool) is { } malformed) return new DislMenuTool { NotAnObject = malformed };
        return new DislMenuTool
        {
            Group = JsonProperties.String(tool, "group"),
            ForEach = tool.TryGetProperty("forEach", out var forEach) ? DislFieldReader.Expression(specification, forEach, JsonPointer.Append(pointer, "forEach")) : null,
            As = JsonProperties.String(tool, "as"),
            Visible = tool.TryGetProperty("visible", out var visible) ? DislFieldReader.Expression(specification, visible, JsonPointer.Append(pointer, "visible")) : null,
            Kind = JsonProperties.String(tool, "kind") ?? "operation",
            Operation = JsonProperties.String(tool, "operation"),
            Form = JsonProperties.String(tool, "form"),
            Arguments = Arguments(specification, tool, pointer),
            Via = JsonProperties.String(tool, "via"),
            Label = tool.TryGetProperty("label", out var label) ? DislFieldReader.Message(specification, label, JsonPointer.Append(pointer, "label")) : null,
            Icon = JsonProperties.String(tool, "icon"),
            Shortcut = JsonProperties.String(tool, "shortcut"),
            Unavailable = tool.TryGetProperty("unavailable", out var unavailable) ? DislFieldReader.Reasons(specification, unavailable, JsonPointer.Append(pointer, "unavailable")) : [],
            Enabled = tool.TryGetProperty("enabled", out var enabled) ? DislFieldReader.Expression(specification, enabled, JsonPointer.Append(pointer, "enabled")) : null,
        };
    }
}
