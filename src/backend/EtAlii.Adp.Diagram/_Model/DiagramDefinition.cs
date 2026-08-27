using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// What a diagram-type module is: its origin/notation and its display title - mirroring one row
/// of docs/diagrams.md's catalog table. Each diagram-type project exposes one or more of these
/// through its own static <c>Diagram.Definitions</c> array - one entry for most types, seven
/// for C4, whose types share a single engine and have no reason to be seven assemblies.
/// </summary>
/// <param name="Description">
/// One sentence saying what this diagram type is for, shown beside the choice when a user
/// picks a type in the Add dialog. Written from the reader's point of view - what the diagram
/// answers - rather than restating the title, so a user meeting a notation for the first time
/// can tell whether it is the one they want.
/// </param>
/// <param name="Extension">
/// The extension, dot included, of the sibling file that holds this type's document body -
/// <c>".mm"</c> for a mindmap. Empty means the <c>.adp</c> registration file is the whole
/// diagram. Declared here so core can name the sibling by construction, without carrying a
/// mapping from MIME type to extension for every type (mindmap-diagram Requirement 2.2).
/// </param>
/// <param name="SharedExtension">
/// Whether <paramref name="Extension"/> is too common for this type to claim on sight.
/// <c>.mm</c>, <c>.owm</c> and <c>.dsl</c> belong to one type or one vendor's family, so a file
/// carrying one is that type's document and is routed as such. <c>.yml</c> does not: a
/// repository is full of workflows, compose files and manifests that are not pipelines, and a
/// type that claimed the extension would claim all of them.
/// <para>
/// A shared extension is never routed from a bare body. Such a file becomes a diagram only when
/// the user says so, by registering it through Add on a file, which writes the <c>.adp</c> that
/// routes it from then on (azure-pipeline-diagram Requirements 2.1-2.3).
/// </para>
/// </param>
/// <param name="Subject">
/// What the diagram <em>is</em>: the document, or the folder its <c>.adp</c> sits in. Defaulted
/// to <see cref="DiagramSubject.Document"/> - what every type before <c>ansible/structure</c>
/// is - so a definition that declares nothing keeps today's behaviour exactly.
/// <para>
/// Declaring <see cref="DiagramSubject.Folder"/> alongside an <paramref name="Extension"/> is a
/// contradiction: a folder has no sibling body to name. <see cref="DiagramDefinitionDiscovery"/>
/// drops such a definition rather than picking one of the two to believe, the same way it drops
/// any other malformed one.
/// </para>
/// </param>
public sealed record DiagramDefinition(
    DiagramOrigin Origin,
    string Title,
    string Description = "",
    string Extension = "",
    bool SharedExtension = false,
    DiagramSubject Subject = DiagramSubject.Document,
    Action<IHostApplicationBuilder>? Build = null!)
{
    /// <summary>Whether this type keeps its body in a sibling file rather than in the <c>.adp</c> file itself.</summary>
    public bool HasDocumentSibling => Extension.Length > 0;

    /// <summary>
    /// Whether this type's diagram is a folder rather than a document - the question core asks
    /// when it has to decide what to hand a validator, and what a change on disk affects
    /// (ansible-structure-diagram Requirement 2.1).
    /// </summary>
    public bool HasFolderSubject => Subject == DiagramSubject.Folder;

    /// <summary>
    /// Whether a file carrying this type's extension may be routed to it without an <c>.adp</c>
    /// registration beside it. False for a shared extension, which the user registers explicitly.
    /// </summary>
    public bool RoutesBareBody => HasDocumentSibling && !SharedExtension;
}
