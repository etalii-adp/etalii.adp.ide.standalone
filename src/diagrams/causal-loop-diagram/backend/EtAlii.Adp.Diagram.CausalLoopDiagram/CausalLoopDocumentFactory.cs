using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The starter document a new causal loop diagram is created as: the smallest thing that is
/// actually a causal loop diagram rather than an empty file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered in the module's first task, deliberately.</b> A definition that declares a
/// document extension and registers no factory is a startup error naming the type
/// (<c>DiagramDocumentFactories.Verify</c>), so a module without one does not merely lack a
/// create path - the host does not boot. sparql was corrected for exactly this and plantuml
/// carries it as an open task; the specification made it Requirement 5.1 rather than an
/// implementation detail so it could not be deferred again.
/// </para>
/// <para>
/// <b>The starter is a loop, not two loose variables.</b> A new file of this type should show
/// what the type is for on the first open: two variables that feed each other positively are a
/// reinforcing loop, and the smallest honest example of the notation's whole point. Its
/// <c>R1</c> label is arithmetically correct - zero negative links is an even count - so the
/// validator has nothing to say about a file the tool itself just wrote.
/// </para>
/// <para>
/// CRLF and a trailing newline: a file ADP creates has no existing style to preserve, and CRLF
/// is the repository's house style.
/// </para>
/// </remarks>
public sealed class CausalLoopDocumentFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        return "causal-loop 1\r\n"
            + "\r\n"
            + "variable population \"Population\"\r\n"
            + "variable births \"Births\"\r\n"
            + "\r\n"
            + "link population -> births +\r\n"
            + "link births -> population +\r\n"
            + "\r\n"
            + "loop R1 \"Births beget births\" population births\r\n";
    }
}
