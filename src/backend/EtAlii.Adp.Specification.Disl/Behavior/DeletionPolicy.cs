namespace EtAlii.Adp.Specification.Disl;

/// <summary>A confirmation to ask before a transaction (DISL §9.5), its texts evaluated.</summary>
public sealed record DislConfirmation(string Title, string Message, string ConfirmLabel, bool Danger, long? Count);

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
/// <b>A derived relation is not removed</b> (§4.11.5): it follows from its sources, so it goes when they
/// go. <b>Children written inside their parent</b>, as an outline's items are, go with the parent's own
/// removal: with <c>nested</c>, the nodes beneath it get no removal of their own, and the one removal
/// takes them all.
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
        if (specification.Behavior.DeletionOf(self.Type.Linearisation) is not { } policy) return null;
        // A policy that is not an object threw here when it was read from the JSON at run time, and still does.
        if (policy.NotAnObject is { } malformed) throw new InvalidOperationException(malformed);
        if (policy.Confirm is not { } confirm) return null;

        var variables = OperationInterpreter.Variables(DislContext.GestureDelete, self.Diagram, env);
        variables["self"] = self;
        variables["selection"] = selection?.Cast<object?>().ToList() ?? [self];
        variables["count"] = null;

        // A plain Message: asked every time (§9.5).
        if (confirm is DislDeletionConfirm.Always always) return new DislConfirmation("Delete", always.Message.Text(variables, ""), "Delete", true, null);
        var asked = (DislDeletionConfirm.Asked)confirm;

        long? count = null;
        if (asked.Count is { } counted)
        {
            if (counted.Evaluate(variables) is not long value) return null;
            count = value;
            variables["count"] = value;
            // A threshold that is not a number threw here when it was read from the JSON at run time, and still does.
            if (asked.ThresholdMalformed is { } notANumber) throw new InvalidOperationException(notANumber);
            if (value < asked.Threshold) return null;
        }
        if (asked.When is { } when && !when.Holds(variables)) return null;

        return new DislConfirmation(
            asked.Title?.Text(variables, "Delete") ?? "Delete",
            asked.Message.Text(variables, ""),
            asked.ConfirmLabel?.Text(variables, "Delete") ?? "Delete",
            asked.Danger,
            count);
    }

    /// <summary>The removals deleting <paramref name="self"/> makes, in the order they are made.</summary>
    /// <param name="specification">The definition.</param>
    /// <param name="self">The element deleted.</param>
    /// <param name="nested">Whether the persistence writes a node's children inside it, so that its removal takes them with it.</param>
    public static DislTransaction Changes(DislSpecification specification, DislElement self, bool nested = false)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(self);

        var policy = specification.Behavior.DeletionOf(self.Type.Linearisation);
        var children = policy?.Children ?? "delete";
        var relations = policy?.Relations ?? "delete";
        var references = policy?.References ?? "unset";

        var subtree = self.Type.IsRelation ? [] : self.Descendants().ToList();
        if (subtree.Count > 0 && children != "delete") return DislTransaction.Refused(children == "forbid" ? "This cannot be deleted while it has children." : $"Deleting with children: \"{children}\" is not supported yet.");

        List<DislElement> gone = [.. subtree, self];
        var attached = self.Diagram.Relations.Where(relation => !relation.IsDerived && !gone.Contains(relation) && (gone.Contains(relation.Source!) || gone.Contains(relation.Target!))).ToList();
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

        if (!nested) changes.AddRange(subtree.AsEnumerable().Reverse().Select(node => new DislChange.Remove(node.Id)));
        changes.Add(new DislChange.Remove(self.Id));
        return new DislTransaction(changes, [], null);
    }

    private static bool Refers(object? value, List<DislElement> gone) => value switch
    {
        DislElement element => gone.Contains(element),
        IReadOnlyList<object?> list => list.Any(item => item is DislElement element && gone.Contains(element)),
        _ => false,
    };
}
