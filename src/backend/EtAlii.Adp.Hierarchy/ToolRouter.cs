namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Which tool serves a path (code-structure FR-058): the one place the precedence between the three
/// tool kinds is decided - diagrams, then designers, then editors. Each kind's router still answers
/// the questions of its own kind; what a caller combining them asks is asked here.
/// </summary>
/// <remarks>
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
    private readonly DiagramFileRouter _diagrams;
    private readonly DesignerFileRouter _designers;
    private readonly EditorResolver _editors;

    public ToolRouter(DiagramFileRouter diagrams, DesignerFileRouter designers, EditorResolver editors)
    {
        ArgumentNullException.ThrowIfNull(diagrams);
        ArgumentNullException.ThrowIfNull(designers);
        ArgumentNullException.ThrowIfNull(editors);

        _diagrams = diagrams;
        _designers = designers;
        _editors = editors;
    }

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

        return _diagrams.Route(path, root) switch
        {
            DiagramRouted diagram => diagram,
            DiagramUnreadable unreadable => new ToolUnreadable(unreadable.Path, unreadable.Reason),
            _ => RouteBeyondDiagrams(path, root),
        };
    }

    /// <summary>What serves a path no diagram type routes: a designer, else an editor.</summary>
    private ToolRouting RouteBeyondDiagrams(string path, string? root) =>
        _designers.Route(path, root) switch
        {
            DesignerRouted designer => designer,
            DesignerUnreadable unreadable => new ToolUnreadable(unreadable.Path, unreadable.Reason),
            _ => _editors.Resolve(path) as EditorRouted ?? (ToolRouting)new NoTool(),
        };
}
