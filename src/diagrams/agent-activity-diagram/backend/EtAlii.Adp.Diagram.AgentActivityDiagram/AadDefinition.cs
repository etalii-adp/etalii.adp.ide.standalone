using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The bundled definition, loaded once: the DISL specification this module derives from, and the
/// few things the module reads off it by name.
/// </summary>
/// <remarks>
/// <b>What a status is called, which statuses there are and in what order are the definition's.</b>
/// The module reads them here and never writes them down a second time, so a status added in
/// etalii.adp reaches the canvas and the file by bundling the definition again and by nothing else.
/// </remarks>
internal static class AadDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(AadDefinition).Assembly, "agent-activity-diagram.dis"));
    private static readonly Lazy<IReadOnlyDictionary<string, AadEnum>> LoadedEnums = new(ReadEnums);

    /// <summary>The key of a location's one group of rows, in the file and on the wire.</summary>
    public const string PullRequestsGroup = "pullRequests";

    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>A task's statuses, in the order their groups are listed.</summary>
    public static AadEnum TaskStatus => LoadedEnums.Value["TaskStatus"];

    public static AadEnum SpecificationStatus => LoadedEnums.Value["SpecificationStatus"];

    public static AadEnum EnvironmentKind => LoadedEnums.Value["EnvironmentKind"];

    /// <summary>
    /// The groups that are folded until the reader unfolds them: the task statuses the definition
    /// marks collapsed, and a location's pull requests.
    /// </summary>
    public static IReadOnlySet<string> CollapsedByDefault { get; } = ReadCollapsedByDefault();

    private static readonly Lazy<IReadOnlyList<AadRelationType>> LoadedRelations = new(ReadRelations);

    private static readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> LoadedToolbox = new(() =>
    [
        .. ToolboxDerivation.Derive(Specification, WireIdMap.None)
            .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, AadContextActionProvider.AddActionPrefix + tool.Id)),
    ]);

    /// <summary>The palette: one tool per element type, each dropping its own add action.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => LoadedToolbox.Value;

    /// <summary>New ids as the definition's persistence says: a version 4 GUID in base 36.</summary>
    public static IIdSource NewIds => DislIds.Of(Specification);

    /// <summary>What an expression reads as <c>env</c> for one gesture: the moment it was made.</summary>
    public static DislEnv Env() => new(Now: AadEdits.Now());

    /// <summary>The enumeration of that name, or nothing for any other type.</summary>
    public static AadEnum? EnumOf(string type) => LoadedEnums.Value.GetValueOrDefault(type);

    /// <summary>
    /// The element, row or relation with that id. A relation is derived from the key that states it
    /// and has the id the definition gives it, such as <c>project:s-knowledge</c>.
    /// </summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return diagram.Nodes.FirstOrDefault(node => node.Id == id) ?? diagram.Relations.FirstOrDefault(relation => relation.Id == id);
    }

    /// <summary>An element's name as its type's label attribute has it, or its id when it has none.</summary>
    public static string NameOf(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return LabelAttribute(element) is { } attribute && element.ValueOf(attribute) is string { Length: > 0 } name ? name : element.Id;
    }

    /// <summary>The attribute a rename writes: <c>name</c>, a row's <c>title</c>, a location's <c>branch</c>.</summary>
    public static string? LabelAttribute(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return TypeJson(element.Type.Name) is { } type && type.TryGetProperty("labelAttribute", out var label) && label.ValueKind == JsonValueKind.String
            ? label.GetString()
            : null;
    }

    /// <summary>A type's name in words, lower case, as a sentence uses it: "pull request".</summary>
    private static string WordsOf(string typeName) =>
        (TypeJson(typeName) is { } type && type.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString()! : typeName).ToLowerInvariant();

    /// <summary>The one relation the two elements' types allow, with its ends in the order the file has them.</summary>
    public static AadDrawnRelation? RelationBetween(DislElement from, DislElement to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        foreach (var relation in LoadedRelations.Value)
        {
            if (from.IsA(relation.Source) && to.IsA(relation.Target)) return new AadDrawnRelation(relation, from, to);
            if (to.IsA(relation.Source) && from.IsA(relation.Target)) return new AadDrawnRelation(relation, to, from);
        }

        return null;
    }

    /// <summary>Why two elements cannot be joined: what the first of them can be joined to instead.</summary>
    public static string NoRelation(DislElement from, DislElement to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (ReferenceEquals(from, to)) return "An element cannot be joined to itself.";

        var own = from.Type.Name;
        var partners = LoadedRelations.Value
            .Select(relation => relation.Source == own ? relation.Target : relation.Target == own ? relation.Source : null)
            .OfType<string>()
            .Select(WordsOf)
            .ToList();
        var instead = partners.Count == 0 ? $"{A(WordsOf(own), capital: true)} has no relations." : $"{A(WordsOf(own), capital: true)} is joined to {string.Join(" or ", partners.Select(partner => A(partner)))}.";
        return $"{A(WordsOf(own), capital: true)} cannot be joined to {A(WordsOf(to.Type.Name))}. {instead}";

        // "a project", "an agent".
        static string A(string words, bool capital = false) =>
            (capital ? "A" : "a") + ("aeiou".Contains(words[0], StringComparison.Ordinal) ? "n " : " ") + words;
    }

    /// <summary>
    /// Why a relation cannot be drawn although its ends are of the right types: the element that
    /// would hold it already names another, and each holds one (Requirements 3.2, 3.4, 3.5).
    /// </summary>
    public static string? Occupied(AadDrawnRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        var holder = relation.Type.HeldBySource ? relation.Source : relation.Target;
        var other = relation.Type.HeldBySource ? relation.Target : relation.Source;
        return holder.ValueOf(relation.Type.Attribute) is DislElement current && !ReferenceEquals(current, other)
            ? $"\"{NameOf(holder)}\" is already joined to \"{NameOf(current)}\". Remove that line first: each {WordsOf(holder.Type.Name)} has one {WordsOf(current.Type.Name)}."
            : null;
    }

    /// <summary>The element whose key states a drawn relation, and the key.</summary>
    public static (DislElement Element, string Attribute)? Holder(DislElement relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        return LoadedRelations.Value.FirstOrDefault(candidate => candidate.Type == relation.Type.Name) is { } type
            && (type.HeldBySource ? relation.Source : relation.Target) is { } holder
            ? (holder, type.Attribute)
            : null;
    }

    /// <summary>The groups an element folds: a specification's task statuses, a location's pull requests.</summary>
    public static IReadOnlyList<string> GroupsOf(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.IsA("Specification") ? [.. TaskStatus.Members.Select(member => member.Name)]
            : element.IsA("Location") ? [PullRequestsGroup]
            : [];
    }

    /// <summary>A group's key as the file writes it: a task status in its stored word.</summary>
    public static string StoredGroup(string group) => TaskStatus.Find(group)?.Stored ?? group;

    /// <summary>Writes a transaction's changes into the body, stopping at the first the binding refuses.</summary>
    public static AadEdit Apply(AadBody document, DislTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.WasApplied) return AadEdit.Refused(transaction.Refusal!);

        foreach (var change in transaction.Changes)
        {
            var edit = document.Change(DislWrite.ToFbl(Specification, change));
            if (!edit.WasApplied) return edit;
        }

        return AadEdit.Applied;
    }

    /// <summary>The menu the definition derives for an element, a row or a relation.</summary>
    public static IReadOnlyList<DerivedMenuGroup> Menus(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return ContextMenuDerivation.Derive(Specification, DislMenuTarget.Element(element), Env(), WireIdMap.None);
    }

    /// <summary>
    /// The property grid's rows for an element or a row: its type's attributes in the definition's
    /// groups and order. A reference shows the name of what it names and is changed by drawing.
    /// </summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DislElement element, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (TypeJson(element.Type.Name) is not { } declared || !declared.TryGetProperty("attributes", out var attributes)) return [];

        var rows = new List<(string Group, int Order, ContextPropertyDefinition Row)>();
        foreach (var attribute in attributes.EnumerateObject())
        {
            var type = attribute.Value.GetProperty("type").GetString() ?? "string";
            var group = attribute.Value.TryGetProperty("group", out var named) ? named.GetString() ?? "" : "";
            var order = attribute.Value.TryGetProperty("order", out var ordered) && ordered.TryGetInt32(out var number) ? number : 0;
            var label = attribute.Value.TryGetProperty("label", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : Words(attribute.Name);
            var value = element.ValueOf(attribute.Name);
            var because = readOnly ? "The file could not be read, so nothing can be edited until it can." : "";

            ContextPropertyDefinition row;
            if (EnumOf(type) is { } choices)
            {
                var member = choices.Find(value as string);
                row = new ContextPropertyDefinition(attribute.Name, label, member?.Label ?? value as string ?? "", ContextPropertyEditor.Choice, because, group, [.. choices.Members.Select(candidate => candidate.Label)]);
            }
            else if (Specification.Metamodel.TypeOf(type) is not null)
            {
                row = new ContextPropertyDefinition(attribute.Name, label, value is DislElement other ? NameOf(other) : "None", ContextPropertyEditor.Line, "A relation is drawn on the canvas, and removed there.", group);
            }
            else
            {
                row = new ContextPropertyDefinition(attribute.Name, label, value as string ?? value?.ToString() ?? "", ContextPropertyEditor.Line, because, group);
            }

            rows.Add((group, order, row));
        }

        string[] groups = ["Identity", "Relations", "Link"];
        return [.. rows.OrderBy(row => Array.IndexOf(groups, row.Group) is var index && index < 0 ? groups.Length : index).ThenBy(row => row.Order).Select(row => row.Row)];

        // "branchLink" reads "Branch link".
        static string Words(string name) =>
            string.Concat(name.Select((letter, index) => index == 0 ? char.ToUpperInvariant(letter).ToString() : char.IsUpper(letter) ? " " + char.ToLowerInvariant(letter) : letter.ToString()));
    }

    private static JsonElement? TypeJson(string name) =>
        Specification.Root.GetProperty("metamodel").GetProperty("types").TryGetProperty(name, out var type) ? type : null;

    private static List<AadRelationType> ReadRelations()
    {
        var relations = new List<AadRelationType>();
        var operations = Specification.Root.GetProperty("behavior").GetProperty("operations");
        foreach (var relation in Specification.Root.GetProperty("metamodel").GetProperty("relations").EnumerateObject())
        {
            // The relation's connect operation says which end's key states it: it sets one key on one end.
            var connect = relation.Value.GetProperty("derived").GetProperty("edits").GetProperty("connect").GetString()!;
            var action = operations.GetProperty(connect).GetProperty("actions")[0];
            relations.Add(new AadRelationType(
                relation.Name,
                relation.Value.GetProperty("source").GetString()!,
                relation.Value.GetProperty("target").GetString()!,
                action.GetProperty("set").EnumerateObject().First().Name,
                HeldBySource: action.GetProperty("target").GetString() == "self.source"));
        }

        return relations;
    }

    private static IReadOnlyDictionary<string, AadEnum> ReadEnums()
    {
        var enums = new Dictionary<string, AadEnum>(StringComparer.Ordinal);
        foreach (var declared in Specification.Root.GetProperty("metamodel").GetProperty("enums").EnumerateObject())
        {
            var members = new List<AadEnumMember>();
            foreach (var member in declared.Value.GetProperty("values").EnumerateObject())
            {
                // A member is stored under its own name unless it says otherwise: `inputRequired` is
                // written `input-required`.
                var stored = member.Value.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : member.Name;
                var label = member.Value.TryGetProperty("label", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : member.Name;
                members.Add(new AadEnumMember(member.Name, stored, label));
            }

            enums[declared.Name] = new AadEnum(members);
        }

        return enums;
    }

    private static HashSet<string> ReadCollapsedByDefault()
    {
        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        var nodes = Specification.Root.GetProperty("notation").GetProperty("nodes");
        foreach (var node in nodes.EnumerateObject())
        {
            if (!node.Value.TryGetProperty("compartments", out var compartments)) continue;
            foreach (var compartment in compartments.EnumerateArray())
            {
                if (compartment.TryGetProperty("groupBy", out var grouping) && grouping.TryGetProperty("collapsed", out var groups) && groups.ValueKind == JsonValueKind.Object)
                {
                    foreach (var group in groups.EnumerateObject().Where(group => group.Value.ValueKind == JsonValueKind.True))
                    {
                        collapsed.Add(group.Name);
                    }
                }
                else if (compartment.TryGetProperty("collapsed", out var whole) && whole.ValueKind == JsonValueKind.True
                    && compartment.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    collapsed.Add(id.GetString()!);
                }
            }
        }

        return collapsed;
    }
}

/// <summary>One enum of the definition: its members in declared order.</summary>
internal sealed record AadEnum(IReadOnlyList<AadEnumMember> Members)
{
    /// <summary>The member whose name or stored form is <paramref name="text"/>, or null for a value the definition does not have.</summary>
    public AadEnumMember? Find(string? text) =>
        text is null ? null : Members.FirstOrDefault(member => member.Name == text || member.Stored == text);
}

/// <summary>One member: its name in the model, how the file writes it, and the words it is shown with.</summary>
internal sealed record AadEnumMember(string Name, string Stored, string Label);

/// <summary>One of the four relations: the two types it joins, and the key on one of them that states it.</summary>
internal sealed record AadRelationType(string Type, string Source, string Target, string Attribute, bool HeldBySource);

/// <summary>A relation between two elements, its ends in the order the file has them.</summary>
internal sealed record AadDrawnRelation(AadRelationType Type, DislElement Source, DislElement Target);
