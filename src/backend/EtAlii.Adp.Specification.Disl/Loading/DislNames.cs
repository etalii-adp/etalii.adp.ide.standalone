using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Step 5 of loading (DISL §14.1) outside the metamodel: the type, operation and form references of
/// the toolbox, the forms, the constraints, the behavior, the notation, the viewpoints and the id
/// rules each name something the specification declares (§2.7), read from the parts the specification
/// typed at load. The metamodel's own references are resolved while its inheritance is flattened
/// (<see cref="DislInheritance"/>).
/// </summary>
internal static class DislNames
{
    /// <summary>The words a context menu's <c>for</c>, a constraint's <c>scope</c> or an operation's <c>for</c> may use besides type names.</summary>
    private static readonly HashSet<string> TargetWords = new(StringComparer.Ordinal) { "diagram", "connection", "node", "relation", "port", "*" };

    public static void Resolve(DislSpecification specification, List<LoadDiagnostic> diagnostics)
    {
        var operations = specification.Operations.Declared.Select(operation => operation.Id).ToHashSet(StringComparer.Ordinal);
        var forms = specification.Forms.All.Select(form => form.Name).ToHashSet(StringComparer.Ordinal);
        var resolver = new Resolver(specification.Metamodel, diagnostics);

        foreach (var entry in specification.Toolbox.Palette.OfType<DislPaletteItem.Entry>())
        {
            if (entry.ResolvedAt is { } toolPointer) resolver.Type(entry.Tool.Creates, toolPointer, "creates", concrete: true);
        }
        var menuIndex = 0;
        foreach (var menu in specification.Toolbox.ContextMenus)
        {
            var menuPointer = JsonPointer.Append("/toolbox/contextMenus", menuIndex++);
            if (menu.NotAnObject is not null) continue;
            resolver.Targets(menu.For, menuPointer, "for");
            var toolIndex = 0;
            foreach (var tool in menu.Tools)
            {
                var toolPointer = JsonPointer.Append(JsonPointer.Append(menuPointer, "tools"), toolIndex++);
                if (tool.NotAnObject is not null) continue;
                if (tool.Operation is { } operation && !operations.Contains(operation))
                {
                    diagnostics.Add(DislLoader.Error(JsonPointer.Append(toolPointer, "operation"), $"'{operation}' is not an operation of behavior.operations (DISL §7.3)."));
                }
                resolver.Type(tool.Via, toolPointer, "via", relation: true);
                if (tool.Form is { } form && !forms.Contains(form))
                {
                    diagnostics.Add(DislLoader.Error(JsonPointer.Append(toolPointer, "form"), $"'{form}' is not a form of this specification (DISL §7.3)."));
                }
            }
        }

        foreach (var form in specification.Forms.All)
        {
            resolver.Targets(form.For, JsonPointer.Append("/forms", form.Name), "for");
        }

        foreach (var rule in specification.Constraints.Rules ?? [])
        {
            if (rule.NotAnObject is null) resolver.Targets(rule.Scope, rule.Pointer, "scope");
        }

        foreach (var operation in specification.Operations.Declared)
        {
            var pointer = JsonPointer.Append("/behavior/operations", operation.Id);
            resolver.Targets(operation.For, pointer, "for");
            if (operation.ParamsForm is { } form && !forms.Contains(form))
            {
                diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, "paramsForm"), $"'{form}' is not a form of this specification (DISL §9.3)."));
            }
        }
        var behavior = specification.Behavior;
        foreach (var hook in behavior.Hooks) resolver.Targets(hook.For, hook.Pointer, "for");
        foreach ((string type, _) in behavior.Deletion)
        {
            resolver.Name(type, JsonPointer.Append("/behavior/deletion", type));
        }
        foreach ((string type, DislRetypePolicy policy) in behavior.Retype)
        {
            var pointer = JsonPointer.Append("/behavior/retype", type);
            resolver.Name(type, pointer);
            foreach (var target in policy.To) resolver.Name(target, JsonPointer.Append(pointer, "to"));
        }

        Notation(specification.Notation.Names, "/notation", resolver);
        foreach ((string name, DislViewpoint viewpoint) in specification.Viewpoints.All)
        {
            var pointer = JsonPointer.Append("/viewpoints", name);
            foreach ((string key, IReadOnlyList<string> names) in new[] { ("include", viewpoint.Include), ("exclude", viewpoint.Exclude) })
            {
                foreach (var type in names.Where(type => !type.Contains('*', StringComparison.Ordinal)))
                {
                    resolver.Name(type, JsonPointer.Append(pointer, key));
                }
            }
            // A viewpoint that is not an object threw here when its notation was read from the JSON, and still does.
            if (viewpoint.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
            if (viewpoint.Notation is { } overrides) Notation(overrides, JsonPointer.Append(pointer, "notation"), resolver);
        }

        // A persistence that is not an object threw here when its ids were read from the JSON, and still does.
        if (specification.PersistenceNotAnObject is { } notAnObject) throw new InvalidOperationException(notAnObject);
        foreach (var type in specification.IdTypeNames) resolver.Name(type, JsonPointer.Append("/persistence/ids/types", type));
    }

    private static void Notation(DislNotationNames names, string pointer, Resolver resolver)
    {
        foreach (var node in names.Nodes) resolver.Name(node, JsonPointer.Append(JsonPointer.Append(pointer, "nodes"), node));

        // An edge notation names a relation type, or a reference attribute as Type.attribute (§6.10).
        foreach (var edge in names.Edges.Where(edge => !edge.Contains('.', StringComparison.Ordinal)))
        {
            resolver.Name(edge, JsonPointer.Append(JsonPointer.Append(pointer, "edges"), edge));
        }
    }

    private sealed class Resolver(DislMetamodel metamodel, List<LoadDiagnostic> diagnostics)
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
        public void Type(string? name, string pointer, string property, bool concrete = false, bool relation = false)
        {
            if (name is null) return;
            var type = metamodel.TypeOf(name);
            var at = JsonPointer.Append(pointer, property);
            if (type is null) diagnostics.Add(DislLoader.Error(at, $"'{name}' is not a type of this specification (DISL §2.7)."));
            else if (concrete && type.Abstract) diagnostics.Add(DislLoader.Error(at, $"'{name}' is abstract, so nothing can create one (DISL §4.6)."));
            else if (relation && !type.IsRelation) diagnostics.Add(DislLoader.Error(at, $"'{name}' is not a relation type (DISL §7.3)."));
        }

        /// <summary>A property naming what something applies to: types, or one of the words <c>diagram</c>, <c>connection</c> and the like.</summary>
        public void Targets(IReadOnlyList<string> names, string pointer, string property)
        {
            foreach (var name in names.Where(name => !TargetWords.Contains(name)))
            {
                Name(name, JsonPointer.Append(pointer, property));
            }
        }
    }
}
