using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>One loaded <c>.supply</c> document: its lines, and the model read from them.</summary>
/// <param name="Document">The document's own lines, which every edit splices.</param>
/// <param name="Model">What <see cref="SupplyChainParser"/> read from them.</param>
/// <param name="Unreadable">Empty for a document that was read; otherwise why its body could not be read.</param>
/// <remarks>
/// <b>An unreadable body is not a missing one.</b> Both open empty, but the emptiness of an
/// unreadable one is not the document, so a save refuses to write it over the only copy on disk.
/// </remarks>
public sealed record SupplyChainDocumentEntry(LineDocument Document, SupplyChainModel Model, string Unreadable = "")
{
    /// <summary>Whether this entry holds the document's content, rather than a stand-in for one that could not be read.</summary>
    public bool IsUsable => Unreadable.Length == 0;

    /// <summary>The entry for a body's text.</summary>
    public static SupplyChainDocumentEntry Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var document = LineDocument.Parse(text);
        return new SupplyChainDocumentEntry(document, SupplyChainParser.Parse(document));
    }

    /// <summary>The entry for a body that is not there or could not be read.</summary>
    public static SupplyChainDocumentEntry Unavailable(DocumentUnavailability unavailability, string reason) =>
        unavailability == DocumentUnavailability.Missing
            ? Read("")
            : Read("") with { Unreadable = reason.Length > 0 ? reason : "the body could not be read" };
}
