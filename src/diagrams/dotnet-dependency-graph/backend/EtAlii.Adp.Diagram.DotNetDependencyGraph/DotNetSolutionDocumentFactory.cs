using EtAlii.Adp.Common;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// The body a brand-new <c>dotnet/dependency-graph</c> diagram starts from: an empty
/// <c>.slnx</c> solution.
/// </summary>
/// <remarks>
/// <para>
/// <b>PROVISIONAL, and awaiting a ruling. This class exists because core requires it, and it
/// sits against a written requirement.</b>
/// </para>
/// <para>
/// The chain is: Requirement 2.3 asks for the solution to be named rather than inferred, so the
/// design binds through a <c>body:</c> header; core honours a <c>body:</c> header only for a
/// type whose definition declares a document extension (<c>DiagramFilePair.BodyOf</c> returns
/// null unless <c>HasDocumentSibling</c>); and the host refuses to start when a type declares an
/// extension and registers no <see cref="IDiagramDocumentFactory"/>. So the binding the design
/// chose forces this class to exist.
/// </para>
/// <para>
/// <b>What that costs.</b> The requirements say the type "SHALL never write to a
/// <c>.csproj</c>, <c>.sln</c> or <c>.slnx</c>". Creating a new empty solution through Add is
/// ADP authoring a build file, which reads against that sentence - even though it creates a new
/// file at explicit request rather than editing a solution anyone already had. The reading this
/// implementation takes is that the requirement's target is the diagram never editing the
/// solution it draws, and that a new empty solution is not that. <b>That reading is the
/// Architect's to confirm or overturn</b>, and this file is where the decision lands either way:
/// overturning it means dropping the extension declaration and accepting ansible-style folder
/// binding, which loses Requirement 2.3.
/// </para>
/// <para>
/// <b>The ordinary way in is not this path at all.</b> The type declares a shared extension, so
/// a solution becomes a diagram when a user registers one that already exists - which is the
/// flow the whole module is built around. This factory serves only "New diagram of this type in
/// an empty folder", where there is nothing yet to draw.
/// </para>
/// </remarks>
public sealed class DotNetSolutionDocumentFactory : IDiagramDocumentFactory
{
    public DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <summary>
    /// An empty solution in the newer serialization: valid, openable by the .NET tooling, and
    /// drawn by this type as a graph with nothing in it until the user adds projects with their
    /// own tools.
    /// </summary>
    public string CreateEmptyDocument(string baseName) => "<Solution>\r\n</Solution>\r\n";
}
