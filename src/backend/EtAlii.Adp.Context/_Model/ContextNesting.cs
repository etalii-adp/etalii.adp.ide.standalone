namespace EtAlii.Adp.Context;

/// <summary>
/// How things of one kind relate to a selection nested under them. Declared by the
/// kind's <see cref="IContextSourceResolver"/>, never by a client.
/// </summary>
public enum ContextNesting
{
    /// <summary>The child lives inside the parent; its path is relative to the parent's.</summary>
    Contained,

    /// <summary>Nothing sensible lives inside a thing of this kind; a child is rejected.</summary>
    NotNestable,
}
