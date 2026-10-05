using System.Text.Json;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What changing a node's type makes (DISL §9.5, <c>behavior.retype</c>): the types it may become, and the
/// values the new type's attributes take from the old element through <c>attributeMapping</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The change carries the mapped values only</b>: an attribute both types declare is kept by whoever
/// writes the change, as the specification's "keeping compatible attributes" says, and a mapping for an
/// attribute the new type does not declare is not evaluated.
/// </para>
/// <para>
/// <b>A mapping is evaluated in the <c>retype</c> context</b> (§12.3), with <c>self</c> and <c>old</c> both the
/// element as it is before the change.
/// </para>
/// <para>
/// <b>A type change not declared is refused</b>: from a type with no entry along its linearisation, to a
/// type its entry's <c>to</c> does not list, of a derived element, or to a type that is not a concrete
/// node type. The working diagram is not changed; the host writes the change and reads the model again.
/// </para>
/// </remarks>
public static class RetypePolicy
{
    /// <summary>The transaction that changes <paramref name="self"/>'s type to <paramref name="type"/>.</summary>
    public static DislTransaction Change(DislSpecification specification, DislElement self, string type, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(type);

        if (self.IsDerived) return DislTransaction.Refused($"{self.Type.Name} is derived, so its type cannot be changed.");
        if (specification.Metamodel.TypeOf(type) is not { IsRelation: false, Abstract: false } target) return DislTransaction.Refused($"A {self.Type.Name} cannot become a {type}: that is no type of node.");
        if (Policy(specification, self) is not var (policy, pointer) || !DislJson.Strings(policy, "to").Contains(type))
        {
            return DislTransaction.Refused($"A {self.Type.Name} cannot become a {type}.");
        }

        var variables = OperationInterpreter.Variables(DislContexts.Retype, self.Diagram, env);
        variables["self"] = self;
        variables["old"] = self;

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var mapping in DislJson.Members(policy, "attributeMapping"))
        {
            if (!target.Attributes.ContainsKey(mapping.Name)) continue;
            var value = DislEvaluation.Expression(specification, mapping.Value, DislJson.Pointer(DislJson.Pointer(pointer, "attributeMapping"), mapping.Name), DislContexts.Retype, variables);
            if (value is CelError error) return DislTransaction.Refused(error.Message);
            attributes[mapping.Name] = value;
        }

        return new DislTransaction([new DislChange.Retype(self.Id, type, attributes)], [], null);
    }

    /// <summary>The retype entry of the first type along <paramref name="self"/>'s linearisation that declares one.</summary>
    private static (JsonElement Policy, string Pointer)? Policy(DislSpecification specification, DislElement self)
    {
        if (!specification.Root.TryGetProperty("behavior", out var behavior) || !behavior.TryGetProperty("retype", out var retype) || retype.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var type in self.Type.Linearisation)
        {
            if (retype.TryGetProperty(type, out var policy) && policy.ValueKind == JsonValueKind.Object) return (policy, DislJson.Pointer("/behavior/retype", type));
        }
        return null;
    }
}
