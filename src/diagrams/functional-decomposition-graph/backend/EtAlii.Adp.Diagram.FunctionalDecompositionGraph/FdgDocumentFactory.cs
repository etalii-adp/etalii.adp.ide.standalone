using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>The text of a new, empty <c>.fdg</c> document (Requirement 2.5).</summary>
/// <remarks>
/// <para>
/// The header line and both sections written flow-empty - <c>elements: []</c> and
/// <c>connections: []</c>. Flow-empty rather than a bare key because a bare <c>elements:</c> with
/// nothing under it parses as a null value rather than an empty sequence, and a reader opening a
/// brand-new file should see what the document is going to hold.
/// </para>
/// <para>
/// <b>The first added entry opens the flow form into a block one</b>, which
/// <see cref="LineSplice.InsertionPointFor"/> does: appending a block item under a key that
/// already carries <c>[]</c> would leave the key with two values and the file unparseable.
/// </para>
/// </remarks>
public static class FdgDocumentFactory
{
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
            [
                $"functional-decomposition-graph: {FdgModel.CurrentVersion}",
                "elements: []",
                "connections: []",
            ]) + lineEnding;
    }
}
