namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>The sizes the document does not state per element.</summary>
/// <remarks>
/// <para>
/// <b>Four of the five element types share one height and the document never carries it.</b> A UI
/// Element, Data Element, Action and Function are all <see cref="SharedHeight"/> tall; only a
/// Comment stores a <c>height</c>, because only a Comment grows to fit text the author wrote.
/// So the shared height is stated once, here, rather than written into every entry - which also
/// means changing it later is one edit rather than a migration of every document in the wild.
/// </para>
/// <para>
/// <b>The mapper sends it and the validator does not read it.</b> Nothing in the rules depends on
/// how tall a box is, so a height that disagreed with this constant could only come from a
/// document that stated one where it should not - which is an unreadable entry, reported under
/// that rule rather than silently honoured.
/// </para>
/// </remarks>
public static class FdgGeometry
{
    /// <summary>The height of every element except a Comment, in canvas units.</summary>
    public const double SharedHeight = 48;

    /// <summary>The narrowest an element may be. A smaller value in the file opens and is reported.</summary>
    public const double MinimumWidth = 80;

    /// <summary>The shortest a Comment may be. A smaller value in the file opens and is reported.</summary>
    public const double MinimumCommentHeight = 48;
}
