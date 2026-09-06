namespace EtAlii.Adp.Common;

/// <summary>
/// What a diagram type's diagram actually <em>is</em> - the thing its module reads and its
/// rules judge.
/// </summary>
/// <remarks>
/// Every type up to <c>ansible/structure</c> answered <see cref="Document"/>, so core could
/// assume it: validation was handed a document's text, a problem was attributed to the file
/// that produced it, and a change to a file that was not itself a diagram meant nothing. A
/// folder-subject type breaks all three assumptions at once, and this is how it says so -
/// without core learning which type it is or what is in the folder.
/// </remarks>
public enum DiagramSubject
{
    /// <summary>
    /// The diagram is the document: the <c>.adp</c> registration, or the sibling body it
    /// names. The default, and what every type before <c>ansible/structure</c> is.
    /// </summary>
    Document,

    /// <summary>
    /// The diagram is the folder the <c>.adp</c> sits in, and the files beneath it. The
    /// registration marks the folder and carries no body of its own, which is why a
    /// <see cref="Folder"/> type may not declare a <see cref="DiagramDefinition.Extension"/>.
    /// </summary>
    Folder,
}
