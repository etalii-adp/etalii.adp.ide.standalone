using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A derived relation type's <c>derived</c> (DISL §4.9, §4.11.3), typed at load: each expression with the program the
/// loader compiled at its pointer, which <see cref="DislModelBuilder"/> evaluates when it builds a model.
/// </summary>
internal abstract record DislDerivedRelation
{
    /// <summary>The expression form (§4.9): one Expression in the <c>derive</c> context, a list of maps with a <c>source</c> and a <c>target</c>.</summary>
    public sealed record Listed(DislDerivedField Items) : DislDerivedRelation;

    /// <summary>The object form (§4.11.3): its items, how they group and what each group gives.</summary>
    /// <param name="From">The items, in the <c>derive</c> context.</param>
    /// <param name="Key">What groups the items, or null when it states none and each item is a group of its own.</param>
    /// <param name="Source">The source of a group's relation.</param>
    /// <param name="Target">The target of a group's relation.</param>
    /// <param name="Id">A group's id, or null when it states none and the id is made from the ends.</param>
    /// <param name="Sources">The elements a group's relation is derived from, or null when it states none.</param>
    /// <param name="Owner">The element a group's relation belongs to, or null when it states none and its ends decide it.</param>
    /// <param name="Attributes">Its <c>attributes</c>, by name, in declaration order.</param>
    public sealed record Mapped(
        DislDerivedField From,
        DislDerivedField? Key,
        DislDerivedField Source,
        DislDerivedField Target,
        DislDerivedField? Id,
        DislDerivedField? Sources,
        DislDerivedField? Owner,
        IReadOnlyList<(string Name, DislDerivedField Value)> Attributes) : DislDerivedRelation;
}

/// <summary>
/// One expression of a derived relation, typed at load: its position and the program the loader compiled there, or
/// null when it compiled none, as for a member that is absent or not an Expression.
/// </summary>
internal sealed record DislDerivedField(string Pointer, CelProgram? Program)
{
    /// <summary>The value; a <see cref="CelError"/> when no program was compiled here or it fails.</summary>
    public object? Evaluate(IReadOnlyDictionary<string, object?> variables) =>
        Program is { } program ? program.Evaluate(variables) : new CelError($"No expression was compiled at {Pointer}.");
}
