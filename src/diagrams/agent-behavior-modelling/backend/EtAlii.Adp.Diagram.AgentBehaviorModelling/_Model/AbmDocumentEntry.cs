using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// One loaded Markdown document: its lines, and the model read from them.
/// </summary>
/// <param name="Document">The document's own lines, which every edit splices.</param>
/// <param name="Model">What <see cref="AbmParser"/> read from them.</param>
/// <param name="Unreadable">
/// Empty for a document that was read; otherwise why its body could not be read.
/// </param>
/// <remarks>
/// <para>
/// <b>An unreadable body is not the same as a missing one, and this is where that is recorded.</b>
/// A missing body is a new diagram and opens empty, like any first open. An unreadable one also
/// opens empty, because the parser never throws - but its emptiness is not the document. The file
/// on disk is the only copy, and something could not read it for a moment. Causal loop measured
/// what happens when the two are confused: one unreadable read, then a save, and a real diagram on
/// disk was replaced by an empty one. So an unreadable entry is kept apart here, and the save that
/// every command makes refuses to write one.
/// </para>
/// <para>
/// <b>There is no parse error field</b>, unlike the stores this mirrors. <see cref="AbmParser"/>
/// never throws: a malformed entry is passed over and reported in <see cref="AbmModel.Problems"/>,
/// so a document that reads always has a model.
/// </para>
/// </remarks>
public sealed record AbmDocumentEntry(LineDocument Document, AbmModel Model, string Unreadable = "")
{
    /// <summary>Whether this entry holds the document's content, rather than a stand-in for one that could not be read.</summary>
    public bool IsUsable => Unreadable.Length == 0;

    /// <summary>The entry for a body's text: its lines, and the model read from them.</summary>
    public static AbmDocumentEntry Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var document = LineDocument.Parse(text);
        return new AbmDocumentEntry(document, AbmParser.Parse(document));
    }

    /// <summary>
    /// The entry for a body that is not there or could not be read, as the shared lifecycle asks
    /// for one (<see cref="DocumentLifecycle{TDocument}"/>'s <c>unavailable</c>).
    /// </summary>
    /// <remarks>
    /// A missing body is a new, empty diagram. An unreadable one is empty too, but it carries the
    /// reason, which is what marks it as not a document to write.
    /// </remarks>
    public static AbmDocumentEntry Unavailable(DocumentUnavailability unavailability, string reason) =>
        unavailability == DocumentUnavailability.Missing
            ? Read("")
            : Read("") with { Unreadable = reason.Length > 0 ? reason : "the body could not be read" };
}
