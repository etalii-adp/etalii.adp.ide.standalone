namespace EtAlii.Adp.Designer;

/// <summary>
/// How a designer type recognises its document by the document's own content rather than by a registration: the
/// envelope on its first line (DED section 2, FBL sections 12.1 and 12.3). A body of one of <paramref name="Extensions"/>
/// that <paramref name="Matches"/> is the type's document whatever registration stands beside it, and opens without one.
/// </summary>
/// <param name="Extensions">The extensions the claim covers, dot included; each is shared with other tools.</param>
/// <param name="Matches">
/// Whether a file of that name, whose first line is the bytes given, is the type's document. A module answers it with
/// its binding's marker, through the FBL library's routing.
/// </param>
/// <param name="Envelope">The line a file needs on its first line to be the type's document, for a refusal to name.</param>
/// <param name="CreatesRegistration">
/// Whether the registration is created with the first setting the user changes (<c>createOnFirstPlacement</c>, FBL
/// section 8.4), so a new document is its body alone and deleting the registration leaves the body.
/// </param>
public sealed record DesignerClaim(
    IReadOnlyList<string> Extensions,
    Func<string, byte[], bool> Matches,
    string Envelope,
    bool CreatesRegistration);
