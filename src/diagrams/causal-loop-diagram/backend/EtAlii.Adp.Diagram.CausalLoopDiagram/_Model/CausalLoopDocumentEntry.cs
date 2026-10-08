namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// One loaded <c>.cld</c> file: the document as lines and the model it states. The lines the
/// grammar could not read are the validator's to report, from its own parse.
/// </summary>
/// <remarks>
/// The document is kept alongside the model rather than discarded, because every edit in this
/// module is a splice into those lines. A store that kept only the model would force a writer to
/// reserialize, which is the thing the line-oriented format exists to avoid.
/// </remarks>
/// <param name="Document">The file as lines, each with the ending it arrived with.</param>
/// <param name="Model">What the document states.</param>
/// <param name="Error">Why the file could not be read at all - empty when it was.</param>
public sealed record CausalLoopDocumentEntry(
    CausalLoopDocument Document,
    CausalLoopModel Model,
    string Error)
{
    /// <summary>Whether there is a diagram to draw.</summary>
    public bool IsUsable => Error.Length == 0;

    /// <summary>The entry a file that could not be read at all yields.</summary>
    public static CausalLoopDocumentEntry Unreadable(string error) =>
        new(CausalLoopDocument.Parse(""), CausalLoopModel.Empty, error);
}
