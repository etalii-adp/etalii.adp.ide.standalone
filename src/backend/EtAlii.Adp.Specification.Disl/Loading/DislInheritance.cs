using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Step 6 of loading (DISL §14.1): the metamodel's types with their supertypes resolved, their C3
/// linearisations computed, and their attributes, containment, label attribute and relation flags merged (§4.7).
/// </summary>
internal static class DislInheritance
{
    public static DislMetamodel Build(JsonElement metamodel, List<LoadDiagnostic> diagnostics)
    {
        var enums = Enums(metamodel);
        var dataTypes = DislJson.Members(metamodel, "dataTypes").Select(member => member.Name).ToHashSet(StringComparer.Ordinal);
        var types = Declared(metamodel, "types", false);
        var relations = Declared(metamodel, "relations", true);

        foreach (var name in types.Keys.Where(relations.ContainsKey))
        {
            diagnostics.Add(DislLoader.Error(JsonPointer.Append("/metamodel/relations", name), $"'{name}' names a node type and a relation type; they share one namespace (DISL §2.2)."));
        }
        foreach (var name in enums.Keys.Concat(dataTypes).Where(name => types.ContainsKey(name) || relations.ContainsKey(name)))
        {
            diagnostics.Add(DislLoader.Error("/metamodel", $"'{name}' names an enum or data type and a node or relation type (DISL §2.2)."));
        }

        Supertypes(types, types, "/metamodel/types", "node", diagnostics);
        Supertypes(relations, relations, "/metamodel/relations", "relation", diagnostics);
        Linearise(types, "/metamodel/types", diagnostics);
        Linearise(relations, "/metamodel/relations", diagnostics);

        var all = types.Values.Concat(relations.Values).ToDictionary(type => type.Name, StringComparer.Ordinal);
        var known = new TypeNames(enums.Keys, dataTypes, all.Keys);
        foreach (var type in all.Values)
        {
            var section = type.IsRelation ? "/metamodel/relations" : "/metamodel/types";
            Merge(type, all, JsonPointer.Append(section, type.Name), known, diagnostics);
        }
        foreach (var relation in relations.Values)
        {
            var pointer = JsonPointer.Append("/metamodel/relations", relation.Name);
            relation.Source = End(relation, "source", all, pointer, diagnostics);
            relation.Target = End(relation, "target", all, pointer, diagnostics);
        }
        Flags(relations, diagnostics);

        var diagram = metamodel.TryGetProperty("diagram", out var diagramJson) ? diagramJson : default;
        var diagramAttributes = Attributes(diagram, "diagram", "/metamodel/diagram", known, diagnostics);
        return new DislMetamodel(diagramAttributes, enums, types, relations, dataTypes);
    }

    private static OrderedDictionary<string, DislType> Declared(JsonElement metamodel, string section, bool relation)
    {
        var declared = new OrderedDictionary<string, DislType>(StringComparer.Ordinal);
        foreach (var member in DislJson.Members(metamodel, section))
        {
            declared[member.Name] = new DislType(member.Name, relation, member.Value);
        }
        return declared;
    }

    private static OrderedDictionary<string, DislEnum> Enums(JsonElement metamodel)
    {
        var enums = new OrderedDictionary<string, DislEnum>(StringComparer.Ordinal);
        foreach (var member in DislJson.Members(metamodel, "enums"))
        {
            var values = DislJson.Members(member.Value, "values")
                .Select(value => new DislEnumValue(value.Name, JsonProperties.String(value.Value, "value") ?? value.Name, JsonProperties.String(value.Value, "label")))
                .ToList();
            enums[member.Name] = new DislEnum(member.Name, values, JsonProperties.Bool(member.Value, "ordered"));
        }
        return enums;
    }

    /// <summary>Each type's <c>extends</c>, every name a type of the same kind (§4.7).</summary>
    private static void Supertypes(OrderedDictionary<string, DislType> types, OrderedDictionary<string, DislType> sameKind, string section, string kind, List<LoadDiagnostic> diagnostics)
    {
        foreach (var type in types.Values)
        {
            var extends = JsonProperties.StringOrStrings(type.Json, "extends");
            foreach (var parent in extends.Where(parent => !sameKind.ContainsKey(parent)))
            {
                diagnostics.Add(DislLoader.Error(JsonPointer.Append(JsonPointer.Append(section, type.Name), "extends"), $"'{type.Name}' extends '{parent}', which is not a {kind} type of this specification (DISL §4.7)."));
            }
            type.Extends = [.. extends.Where(sameKind.ContainsKey)];
        }
    }

    /// <summary>The C3 linearisation of every type (§4.7); a cycle or an inconsistent hierarchy is an error.</summary>
    private static void Linearise(OrderedDictionary<string, DislType> types, string section, List<LoadDiagnostic> diagnostics)
    {
        var done = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types.Values)
        {
            type.Linearisation = Of(type);
        }

        return;

        List<string> Of(DislType type)
        {
            if (done.TryGetValue(type.Name, out var known)) return known;
            if (!visiting.Add(type.Name))
            {
                diagnostics.Add(DislLoader.Error(JsonPointer.Append(section, type.Name), $"'{type.Name}' is its own supertype; supertypes form a directed acyclic graph (DISL §4.7)."));
                return [type.Name];
            }
            var sequences = type.Extends.Select(parent => new List<string>(Of(types[parent]))).ToList();
            sequences.Add([.. type.Extends]);
            var result = new List<string> { type.Name };
            while (sequences.Any(sequence => sequence.Count > 0))
            {
                var head = sequences.Where(sequence => sequence.Count > 0).Select(sequence => sequence[0])
                    .FirstOrDefault(candidate => !sequences.Any(sequence => sequence.IndexOf(candidate) > 0));
                if (head is null)
                {
                    diagnostics.Add(DislLoader.Error(JsonPointer.Append(section, type.Name), $"'{type.Name}' has no consistent C3 linearisation of its supertypes (DISL §4.7)."));
                    result.AddRange(sequences.SelectMany(sequence => sequence).Distinct(StringComparer.Ordinal).Where(name => !result.Contains(name)));
                    break;
                }
                result.Add(head);
                foreach (var sequence in sequences) sequence.Remove(head);
            }
            visiting.Remove(type.Name);
            done[type.Name] = result;
            return result;
        }
    }

    /// <summary>A type's attributes, containment and label attribute, merged along its linearisation (§4.6 to §4.8).</summary>
    private static void Merge(DislType type, Dictionary<string, DislType> all, string pointer, TypeNames known, List<LoadDiagnostic> diagnostics)
    {
        var merged = new OrderedDictionary<string, DislAttribute>(StringComparer.Ordinal);
        foreach (var ancestor in type.Linearisation.AsEnumerable().Reverse())
        {
            var declarer = all[ancestor];
            var own = Attributes(declarer.Json, declarer.Name, JsonPointer.Append(declarer.IsRelation ? "/metamodel/relations" : "/metamodel/types", declarer.Name), known, ancestor == type.Name ? diagnostics : null);
            foreach (var attribute in own.Values)
            {
                if (merged.TryGetValue(attribute.Name, out var inherited))
                {
                    var narrows = all[attribute.DeclaredBy].Linearisation.Contains(inherited.DeclaredBy);
                    if (!narrows && attribute.Json.GetRawText() != inherited.Json.GetRawText() && !DeclaresOwn(type, attribute.Name))
                    {
                        diagnostics.Add(DislLoader.Error(pointer, $"'{type.Name}' inherits '{attribute.Name}' from both '{inherited.DeclaredBy}' and '{attribute.DeclaredBy}', differently, and does not redeclare it (DISL §4.7)."));
                    }
                    if (narrows && attribute.Type != inherited.Type && !(all.ContainsKey(attribute.Type) && all.ContainsKey(inherited.Type) && all[attribute.Type].Linearisation.Contains(inherited.Type)))
                    {
                        diagnostics.Add(DislLoader.Error(JsonPointer.Append(JsonPointer.Append(pointer, "attributes"), attribute.Name), $"'{attribute.DeclaredBy}' redeclares '{attribute.Name}' as {attribute.Type}, changing its type {inherited.Type}; a subtype may only narrow it (DISL §4.7)."));
                    }
                }
                merged[attribute.Name] = attribute;
            }
        }
        type.Attributes = merged;

        var containing = type.Linearisation.Select(name => all[name].Json).FirstOrDefault(json => json.ValueKind == JsonValueKind.Object && json.TryGetProperty("children", out _));
        type.ChildTypes = containing.ValueKind == JsonValueKind.Object ? JsonProperties.StringOrStrings(containing.GetProperty("children"), "allowed") : [];

        var declaredLabel = type.Linearisation.Select(name => JsonProperties.String(all[name].Json, "labelAttribute")).FirstOrDefault(label => label is not null);
        if (declaredLabel is not null && !merged.ContainsKey(declaredLabel))
        {
            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, "labelAttribute"), $"'{type.Name}' has no attribute '{declaredLabel}' to use as its label (DISL §4.6)."));
        }
        type.LabelAttribute = declaredLabel
            ?? merged.Values.FirstOrDefault(attribute => attribute.Key)?.Name
            ?? merged.Values.FirstOrDefault(attribute => attribute.Type == "string" && JsonProperties.Bool(attribute.Json, "required"))?.Name
            ?? merged.Values.FirstOrDefault(attribute => attribute.Type == "string")?.Name;
    }

    private static bool DeclaresOwn(DislType type, string attribute) =>
        DislJson.Members(type.Json, "attributes").Any(member => member.Name == attribute);

    /// <summary>
    /// The attributes an object declares itself, in order; each name checked against the reserved names
    /// and each type resolved when <paramref name="diagnostics"/> is given.
    /// </summary>
    private static OrderedDictionary<string, DislAttribute> Attributes(JsonElement owner, string declaredBy, string pointer, TypeNames known, List<LoadDiagnostic>? diagnostics)
    {
        var attributes = new OrderedDictionary<string, DislAttribute>(StringComparer.Ordinal);
        foreach (var member in DislJson.Members(owner, "attributes"))
        {
            var attributePointer = JsonPointer.Append(JsonPointer.Append(pointer, "attributes"), member.Name);
            var type = JsonProperties.String(member.Value, "type");
            if (diagnostics is not null)
            {
                if (DislLoader.IsReserved(member.Name))
                {
                    diagnostics.Add(DislLoader.Error(attributePointer, $"'{member.Name}' is a reserved name and cannot be an attribute (DISL §2.2)."));
                }
                if (type is null)
                {
                    diagnostics.Add(DislLoader.Error(attributePointer, $"The attribute '{member.Name}' has no type (DISL §4.3)."));
                }
                else if (!known.Contains(type))
                {
                    diagnostics.Add(DislLoader.Error(JsonPointer.Append(attributePointer, "type"), $"'{type}' is not a primitive, an enum, a data type or a type of this specification (DISL §4.3)."));
                }
            }
            JsonElement? literalDefault = member.Value.TryGetProperty("default", out var declaredDefault) && !(declaredDefault.ValueKind == JsonValueKind.Object && declaredDefault.TryGetProperty("cel", out _))
                ? declaredDefault
                : null;
            var many = JsonProperties.Bool(member.Value, "many");
            attributes[member.Name] = new DislAttribute(member.Name, type ?? "json", many, literalDefault, declaredBy, member.Value)
            {
                DefaultValue = literalDefault is { } literal ? DislLiterals.FromJson(literal, type ?? "json", many, known.DataTypes) : null,
                FixedValue = member.Value.TryGetProperty("fixed", out var declaredFixed) ? DislLiterals.FromJson(declaredFixed, type ?? "json", many, known.DataTypes) : null,
            };
        }
        return attributes;
    }

    /// <summary>A relation flag (§4.9): its name, its default, and what a relation type states of it.</summary>
    private sealed record RelationFlag(string Name, bool Default, Func<DislType, bool?> Stated);

    private static readonly RelationFlag[] RelationFlags =
    [
        new("directed", true, type => type.StatedDirected),
        new("allowSelfLoops", false, type => type.StatedAllowSelfLoops),
        new("allowParallel", true, type => type.StatedAllowParallel),
    ];

    /// <summary>
    /// Each relation type's <c>directed</c>, <c>allowSelfLoops</c> and <c>allowParallel</c> (DISL 0.6, §4.7): the value of
    /// the nearest type of its linearisation that states it, else the §4.9 default. A stated flag only narrows every direct
    /// supertype's value (<c>true</c> to <c>false</c>; <c>directed</c> never differs), and direct supertypes that differ in a
    /// flag the type does not state are an error, as for conflicting inherited attributes.
    /// </summary>
    private static void Flags(OrderedDictionary<string, DislType> relations, List<LoadDiagnostic> diagnostics)
    {
        foreach (var relation in relations.Values)
        {
            relation.Directed = Effective(relation, RelationFlags[0]);
            relation.AllowSelfLoops = Effective(relation, RelationFlags[1]);
            relation.AllowParallel = Effective(relation, RelationFlags[2]);
        }
        foreach (var relation in relations.Values)
        {
            var pointer = JsonPointer.Append("/metamodel/relations", relation.Name);
            foreach (var flag in RelationFlags)
            {
                var inherited = relation.Extends.Select(parent => (Parent: parent, Value: Effective(relations[parent], flag))).ToList();
                if (flag.Stated(relation) is { } stated)
                {
                    foreach ((string parent, bool value) in inherited)
                    {
                        if (flag.Name == "directed" && stated != value)
                        {
                            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, flag.Name), $"'{relation.Name}' states {flag.Name} {Text(stated)}, which differs from the {Text(value)} of its supertype '{parent}' (DISL §4.7)."));
                        }
                        else if (flag.Name != "directed" && stated && !value)
                        {
                            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, flag.Name), $"'{relation.Name}' states {flag.Name} true, which widens the false of its supertype '{parent}'; a subtype may only narrow it (DISL §4.7)."));
                        }
                    }
                }
                else if (inherited.Select(supertype => supertype.Value).Distinct().Count() > 1)
                {
                    var given = string.Join(", ", inherited.Select(supertype => $"{Text(supertype.Value)} from '{supertype.Parent}'"));
                    diagnostics.Add(DislLoader.Error(pointer, $"'{relation.Name}' inherits {flag.Name} differently, {given}, and does not redeclare it (DISL §4.7)."));
                }
            }
        }

        return;

        bool Effective(DislType relation, RelationFlag flag) =>
            relation.Linearisation.Select(name => relations.TryGetValue(name, out var type) ? flag.Stated(type) : null).FirstOrDefault(value => value is not null) ?? flag.Default;

        static string Text(bool value) => value ? "true" : "false";
    }

    /// <summary>A relation end (§4.9): a TypeRef, a TypeRef[] or a RelationEnd, its types resolved.</summary>
    private static DislRelationEnd End(DislType relation, string side, Dictionary<string, DislType> all, string pointer, List<LoadDiagnostic> diagnostics)
    {
        // An end is inherited from the nearest supertype that declares it.
        var declaringType = relation.Linearisation.Select(name => all[name]).FirstOrDefault(type => type.Json.TryGetProperty(side, out _));
        var declaring = declaringType?.Json ?? default;
        if (declaring.ValueKind != JsonValueKind.Object)
        {
            if (relation is { Abstract: false, Derived: null }) diagnostics.Add(DislLoader.Error(pointer, $"The relation type '{relation.Name}' has no {side} end (DISL §4.9)."));
            return new DislRelationEnd([], [], false);
        }
        var end = declaring.GetProperty(side);
        var types = end.ValueKind == JsonValueKind.Object ? JsonProperties.StringOrStrings(end, "types") : JsonProperties.Strings(end);
        var exclude = end.ValueKind == JsonValueKind.Object ? JsonProperties.StringOrStrings(end, "exclude") : [];
        foreach (var name in types.Concat(exclude).Where(name => !all.ContainsKey(name)))
        {
            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, side), $"'{name}' is not a type of this specification (DISL §4.9)."));
        }
        return new DislRelationEnd(types, exclude, side == "target" && JsonProperties.Bool(end, "optional"), Bound(end, "min"), Bound(end, "max"), declaringType!.Name);
    }

    /// <summary>A relation end's <c>min</c> or <c>max</c> (§4.9) when it is an int; null when it is absent, <c>null</c> or anything else.</summary>
    private static long? Bound(JsonElement end, string name) =>
        end.ValueKind == JsonValueKind.Object && end.TryGetProperty(name, out var bound) && bound.ValueKind == JsonValueKind.Number && bound.TryGetInt64(out var value) ? value : null;
}

/// <summary>The names an attribute's type may be: a primitive, an enum, a data type, or a node or relation type (DISL §4.3).</summary>
internal sealed class TypeNames(IEnumerable<string> enums, IReadOnlySet<string> dataTypes, IEnumerable<string> types)
{
    private readonly HashSet<string> _names = [.. DislAttribute.Primitives, .. enums, .. dataTypes, .. types];

    /// <summary>The names of the data types, whose literals are read as maps.</summary>
    public IReadOnlySet<string> DataTypes => dataTypes;

    public bool Contains(string name) => _names.Contains(name);
}
