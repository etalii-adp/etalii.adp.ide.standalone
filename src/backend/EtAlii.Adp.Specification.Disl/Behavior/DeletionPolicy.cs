using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>A confirmation to ask before a transaction (DISL §9.5), its texts evaluated.</summary>
public sealed record DislConfirmation(string Title, string Message, string ConfirmLabel, string? CancelLabel, bool Danger, long? Count);

/// <summary>
/// What deleting an element takes with it, and whether to ask first (DISL §9.5): the type's
/// <c>behavior.deletion</c>, found along its linearisation, with the defaults for a type without one.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Changes"/> gives every removal</b>: the relations at the element and at every node
/// beneath it (<c>relations: "delete"</c>), the nodes beneath it deepest first (<c>children: "delete"</c>),
/// then the element itself; a reference to any of them is unset (<c>references: "unset"</c>). A
/// <c>forbid</c> that applies refuses the deletion. <c>reconnect</c>, <c>reparent</c> and
/// <c>delete-referencing</c> are refused as not supported yet; neither bundled definition uses them.
/// </para>
/// <para>
/// <b><see cref="Confirmation"/> is null when nothing is to be asked</b>: no <c>confirm</c>, a
/// <c>when</c> that does not hold, or a <c>count</c> below its <c>threshold</c>.
/// </para>
/// </remarks>
public static class DeletionPolicy
{
    /// <summary>The confirmation deleting <paramref name="self"/> asks for, or null when it asks nothing.</summary>
    public static DislConfirmation? Confirmation(DislSpecification specification, DislElement self, IReadOnlyList<DislElement>? selection = null, DislEnv? env = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);
        if (Policy(specification, self) is not var (policy, pointer) || !policy.TryGetProperty("confirm", out var confirm)) return null;

        var at = DislJson.Pointer(pointer, "confirm");
        var variables = OperationInterpreter.Variables(DislContexts.GestureDelete, self.Diagram, env);
        variables["self"] = self;
        variables["selection"] = selection?.Cast<object?>().ToList() ?? [self];
        variables["count"] = null;

        if (confirm.ValueKind != JsonValueKind.Object || confirm.TryGetProperty("cel", out _) || !confirm.TryGetProperty("message", out var message))
        {
            // A plain Message: asked every time (§9.5).
            return new DislConfirmation("Delete", DislEvaluation.Message(specification, confirm, at, DislContexts.GestureDelete, variables, ""), "Delete", null, true, null);
        }

        long? count = null;
        if (confirm.TryGetProperty("count", out var counted))
        {
            if (DislEvaluation.Expression(specification, counted, DislJson.Pointer(at, "count"), DislContexts.GestureDelete, variables) is not long value) return null;
            count = value;
            variables["count"] = value;
            var threshold = confirm.TryGetProperty("threshold", out var declared) && declared.TryGetInt64(out var limit) ? limit : 0;
            if (value < threshold) return null;
        }
        if (confirm.TryGetProperty("when", out var when) && !DislEvaluation.Holds(specification, when, DislJson.Pointer(at, "when"), DislContexts.GestureDelete, variables)) return null;

        return new DislConfirmation(
            Text(confirm, "title", "Delete"),
            DislEvaluation.Message(specification, message, DislJson.Pointer(at, "message"), DislContexts.GestureDelete, variables, ""),
            Text(confirm, "confirmLabel", "Delete"),
            confirm.TryGetProperty("cancelLabel", out _) ? Text(confirm, "cancelLabel", "Cancel") : null,
            !confirm.TryGetProperty("danger", out var danger) || danger.ValueKind != JsonValueKind.False,
            count);

        string Text(JsonElement owner, string name, string fallback) =>
            owner.TryGetProperty(name, out var text) ? DislEvaluation.Message(specification, text, DislJson.Pointer(at, name), DislContexts.GestureDelete, variables, fallback) : fallback;
    }

    /// <summary>The removals deleting <paramref name="self"/> makes, in the order they are made.</summary>
    public static DislTransaction Changes(DislSpecification specification, DislElement self)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);

        var policy = Policy(specification, self)?.Policy;
        var children = policy is { } declared ? DislJson.String(declared, "children") ?? "delete" : "delete";
        var relations = policy is { } withRelations ? DislJson.String(withRelations, "relations") ?? "delete" : "delete";
        var references = policy is { } withReferences ? DislJson.String(withReferences, "references") ?? "unset" : "unset";

        var subtree = self.Type.IsRelation ? [] : self.Descendants().ToList();
        if (subtree.Count > 0 && children != "delete") return DislTransaction.Refused(children == "forbid" ? "This cannot be deleted while it has children." : $"Deleting with children: \"{children}\" is not supported yet.");

        List<DislElement> gone = [.. subtree, self];
        var attached = self.Diagram.Relations.Where(relation => !gone.Contains(relation) && (gone.Contains(relation.Source!) || gone.Contains(relation.Target!))).ToList();
        if (attached.Count > 0 && relations != "delete") return DislTransaction.Refused(relations == "forbid" ? "This cannot be deleted while relations end at it." : $"Deleting with relations: \"{relations}\" is not supported yet.");

        List<DislChange> changes = [.. attached.Select(relation => new DislChange.Remove(relation.Id))];
        gone.AddRange(attached);

        foreach (var element in self.Diagram.Elements.Where(element => !gone.Contains(element)))
        {
            var unset = element.Type.Attributes.Values
                .Where(attribute => self.Diagram.Specification.Metamodel.TypeOf(attribute.Type) is not null && Refers(element.ValueOf(attribute.Name), gone))
                .ToDictionary(attribute => attribute.Name, _ => (object?)null, StringComparer.Ordinal);
            if (unset.Count == 0) continue;
            if (references != "unset") return DislTransaction.Refused(references == "forbid" ? "This cannot be deleted while something refers to it." : $"Deleting with references: \"{references}\" is not supported yet.");
            changes.Add(new DislChange.Set(element.Id, element.Type.Name, unset));
        }

        changes.AddRange(subtree.AsEnumerable().Reverse().Select(node => new DislChange.Remove(node.Id)));
        changes.Add(new DislChange.Remove(self.Id));
        return new DislTransaction(changes, [], null);
    }

    private static bool Refers(object? value, List<DislElement> gone) => value switch
    {
        DislElement element => gone.Contains(element),
        IReadOnlyList<object?> list => list.Any(item => item is DislElement element && gone.Contains(element)),
        _ => false,
    };

    /// <summary>The deletion policy of the first type along <paramref name="self"/>'s linearisation that declares one.</summary>
    private static (JsonElement Policy, string Pointer)? Policy(DislSpecification specification, DislElement self)
    {
        if (!specification.Root.TryGetProperty("behavior", out var behavior) || !behavior.TryGetProperty("deletion", out var deletion) || deletion.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var type in self.Type.Linearisation)
        {
            if (deletion.TryGetProperty(type, out var policy)) return (policy, DislJson.Pointer("/behavior/deletion", type));
        }
        return null;
    }
}
