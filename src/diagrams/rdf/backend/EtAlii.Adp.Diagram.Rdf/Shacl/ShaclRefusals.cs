namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The sentences this reading refuses with, in one place because two layers must say the same
/// thing: the writer refuses when called directly and the provider marks the gesture unavailable
/// without consulting the writer, and a user who meets both must read one refusal rather than two
/// that drifted apart (shacl-diagram Requirements 3.3, 5.7, 6.4).
/// </summary>
public static class ShaclRefusals
{
    /// <summary>
    /// Mutating or removing content rooted in a blank node - which is nearly every property shape
    /// as the world writes them. The **blank-node identity boundary**: such a node has no identity
    /// that survives a reparse, so nothing keyed to it can be undone honestly.
    /// </summary>
    public const string BlankRooted =
        "This constraint is written as a blank node, which has no identity that survives a reparse, so an edit keyed to it could not be undone reliably. Open the file as text to change it, or give the shape an IRI of its own.";

    /// <summary>The shape a gesture names is not in the file (or is not IRI-named at all).</summary>
    public const string NoSuchShape =
        "No IRI-named shape of that name is in this file, so there is nothing to edit.";

    /// <summary>A property row was asked for without the one thing SHACL requires of it.</summary>
    public const string PathRequired =
        "A property shape needs exactly one sh:path, so a path is required before the row can be written.";

    /// <summary>The target declaration a removal names is not stated in the file.</summary>
    public const string NoSuchTarget =
        "That target is not declared in this file, so there is nothing to remove.";

    /// <summary>An implicit class target is not stated by any triple, so no edit can remove it.</summary>
    public const string ImplicitTarget =
        "This shape targets its own instances because the file states it to be a class, not through a target triple, so there is no declaration to remove. Remove the class typing instead, as text.";
}
