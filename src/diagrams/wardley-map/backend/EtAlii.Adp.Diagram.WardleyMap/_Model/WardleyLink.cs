namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A link between two named elements (Requirement 5.3).
/// </summary>
/// <param name="Source">The name on the left of the arrow, as written.</param>
/// <param name="Target">The name on the right, as written.</param>
/// <param name="Kind">Dependency or flow.</param>
/// <param name="Line">The 1-based line that declares it.</param>
/// <param name="Context">The text after `;`, which the notation uses to say why the link exists. Empty when absent.</param>
/// <remarks>
/// Endpoints are held by <b>name</b> rather than by identity, because that is what the document
/// says and this model is a reading of the document. Resolving names to identities is the
/// mapper's job, and a name that resolves to nothing is a problem Requirement 14.2 reports
/// rather than a parse failure - the map still opens with the link marked.
/// </remarks>
public sealed record WardleyLink(
    string Source,
    string Target,
    WardleyLinkKind Kind,
    uint Line,
    string Context = "");
