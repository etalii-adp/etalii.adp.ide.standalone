namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// One step, as declared.
/// </summary>
/// <param name="Id">Stable within a document: the owning job's id and the step's position in it.</param>
/// <param name="Kind">The key the step leads with.</param>
/// <param name="DisplayName">Its <c>displayName</c>, where it has one.</param>
/// <param name="Identifier">
/// The value of the key naming the kind - a task's name, a script's text, a template's path. It is
/// what <see cref="Label"/> falls back to, per Requirement 4.4.
/// </param>
/// <param name="Execution">What decides whether and how it runs (Requirement 4.5).</param>
/// <param name="Hook">
/// For a step inside a deployment job, the lifecycle hook it belongs to (<c>deploy</c>,
/// <c>preDeploy</c>, ...); empty for a step declared directly under a plain job.
/// </param>
/// <param name="Template">
/// The template that contributed this element, as a workspace-relative path; empty when the file
/// being viewed declares it itself. An element from a template is not editable through the
/// diagram, because its text lives in another file (Requirement 5.4).
/// </param>
/// <param name="Lines">The lines declaring it, in whichever file that is.</param>
public sealed record PipelineStep(
    string Id,
    PipelineStepKind Kind,
    string DisplayName,
    string Identifier,
    PipelineExecution Execution,
    string Hook,
    string Template,
    PipelineLineRange Lines)
{
    /// <summary>Whether this element came from a template, and so may not be edited here.</summary>
    public bool IsFromTemplate => Template.Length > 0;

    /// <summary>
    /// What to show for this step: its <c>displayName</c>, or failing that the identifying value
    /// of its kind, reduced to its first line so a multi-line script does not become the label
    /// (Requirement 4.4).
    /// </summary>
    public string Label
    {
        get
        {
            if (DisplayName.Length > 0)
            {
                return DisplayName;
            }

            var firstLine = Identifier.Split('\n')[0].Trim();
            return firstLine.Length > 0 ? firstLine : Kind.ToString();
        }
    }
}
