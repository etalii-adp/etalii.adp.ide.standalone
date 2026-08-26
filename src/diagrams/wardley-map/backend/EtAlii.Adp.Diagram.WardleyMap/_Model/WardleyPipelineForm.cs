namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Which of the two pipeline syntaxes a document used. Recorded so the writer gives the file
/// back in the form it was read, rather than silently upgrading it (Requirements 3.2, 5.4).
/// </summary>
public enum WardleyPipelineForm
{
    /// <summary>
    /// The current form: `pipeline Parent` followed by a braced block of children, each
    /// carrying an evolution position only.
    /// </summary>
    Nested,

    /// <summary>
    /// The legacy form: `pipeline Parent [0.30, 0.85]` on one line, with no children of its own.
    /// Readable, and written back this way - upgrading it would be an unrequested edit to
    /// someone else's file.
    /// </summary>
    Legacy,
}
