namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Which level a job's pool came from, after the schema's inheritance has been applied
/// (Requirement 4.7).
/// </summary>
/// <remarks>
/// Kept rather than discarded because "ubuntu-latest, inherited from the pipeline" and
/// "ubuntu-latest, set on this job" are different facts about the same value, and a reader
/// changing where a job runs needs to know which file line to go and edit.
/// </remarks>
public enum PipelinePoolOrigin
{
    /// <summary>No <c>pool</c> declared anywhere above this element.</summary>
    None,

    /// <summary>Declared at the top of the file.</summary>
    Pipeline,

    /// <summary>Declared on the owning stage.</summary>
    Stage,

    /// <summary>Declared on the job itself, which beats both of the others.</summary>
    Job,
}
