using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Where an element runs, as declared - and, once inheritance has been applied, which level
/// declared it.
/// </summary>
/// <remarks>
/// <c>pool</c> takes either a bare name (<c>pool: ubuntu-latest</c>, which names an agent pool) or
/// a mapping with <c>name</c>, <c>vmImage</c> and <c>demands</c>. Both shapes land here, and which
/// one the file used is recoverable from which fields are filled.
/// </remarks>
/// <param name="Name">Its <c>name</c>, or the bare scalar form.</param>
/// <param name="VmImage">Its <c>vmImage</c>, for a Microsoft-hosted pool.</param>
/// <param name="Demands">Its <c>demands</c>, in order.</param>
/// <param name="Origin">Which level this pool was declared at.</param>
/// <param name="Lines">The lines declaring it, at whichever level that was.</param>
public sealed record PipelinePool(
    string Name,
    string VmImage,
    IReadOnlyList<string> Demands,
    PipelinePoolOrigin Origin,
    LineRange Lines)
{
    /// <summary>No <c>pool</c> declared at this level or any above it.</summary>
    public static PipelinePool None { get; } =
        new("", "", [], PipelinePoolOrigin.None, new LineRange(0, 0));

    /// <summary>Whether a pool was declared at all.</summary>
    public bool IsDeclared => Origin != PipelinePoolOrigin.None;

    /// <summary>What to show: the image where there is one, since that is what identifies a hosted agent.</summary>
    public string Label => VmImage.Length > 0 ? VmImage : Name;

    /// <summary>
    /// Whether this pool reached the element by inheritance rather than being declared on it -
    /// which is what tells a reader that editing it means editing a different element.
    /// </summary>
    public bool IsInheritedByAJob => Origin is PipelinePoolOrigin.Pipeline or PipelinePoolOrigin.Stage;
}
