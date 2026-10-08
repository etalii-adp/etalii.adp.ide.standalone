using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>The text of a new, empty <c>.supply</c> document.</summary>
/// <remarks>
/// <para>
/// The header and the three sections written flow-empty - <c>groups: []</c> and so on - because a
/// bare <c>nodes:</c> parses as a null rather than an empty sequence. The first added entry opens the
/// flow form into a block one, which <c>LineSplice.InsertionPointFor</c> does.
/// </para>
/// <para>
/// <b>It is also the module's <see cref="IDiagramDocumentFactory"/>, and the host will not start
/// without one</b>, because a type that declares an extension must be creatable from the Add dialog.
/// </para>
/// </remarks>
public sealed class SupplyChainDocumentFactory : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.SupplyChain.Origin;

    /// <inheritdoc />
    /// <remarks>CRLF, the repository's house style, because a new file has no style of its own to keep.</remarks>
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return EmptyDocument("\r\n");
    }

    /// <summary>The document text, with the ending the caller asks for.</summary>
    public static string EmptyDocument(string lineEnding)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);

        return string.Join(
            lineEnding,
            $"{SupplyChainParser.HeaderKey}: {SupplyChainModel.CurrentVersion}",
            $"{SupplyChainParser.GroupsKey}: []",
            $"{SupplyChainParser.NodesKey}: []",
            $"{SupplyChainParser.FlowsKey}: []") + lineEnding;
    }
}
