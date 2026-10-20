using System.Text.Json;

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
            if (libraryMalformed is null) library = [.. tools.EnumerateObject().Select(tool => (tool.Name, Tool(specification, tool.Value, DislJson.Pointer("/toolbox/tools", tool.Name))))];
        }

        var palette = new List<DislPaletteItem>();
        if (toolbox.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array) Groups(groups, "/toolbox/groups");

        var menus = new List<DislMenuSet>();
        if (toolbox.TryGetProperty("contextMenus", out var sets) && sets.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var set in sets.EnumerateArray()) menus.Add(Set(specification, set, DislJson.Pointer("/toolbox/contextMenus", index++)));
        }
        return new DislToolbox(palette, library, menus, null);

        // Each group's tools, then its nested groups, depth first; a reference into the library is resolved here.
        void Groups(JsonElement list, string pointer)
        {
            var index = 0;
            foreach (var group in list.EnumerateArray())
            {
                var at = DislJson.Pointer(pointer, index++);
                if (DislFieldReader.NotAnObject(group) is { } notAnObject)
                {
                    palette.Add(new DislPaletteItem.Malformed(null, notAnObject));
                    continue;
                }

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
                            if (libraryMalformed is not null) palette.Add(new DislPaletteItem.Malformed(name, libraryMalformed));
                            else if (library?.FirstOrDefault(candidate => candidate.Name == name).Tool is { } shared) palette.Add(new DislPaletteItem.Entry(id, name, shared));
                        }
                        else if (entry.ValueKind == JsonValueKind.Object)
                        {
                            palette.Add(new DislPaletteItem.Entry(id, DislJson.String(entry, "id"), Tool(specification, entry, entryAt), pointer == "/toolbox/groups" ? entryAt : null));
                        }
                    }
                }
                if (group.TryGetProperty("groups", out var nested) && nested.ValueKind == JsonValueKind.Array) Groups(nested, DislJson.Pointer(at, "groups"));
            }
        }
    }

    private static DislTool Tool(DislSpecification specification, JsonElement tool, string pointer)
    {
        if (DislFieldReader.NotAnObject(tool) is { } malformed) return new DislTool { NotAnObject = malformed };
        return new DislTool
        {
            Id = DislJson.String(tool, "id"),
            Creates = DislJson.String(tool, "creates"),
            Label = tool.TryGetProperty("label", out var label) ? DislFieldReader.Message(specification, label, DislJson.Pointer(pointer, "label")) : null,
            Icon = DislJson.String(tool, "icon"),
            Doc = tool.TryGetProperty("doc", out var doc) ? (doc.ValueKind == JsonValueKind.String ? doc.GetString()! : DislJson.String(doc, "summary") ?? "") : "",
            Kind = DislJson.String(tool, "kind"),
            Operation = DislJson.String(tool, "operation"),
            After = DislJson.String(tool, "after"),
            Initial = Initial(specification, tool, DislJson.Pointer(pointer, "initial")),
            CreateSource = tool.TryGetProperty("createSource", out var source) ? End(specification, source, DislJson.Pointer(pointer, "createSource")) : null,
            CreateTarget = tool.TryGetProperty("createTarget", out var target) ? End(specification, target, DislJson.Pointer(pointer, "createTarget")) : null,
            Drop = tool.TryGetProperty("drop", out var drop) && drop.ValueKind == JsonValueKind.Object ? Drop(specification, drop, DislJson.Pointer(pointer, "drop")) : null,
        };
    }

    /// <summary>The <c>initial</c> values of <paramref name="owner"/>, whose pointer is <paramref name="pointer"/>; none when they are not an object.</summary>
    private static List<DislToolInitial> Initial(DislSpecification specification, JsonElement owner, string pointer) =>
    [
        .. DislJson.Members(owner, "initial").Select(property => new DislToolInitial(
            property.Name,
            property.Value.ValueKind == JsonValueKind.Object ? DislFieldReader.Expression(specification, property.Value, DislJson.Pointer(pointer, property.Name)) : null,
            property.Value)),
    ];

    /// <summary>A <c>createSource</c> or <c>createTarget</c>: a type name, <c>ask</c>, or an object with a <c>type</c> and <c>initial</c> values.</summary>
    private static DislToolEnd End(DislSpecification specification, JsonElement end, string pointer) =>
        new(
            end.ValueKind == JsonValueKind.String ? end.GetString() : DislJson.String(end, "type"),
            end.ValueKind == JsonValueKind.String && end.GetString() == "ask",
            Initial(specification, end, DislJson.Pointer(pointer, "initial")));

    /// <summary>A tool's <c>drop</c>; a target that is not an object is on nothing, so it is left out.</summary>
    private static DislToolDrop Drop(DislSpecification specification, JsonElement drop, string pointer)
    {
        var targets = new List<DislDropTarget>();
        if (drop.TryGetProperty("targets", out var declared) && declared.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var target in declared.EnumerateArray())
            {
                var at = DislJson.Pointer(DislJson.Pointer(pointer, "targets"), index++);
                if (target.ValueKind != JsonValueKind.Object) continue;
                targets.Add(new DislDropTarget(DislJson.Strings(target, "on"), DislJson.String(target, "kind") ?? "operation", DislJson.String(target, "operation"), Arguments(specification, target, at)));
            }
        }

        var refusalAt = DislJson.Pointer(pointer, "refusal");
        IReadOnlyList<DislReason>? refusal = !drop.TryGetProperty("refusal", out var declaredRefusal) ? null
            : declaredRefusal.ValueKind == JsonValueKind.Array ? DislFieldReader.Reasons(specification, declaredRefusal, refusalAt)
            : [DislFieldReader.Reason(specification, declaredRefusal, refusalAt)];
        return new DislToolDrop(DislJson.String(drop, "place"), targets, DislJson.String(drop, "elsewhere") ?? "create", refusal);
    }

    /// <summary>The <c>args</c> of a drop target or a context tool, Expressions, in order; none when they are not an object.</summary>
    private static List<(string Name, DislExpressionField Value)> Arguments(DislSpecification specification, JsonElement owner, string pointer)
    {
        var argumentsAt = DislJson.Pointer(pointer, "args");
        return [.. DislJson.Members(owner, "args").Select(argument => (argument.Name, DislFieldReader.Expression(specification, argument.Value, DislJson.Pointer(argumentsAt, argument.Name))))];
    }

    private static DislMenuSet Set(DislSpecification specification, JsonElement set, string pointer)
    {
        if (DislFieldReader.NotAnObject(set) is { } malformed) return new DislMenuSet(false, [], null, [], malformed);

        var tools = new List<DislMenuTool>();
        if (set.TryGetProperty("tools", out var declared) && declared.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var tool in declared.EnumerateArray()) tools.Add(MenuTool(specification, tool, DislJson.Pointer(DislJson.Pointer(pointer, "tools"), index++)));
        }
        return new DislMenuSet(
            set.TryGetProperty("for", out _),
            DislJson.Strings(set, "for"),
            set.TryGetProperty("when", out var when) ? DislFieldReader.Expression(specification, when, DislJson.Pointer(pointer, "when")) : null,
            tools,
            null);
    }

    private static DislMenuTool MenuTool(DislSpecification specification, JsonElement tool, string pointer)
    {
        if (DislFieldReader.NotAnObject(tool) is { } malformed) return new DislMenuTool { NotAnObject = malformed };
        return new DislMenuTool
        {
            Group = DislJson.String(tool, "group"),
            ForEach = tool.TryGetProperty("forEach", out var forEach) ? DislFieldReader.Expression(specification, forEach, DislJson.Pointer(pointer, "forEach")) : null,
            As = DislJson.String(tool, "as"),
            Visible = tool.TryGetProperty("visible", out var visible) ? DislFieldReader.Expression(specification, visible, DislJson.Pointer(pointer, "visible")) : null,
            Kind = DislJson.String(tool, "kind") ?? "operation",
            Operation = DislJson.String(tool, "operation"),
            Form = DislJson.String(tool, "form"),
            Arguments = Arguments(specification, tool, pointer),
            Via = DislJson.String(tool, "via"),
            Label = tool.TryGetProperty("label", out var label) ? DislFieldReader.Message(specification, label, DislJson.Pointer(pointer, "label")) : null,
            Icon = DislJson.String(tool, "icon"),
            Shortcut = DislJson.String(tool, "shortcut"),
            Unavailable = tool.TryGetProperty("unavailable", out var unavailable) ? DislFieldReader.Reasons(specification, unavailable, DislJson.Pointer(pointer, "unavailable")) : [],
            Enabled = tool.TryGetProperty("enabled", out var enabled) ? DislFieldReader.Expression(specification, enabled, DislJson.Pointer(pointer, "enabled")) : null,
        };
    }
}
