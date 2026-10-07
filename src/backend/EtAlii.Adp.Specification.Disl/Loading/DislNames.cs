using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Step 5 of loading (DISL §14.1) outside the metamodel: the type, operation and form references of
/// the toolbox, the forms, the constraints, the behavior, the notation, the viewpoints and the id
/// rules each name something the specification declares (§2.7). The metamodel's own references are
/// resolved while its inheritance is flattened (<see cref="DislInheritance"/>).
/// </summary>
internal static class DislNames
{
    /// <summary>The words a context menu's <c>for</c>, a constraint's <c>scope</c> or an operation's <c>for</c> may use besides type names.</summary>
    private static readonly HashSet<string> TargetWords = new(StringComparer.Ordinal) { "diagram", "connection", "node", "relation", "port", "*" };

    public static void Resolve(JsonElement root, DislMetamodel metamodel, List<DislDiagnostic> diagnostics)
    {
        var operations = DislJson.Members(root.TryGetProperty("behavior", out var behavior) ? behavior : default, "operations")
            .Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var forms = DislJson.Members(root, "forms").Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var resolver = new Resolver(metamodel, diagnostics);

        if (root.TryGetProperty("toolbox", out var toolbox))
        {
            Each(toolbox, "groups", "/toolbox/groups", (group, groupPointer) =>
                Each(group, "tools", DislJson.Pointer(groupPointer, "tools"), (tool, toolPointer) =>
                    resolver.Type(tool, "creates", toolPointer, concrete: true)));
            Each(toolbox, "contextMenus", "/toolbox/contextMenus", (menu, menuPointer) =>
            {
                resolver.Targets(menu, "for", menuPointer);
                Each(menu, "tools", DislJson.Pointer(menuPointer, "tools"), (tool, toolPointer) =>
                {
                    if (DislJson.String(tool, "operation") is { } operation && !operations.Contains(operation))
                    {
                        diagnostics.Add(DislLoader.Error(DislJson.Pointer(toolPointer, "operation"), $"'{operation}' is not an operation of behavior.operations (DISL §7.3)."));
                    }
                    resolver.Type(tool, "via", toolPointer, relation: true);
                    if (DislJson.String(tool, "form") is { } form && !forms.Contains(form))
                    {
                        diagnostics.Add(DislLoader.Error(DislJson.Pointer(toolPointer, "form"), $"'{form}' is not a form of this specification (DISL §7.3)."));
                    }
                });
            });
        }

        foreach (var form in DislJson.Members(root, "forms"))
        {
            resolver.Targets(form.Value, "for", DislJson.Pointer("/forms", form.Name));
        }

        if (root.TryGetProperty("constraints", out var constraints))
        {
            Each(constraints, "rules", "/constraints/rules", (rule, pointer) => resolver.Targets(rule, "scope", pointer));
        }

        foreach (var operation in DislJson.Members(behavior, "operations"))
        {
            var pointer = DislJson.Pointer("/behavior/operations", operation.Name);
            resolver.Targets(operation.Value, "for", pointer);
            if (DislJson.String(operation.Value, "paramsForm") is { } form && !forms.Contains(form))
            {
                diagnostics.Add(DislLoader.Error(DislJson.Pointer(pointer, "paramsForm"), $"'{form}' is not a form of this specification (DISL §9.3)."));
            }
        }
        Each(behavior, "hooks", "/behavior/hooks", (hook, pointer) => resolver.Targets(hook, "for", pointer));
        foreach (var deletion in DislJson.Members(behavior, "deletion"))
        {
            resolver.Name(deletion.Name, DislJson.Pointer("/behavior/deletion", deletion.Name));
        }
        foreach (var retype in DislJson.Members(behavior, "retype"))
        {
            var pointer = DislJson.Pointer("/behavior/retype", retype.Name);
            resolver.Name(retype.Name, pointer);
            foreach (var target in DislJson.Strings(retype.Value, "to")) resolver.Name(target, DislJson.Pointer(pointer, "to"));
        }

        if (root.TryGetProperty("notation", out var notation)) Notation(notation, "/notation", resolver);
        foreach (var viewpoint in DislJson.Members(root, "viewpoints"))
        {
            var pointer = DislJson.Pointer("/viewpoints", viewpoint.Name);
            foreach (var key in new[] { "include", "exclude" })
            {
                foreach (var name in DislJson.Strings(viewpoint.Value, key).Where(name => !name.Contains('*', StringComparison.Ordinal)))
                {
                    resolver.Name(name, DislJson.Pointer(pointer, key));
                }
            }
            if (viewpoint.Value.TryGetProperty("notation", out var overrides)) Notation(overrides, DislJson.Pointer(pointer, "notation"), resolver);
        }

        if (root.TryGetProperty("persistence", out var persistence) && persistence.TryGetProperty("ids", out var ids))
        {
            foreach (var type in DislJson.Members(ids, "types")) resolver.Name(type.Name, DislJson.Pointer("/persistence/ids/types", type.Name));
        }
    }

    private static void Notation(JsonElement notation, string pointer, Resolver resolver)
    {
        foreach (var node in DislJson.Members(notation, "nodes")) resolver.Name(node.Name, DislJson.Pointer(DislJson.Pointer(pointer, "nodes"), node.Name));

        // An edge notation names a relation type, or a reference attribute as Type.attribute (§6.10).
        foreach (var edge in DislJson.Members(notation, "edges").Where(edge => !edge.Name.Contains('.', StringComparison.Ordinal)))
        {
            resolver.Name(edge.Name, DislJson.Pointer(DislJson.Pointer(pointer, "edges"), edge.Name));
        }
    }

    private static void Each(JsonElement owner, string name, string pointer, Action<JsonElement, string> visit)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array) return;
        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) visit(item, DislJson.Pointer(pointer, index));
            index++;
        }
    }

    private sealed class Resolver(DislMetamodel metamodel, List<DislDiagnostic> diagnostics)
    {
        /// <summary>A type name, where any node or relation type will do.</summary>
        public void Name(string name, string pointer)
        {
            if (metamodel.TypeOf(name) is null)
            {
                diagnostics.Add(DislLoader.Error(pointer, $"'{name}' is not a type of this specification (DISL §2.7)."));
            }
        }

        /// <summary>A property naming one type: a concrete one where something is created, a relation type where one is connected.</summary>
        public void Type(JsonElement owner, string property, string pointer, bool concrete = false, bool relation = false)
        {
            if (DislJson.String(owner, property) is not { } name) return;
            var type = metamodel.TypeOf(name);
            var at = DislJson.Pointer(pointer, property);
            if (type is null) diagnostics.Add(DislLoader.Error(at, $"'{name}' is not a type of this specification (DISL §2.7)."));
            else if (concrete && type.Abstract) diagnostics.Add(DislLoader.Error(at, $"'{name}' is abstract, so nothing can create one (DISL §4.6)."));
            else if (relation && !type.IsRelation) diagnostics.Add(DislLoader.Error(at, $"'{name}' is not a relation type (DISL §7.3)."));
        }

        /// <summary>A property naming what something applies to: types, or one of the words <c>diagram</c>, <c>connection</c> and the like.</summary>
        public void Targets(JsonElement owner, string property, string pointer)
        {
            foreach (var name in DislJson.Strings(owner, property).Where(name => !TargetWords.Contains(name)))
            {
                Name(name, DislJson.Pointer(pointer, property));
            }
        }
    }
}
