using System.Text.Json;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A host's wire ids for a specification's names, read from a top-level <c>x-&lt;prefix&gt;</c> block
/// (<c>x-abm</c>, <c>x-ghg</c>): what the client and the backend already call a tool, an action and a
/// property row, which DISL's own names cannot always be (a tool id has no dot, and two entries of
/// one kind may need two ids).
/// </summary>
/// <remarks>
/// <para>The block's shape, every part optional:</para>
/// <list type="bullet">
/// <item><c>tools</c>: per toolbox tool id, <c>{ "id": …, "drop": … }</c>, its palette id and the action a drop runs.</item>
/// <item><c>actions</c>: per operation name or entry kind, the action id. A key <c>"&lt;Type&gt;/&lt;operation&gt;"</c> or
/// <c>"&lt;Type&gt;/&lt;kind&gt;"</c> overrides it for entries offered on that type or its subtypes. A
/// <c>&lt;name&gt;</c> in an id is replaced by the entry's binding of that name (its <c>as</c> name or an argument), mapped
/// through <c>types</c> when it is a type name there.</item>
/// <item><c>properties</c>: per form item key - its row id: its <c>id</c>, else its attribute, else a computed item's label - the row id.</item>
/// <item><c>types</c>: per type name, the host's kind, which <c>&lt;name&gt;</c> substitution reads.</item>
/// </list>
/// <para>A name the block does not map is its own id, so a specification without a block keeps DISL's names.</para>
/// </remarks>
public sealed partial class WireIdMap
{
    private readonly IReadOnlyDictionary<string, (string? Id, string? Drop)> _tools;
    private readonly IReadOnlyDictionary<string, string> _actions;
    private readonly IReadOnlyDictionary<string, string> _properties;

    private WireIdMap(
        IReadOnlyDictionary<string, (string? Id, string? Drop)> tools,
        IReadOnlyDictionary<string, string> actions,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyDictionary<string, string> types)
    {
        _tools = tools;
        _actions = actions;
        _properties = properties;
        Types = types;
    }

    /// <summary>The map that maps nothing: every name is its own id.</summary>
    public static WireIdMap None { get; } = new(
        new Dictionary<string, (string?, string?)>(),
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<string, string>());

    /// <summary>The host's kind of each type name (<c>types</c>).</summary>
    public IReadOnlyDictionary<string, string> Types { get; }

    /// <summary>The map in <paramref name="specification"/>'s top-level <paramref name="key"/> (<c>x-ghg</c>); <see cref="None"/> when it has none.</summary>
    public static WireIdMap Of(DislSpecification specification, string key)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!specification.Extensions.TryGetValue(key, out var block) || block.ValueKind != JsonValueKind.Object) return None;

        var tools = DislJson.Members(block, "tools").ToDictionary(
            tool => tool.Name,
            tool => (DislJson.String(tool.Value, "id"), DislJson.String(tool.Value, "drop")),
            StringComparer.Ordinal);
        return new WireIdMap(tools, Strings(block, "actions"), Strings(block, "properties"), Strings(block, "types"));
    }

    /// <summary>The palette id of toolbox tool <paramref name="tool"/>.</summary>
    public string ToolId(string tool) => _tools.TryGetValue(tool, out var mapped) && mapped.Id is { } id ? id : tool;

    /// <summary>The action a drop of toolbox tool <paramref name="tool"/> runs, or null when the block names none.</summary>
    public string? DropActionId(string tool) => _tools.TryGetValue(tool, out var mapped) ? mapped.Drop : null;

    /// <summary>
    /// The id of a context entry of <paramref name="kind"/> running <paramref name="operation"/>,
    /// offered on an element whose type linearisation is <paramref name="types"/> (most specific first),
    /// with <paramref name="bindings"/> for the <c>&lt;name&gt;</c>s in it.
    /// </summary>
    public string ActionId(IReadOnlyList<string> types, string kind, string? operation, IReadOnlyDictionary<string, object?> bindings)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(bindings);
        foreach (var type in types)
        {
            if (operation is not null && _actions.TryGetValue($"{type}/{operation}", out var byOperation)) return Substitute(byOperation, bindings);
            if (_actions.TryGetValue($"{type}/{kind}", out var byKind)) return Substitute(byKind, bindings);
        }
        if (operation is not null && _actions.TryGetValue(operation, out var mapped)) return Substitute(mapped, bindings);
        return _actions.TryGetValue(kind, out var forKind) ? Substitute(forKind, bindings) : operation ?? kind;
    }

    /// <summary>The row id of a form item known by <paramref name="key"/>.</summary>
    public string PropertyId(string key) => _properties.GetValueOrDefault(key, key);

    private string Substitute(string id, IReadOnlyDictionary<string, object?> bindings) =>
        Placeholder().Replace(id, match =>
            bindings.TryGetValue(match.Groups[1].Value, out var value) && value is string text
                ? Types.GetValueOrDefault(text, text)
                : match.Value);

    private static Dictionary<string, string> Strings(JsonElement block, string name) =>
        DislJson.Members(block, name)
            .Where(member => member.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(member => member.Name, member => member.Value.GetString()!, StringComparer.Ordinal);

    [GeneratedRegex("<([A-Za-z_][A-Za-z0-9_]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
