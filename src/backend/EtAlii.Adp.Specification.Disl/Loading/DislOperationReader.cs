using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Reads <c>behavior.operations</c> and <c>behavior.editGate</c> (DISL §9.1, §9.3) into <see cref="DislOperations"/>, every
/// expression with the program the loader compiled at its pointer.
/// </summary>
/// <remarks>
/// Reading never refuses and never reports: an operation the runtime threw on, or ignored a member of, when it read
/// the JSON at run time is read as one that does the same when it runs.
/// </remarks>
internal static class DislOperationReader
{
    private const string Pointer = "/behavior/operations";

    public static DislOperations Read(DislSpecification specification, JsonElement root)
    {
        var operations = new Dictionary<string, DislOperation>(StringComparer.Ordinal);
        if (!root.TryGetProperty("behavior", out var behavior)) return new DislOperations(operations, [], null);
        if (DislFieldReader.NotAnObject(behavior) is { } malformed) return new DislOperations(operations, [], malformed);

        var gate = behavior.TryGetProperty("editGate", out var declared) ? DislFieldReader.Reasons(specification, declared, "/behavior/editGate") : [];
        if (!behavior.TryGetProperty("operations", out var all)) return new DislOperations(operations, gate, null);
        if (DislFieldReader.NotAnObject(all) is { } notAnObject) return new DislOperations(operations, gate, notAnObject);

        foreach (var operation in all.EnumerateObject())
        {
            operations[operation.Name] = Operation(specification, operation.Name, operation.Value, DislJson.Pointer(Pointer, operation.Name));
        }
        return new DislOperations(operations, gate, null);
    }

    private static DislOperation Operation(DislSpecification specification, string id, JsonElement operation, string pointer)
    {
        var targets = DislJson.Strings(operation, "for");
        if (DislFieldReader.NotAnObject(operation) is { } malformed) return new DislOperation(id, targets, [], null, null, DislActionList.Empty, malformed);

        return new DislOperation(
            id,
            targets,
            operation.TryGetProperty("unavailable", out var unavailable) ? DislFieldReader.Reasons(specification, unavailable, DislJson.Pointer(pointer, "unavailable")) : [],
            operation.TryGetProperty("enabled", out var enabled) ? DislFieldReader.Expression(specification, enabled, DislJson.Pointer(pointer, "enabled")) : null,
            operation.TryGetProperty("plugin", out var plugin) ? Plugin(plugin, DislJson.Pointer(pointer, "plugin")) : null,
            operation.TryGetProperty("actions", out var actions) ? DislActionReader.List(specification, actions, DislJson.Pointer(pointer, "actions")) : DislActionList.Empty,
            null);
    }

    /// <summary>A <c>plugin</c>: a name, or <c>{name, args}</c>, its arguments the values they read as.</summary>
    private static DislOperationPlugin Plugin(JsonElement plugin, string pointer)
    {
        var name = plugin.ValueKind == JsonValueKind.String ? plugin.GetString()! : DislJson.String(plugin, "name") ?? "";
        var argumentsAt = DislJson.Pointer(pointer, "args");
        return new DislOperationPlugin(name, [.. DislJson.Members(plugin, "args").Select(argument => (argument.Name, new DislActionValue.Literal(DislJson.Pointer(argumentsAt, argument.Name), DislValues.Json(argument.Value))))]);
    }
}
