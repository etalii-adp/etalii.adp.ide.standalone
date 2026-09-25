namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Whether a link may be written: the same three checks the canvas makes before it offers a target.
/// </summary>
/// <remarks>
/// <para>
/// <b>The backend checks again, and does not trust the client.</b> A stale canvas, a second window or
/// a scripted request can ask for a link the canvas would never have offered. This is what stops
/// such a request writing a cycle, a second parent or a forbidden pair into the document.
/// </para>
/// <para>
/// <b>Three independent checks, in the design's order: type, cardinality, cycle</b> (Requirement 5.4).
/// The refusal names which one failed, so a user who could not see why a link was refused is told.
/// All three read the one table, <see cref="FdgRelations"/>, and the cycle check is
/// <see cref="FdgOwnership.WouldClose"/>, so this adds no rule of its own.
/// </para>
/// </remarks>
public static class FdgConnectVerdict
{
    /// <summary>The refusal for linking <paramref name="fromId"/> to <paramref name="toId"/> by <paramref name="relationType"/>, or null when it may be written.</summary>
    public static string? RefusalFor(FdgModel model, string relationType, string fromId, string toId)
    {
        ArgumentNullException.ThrowIfNull(model);

        var relation = FdgRelations.ById(relationType);
        if (relation is null)
        {
            return $"This notation has no `{relationType}` relation.";
        }

        var from = model.Elements.FirstOrDefault(element => string.Equals(element.Id, fromId, StringComparison.Ordinal));
        var to = model.Elements.FirstOrDefault(element => string.Equals(element.Id, toId, StringComparison.Ordinal));
        if (from is null || to is null)
        {
            return "That link names an element that is no longer in this graph.";
        }

        // The type check.
        if (string.Equals(from.Id, to.Id, StringComparison.Ordinal))
        {
            return "Refused by the type check: no relation links an element to itself.";
        }

        if (!relation.Admits(from.Type, to.Type))
        {
            return $"Refused by the type check: `{relation.Id}` does not link a {from.Type} to a {to.Type}.";
        }

        // The cardinality check.
        if (relation.MaxIntoTarget is { } intoTarget
            && model.Connections.Count(connection => connection.Type == relation.Id && connection.To == to.Id) >= intoTarget)
        {
            return $"Refused by the cardinality check: `{NameOf(to)}` already has its `{relation.Id}` parent, and may have {intoTarget}.";
        }

        if (relation.MaxFromSource is { } fromSource
            && model.Connections.Count(connection => connection.Type == relation.Id && connection.From == from.Id) >= fromSource)
        {
            return $"Refused by the cardinality check: `{NameOf(from)}` already has its `{relation.Id}` link, and may have {fromSource}.";
        }

        // The cycle check: ownership only, Shows exempt (Requirement 5.4).
        if (relation.IsOwnership && FdgOwnership.WouldClose(model, from.Id, to.Id))
        {
            return $"Refused by the cycle check: `{NameOf(to)}` already owns `{NameOf(from)}`, so this link would make an ownership loop.";
        }

        return null;
    }

    private static string NameOf(FdgElement element) => element.Name.Length > 0 ? element.Name : element.Id;
}
