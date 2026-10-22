using EtAlii.Adp.Designer;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Which tool serves a path (code-structure FR-058): the one place the precedence between the three
/// tool kinds is decided - diagrams, then designers, then editors. Each kind's router still answers
/// the questions of its own kind; what a caller combining them asks is asked here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Envelopes before all.</b> A body whose first line is the envelope of a designer type that claims it so is that
/// designer's document, registered or not, whatever another tool's registration says of it (spec 029 FR-002, R3); and
/// another tool's registration that names such a body is refused, saying where the body opens.
/// </para>
/// <para>
/// <b>Diagrams first.</b> A routed diagram is the answer, whether or not its body resolved inside the
/// project, and an unreadable registration is refused rather than offered on.
/// </para>
/// <para>
/// <b>Designers second.</b> A registration whose type no diagram module knows may be a designer's,
/// and so may a file no diagram type claims; a routed designer is the answer and an unreadable one is
/// refused, as for a diagram.
/// </para>
/// <para>
/// <b>Editors last.</b> Everything else - a file no diagram or designer type claims, a registration of
/// a type nothing deployed knows, a body whose extension two diagram types claim - is the editors'.
/// The fallback editor answers for whatever no other claims; an extension several editors claim
/// without one default is <see cref="NoTool"/>.
/// </para>
/// </remarks>
public sealed class ToolRouter
{
    private readonly DesignerFileRouter _designers;

    public ToolRouter(DiagramFileRouter diagrams, DesignerFileRouter designers, EditorResolver editors)
    {
        ArgumentNullException.ThrowIfNull(diagrams);
        ArgumentNullException.ThrowIfNull(designers);
        ArgumentNullException.ThrowIfNull(editors);

        Diagrams = diagrams;
        _designers = designers;
        Editors = editors;
    }

    /// <summary>The diagram types' own router, for what only diagrams are asked: which extensions they claim, and which take a folder as their subject.</summary>
    public DiagramFileRouter Diagrams { get; }

    /// <summary>The editors' own resolver, for what only editors are asked: whether one claims an extension.</summary>
    public EditorResolver Editors { get; }

    /// <summary>
    /// The tool that serves <paramref name="path"/>: <see cref="DiagramRouted"/>,
    /// <see cref="DesignerRouted"/>, <see cref="EditorRouted"/>, <see cref="ToolUnreadable"/> or
    /// <see cref="NoTool"/>, and nothing else.
    /// </summary>
    /// <param name="path">The file, absolute.</param>
    /// <param name="root">
    /// The project folder a <c>body:</c> header must stay inside; without it a header is not followed,
    /// which is enough for a caller asking only which kind the file is.
    /// </param>
    public ToolRouting Route(string path, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_designers.RouteByEnvelope(path) is { } enveloped)
        {
            return enveloped;
        }

        return Diagrams.Route(path, root) switch
        {
            DiagramRouted { RegistrationPath: { } registration, BodyPath: { } body } when _designers.ClaimantOf(body) is { } claimant =>
                new ToolUnreadable(registration, ClaimedBody(body, claimant)),
            DiagramRouted diagram => diagram,
            DiagramUnreadable unreadable => new ToolUnreadable(unreadable.Path, unreadable.Reason),
            _ => RouteBeyondDiagrams(path, root),
        };
    }

    /// <summary>What serves a path no diagram type routes: a designer, else an editor.</summary>
    private ToolRouting RouteBeyondDiagrams(string path, string? root) =>
        _designers.Route(path, root) switch
        {
            DesignerRouted { RegistrationPath: { } registration, BodyPath: { } body } designer
                when _designers.ClaimantOf(body) is { } claimant && claimant != designer.Definition =>
                new ToolUnreadable(registration, ClaimedBody(body, claimant)),
            DesignerRouted designer => designer,
            DesignerUnreadable unreadable => new ToolUnreadable(unreadable.Path, unreadable.Reason),
            _ => Editors.Resolve(path) as EditorRouted ?? (ToolRouting)new NoTool(),
        };

    /// <summary>Why a registration of one tool is not followed to a body that another's envelope claims.</summary>
    private static string ClaimedBody(string body, DesignerDefinition claimant) =>
        $"Its body, {IoPath.GetFileName(body)}, is a {claimant.Title} document by the envelope on its first line, and opens there; this registration is not followed.";
}
