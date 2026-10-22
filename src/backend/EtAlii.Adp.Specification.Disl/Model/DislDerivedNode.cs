namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A derived node type's <c>derived</c> (DISL §4.11.2), typed at load: each expression with the program the loader
/// compiled at its pointer, which <see cref="DislModelBuilder"/> evaluates when it builds a model.
/// </summary>
/// <param name="From">The items, in the <c>derive</c> context.</param>
/// <param name="Key">What groups the items, or null when it states none and each item is a group of its own.</param>
/// <param name="Id">A group's id.</param>
/// <param name="Parent">A group's parent, or null when it states none and the node is at the top level.</param>
/// <param name="Slot">The slot in the parent, or null when it states none.</param>
/// <param name="Sources">The elements a group's node is derived from, or null when it states none.</param>
/// <param name="Attributes">Its <c>attributes</c>, by name, in declaration order.</param>
internal sealed record DislDerivedNode(
    DislDerivedField From,
    DislDerivedField? Key,
    DislDerivedField Id,
    DislDerivedField? Parent,
    DislDerivedField? Slot,
    DislDerivedField? Sources,
    IReadOnlyList<(string Name, DislDerivedField Value)> Attributes);
