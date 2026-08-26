namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// What the add command is being asked to create - one entry per thing the toolbox offers
/// (Requirement 9.6).
/// </summary>
public enum PipelineAddKind
{
    /// <summary>A stage, with a job and a step inside it.</summary>
    Stage,

    /// <summary>A plain job, with a step inside it.</summary>
    Job,

    /// <summary>A deployment job, with the environment and strategy the schema requires.</summary>
    DeploymentJob,

    /// <summary>A script step.</summary>
    Step,
}
