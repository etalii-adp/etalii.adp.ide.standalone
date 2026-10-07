using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>The text of a new, empty <c>.ghg</c> document (Requirement 2.6).</summary>
/// <remarks>
/// <para>
/// The header line and both sections written flow-empty - <c>trends: []</c> and
/// <c>influences: []</c>. Flow-empty rather than a bare key because a bare <c>trends:</c> with
/// nothing under it parses as a null value rather than an empty sequence, and a reader opening a
/// brand-new file should see what the document is going to hold.
/// </para>
/// <para>
/// <b>The first added entry opens the flow form into a block one</b>, which
/// FBL's yaml family does (FBL §6.2): appending a block item under a key that already carries
/// <c>[]</c> would leave the key with two values and the file unparseable.
/// </para>
/// <para>
/// <b>It is also the module's <see cref="IDiagramDocumentFactory"/>, and the host will not start
/// without one.</b> <c>Program.cs</c> refuses any diagram type that declares a document extension but
/// registers no factory, because such a type cannot be created from the Add dialog. That is a startup
/// check, not a test, so a search of the test projects for what a type must register does not find it.
/// FDG's first build found it when every discovery test failed at once.
/// </para>
/// </remarks>
public sealed class GhgDocumentFactory : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    /// <inheritdoc />
    /// <remarks>
    /// The base name is not written into the document: the schema has no title line, and inventing
    /// one would put a key in every new file that nothing reads. CRLF, because a file ADP creates has
    /// no existing style to preserve, and CRLF is the repository's own house style.
    /// </remarks>
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return EmptyDocument("\r\n");
    }

    /// <summary>The document text, with the platform's line ending.</summary>
    public static string EmptyDocument() => EmptyDocument(Environment.NewLine);

    /// <summary>The document text, with the ending the caller asks for.</summary>
    /// <remarks>
    /// The ending is a parameter because a new document created beside existing ones should match
    /// them, and because a test that pins the bytes needs to say which ending it means rather than
    /// depending on the machine it runs on.
    /// </remarks>
    public static string EmptyDocument(string lineEnding)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);

        return string.Join(
            lineEnding,
            $"gartner-hypecycle-graph: {GhgModel.CurrentVersion}",
            "trends: []",
            "influences: []") + lineEnding;
    }
}
