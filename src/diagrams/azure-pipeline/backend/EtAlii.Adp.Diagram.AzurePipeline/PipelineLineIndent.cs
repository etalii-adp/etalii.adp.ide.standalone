using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// The one reading of a <see cref="Line"/> this module needs that core's line does not carry: how
/// deeply it is indented.
/// </summary>
/// <remarks>
/// YAML nesting is expressed in leading spaces, and this module's writer finds a key's extent and
/// a new entry's indentation by comparing them. Counting spaces only, not tabs, is deliberate: YAML
/// forbids tabs in indentation, so a tab is never evidence of depth. It stayed here rather than
/// moving onto <see cref="Line"/> because no other line-splicing module asks the question
/// (backend-centralization Requirement 1.1).
/// </remarks>
internal static class PipelineLineIndent
{
    /// <summary>The number of leading spaces on <paramref name="line"/>.</summary>
    public static int Indent(this Line line) => line.Text.Length - line.Text.TrimStart(' ').Length;
}
