namespace EtAlii.Adp.Common;

/// <summary>
/// Everything an <see cref="IDiagramValidator"/> is given about the one diagram it is judging.
/// </summary>
/// <remarks>
/// <para>
/// A parameter object rather than a growing list of loose strings. The seam previously passed
/// the document text and a base name, which was everything a type whose diagram <em>is</em> its
/// text could want - and nothing at all for a rule that has to look at the disk. The mindmap
/// validator's own remarks named that as the reason it could not judge a link whose target file
/// is missing, and a folder-subject type (<see cref="DiagramSubject.Folder"/>) cannot write a
/// single rule without it.
/// </para>
/// <para>
/// Made a record so the next fact a validator turns out to need is an added property rather
/// than another signature change rippling through every implementer.
/// </para>
/// </remarks>
/// <param name="Document">
/// The body text as read. For a type with no <see cref="DiagramDefinition.Extension"/> this is
/// the <c>.adp</c> registration's own text, which for a folder-subject type is one MIME line
/// and says nothing - such a validator reads <see cref="SubjectFolder"/> instead.
/// </param>
/// <param name="BaseName">The diagram's file base name, for messages that want to name it.</param>
/// <param name="RootPath">The project root. Every path a problem names is relative to this.</param>
/// <param name="BodyPath">The document that was read, already resolved and containment-checked.</param>
/// <param name="RegistrationPath">
/// The <c>.adp</c> this was routed through, or null for a body opened without one. Equal to
/// <paramref name="BodyPath"/> for a type that keeps no sibling document.
/// </param>
public sealed record DiagramValidationRequest(
    string Document,
    string BaseName,
    string RootPath,
    string BodyPath,
    string? RegistrationPath)
{
    /// <summary>
    /// The folder a <see cref="DiagramSubject.Folder"/> type reads - the folder its
    /// registration sits in. Null for a <see cref="DiagramSubject.Document"/> type, which is
    /// every type but <c>ansible/structure</c>, so a validator that finds it null is being
    /// asked about a diagram whose subject is the text it was handed.
    /// </summary>
    public string? SubjectFolder { get; init; }
}
