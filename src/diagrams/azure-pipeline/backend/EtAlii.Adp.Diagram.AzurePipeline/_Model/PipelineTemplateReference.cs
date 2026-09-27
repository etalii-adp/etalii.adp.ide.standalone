using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// A <c>template:</c> or <c>extends:</c> reference, exactly as the file wrote it.
/// </summary>
/// <remarks>
/// Recorded even where this module will never follow it, because Requirement 5.1 and 5.3 both
/// turn on the same point: a diagram that quietly drops a template is worse than one that admits
/// the gap. Whether a reference can be followed, and what it contributes when it can, is decided
/// later by the template resolver - this record is only what the file said.
/// </remarks>
/// <param name="Id">Stable within a document.</param>
/// <param name="OwnerId">The stage or job this reference sits inside; empty at pipeline level.</param>
/// <param name="Slot">Where it appeared, and so what it contributes.</param>
/// <param name="Path">The path part, without any <c>@resource</c> suffix.</param>
/// <param name="Resource">
/// The repository resource named after <c>@</c>, where there is one. A reference into another
/// repository is the commonest reason a template cannot be followed.
/// </param>
/// <param name="ParameterNames">The names under its <c>parameters</c>, in order (Requirement 5.6).</param>
/// <param name="Lines">The lines declaring it.</param>
public sealed record PipelineTemplateReference(
    string Id,
    string OwnerId,
    PipelineTemplateSlot Slot,
    string Path,
    string Resource,
    IReadOnlyList<string> ParameterNames,
    LineRange Lines)
{
    /// <summary>The reference as written, which is what the canvas shows.</summary>
    public string Reference => Resource.Length > 0 ? $"{Path}@{Resource}" : Path;
}
