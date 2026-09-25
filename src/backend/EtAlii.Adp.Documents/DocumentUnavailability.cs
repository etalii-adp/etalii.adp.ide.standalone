namespace EtAlii.Adp.Documents;

/// <summary>
/// Why a document could not be read from its file - the two cases a store has to tell apart.
/// </summary>
/// <remarks>
/// <b>They are different paths, and they diverge.</b> A MISSING body is either not written yet or
/// gone; a PRESENT-BUT-UNREADABLE one is almost always another program mid-save. Measured across the
/// ten module stores before <see cref="DocumentLifecycle{TDocument}"/> existed, six treated both the
/// same way and mindmap did not: a missing map opened empty while an unreadable one threw. Naming the
/// two lets a module that reports its own unavailability (backend-centralization R2.2) say which.
/// </remarks>
public enum DocumentUnavailability
{
    /// <summary>No file at the path.</summary>
    Missing,

    /// <summary>A file is there, but it could not be read - held open, or refused.</summary>
    Unreadable,
}
