namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// The pipeline Toolbox: one entry per thing that can be added (Requirement 9.6).
/// </summary>
/// <remarks>
/// <para>
/// Each entry names the context action it drops into, so the drop inherits that action's command,
/// its refusals and its undo without an implementation of its own. That is what "one implementation
/// behind all three triggers" means in practice: dropping a Job on a stage, choosing Add job from
/// its menu, and whatever key is bound to it all end at the same
/// <see cref="AddPipelineElementCommand"/>.
/// </para>
/// <para>
/// It also means the drop cannot go somewhere the menu would not. The action provider offers
/// <see cref="PipelineContextActionProvider.AddStepActionId"/> only on a job, so dropping a Script
/// step on a stage is refused by the same rule that keeps it out of the stage's menu - and the
/// description of each entry says where it goes, so the user finds out before trying.
/// </para>
/// </remarks>
public sealed class PipelineToolboxProvider : IDiagramToolboxProvider
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = Diagram.Definitions[0].Origin;

    /// <inheritdoc />
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new ToolboxItemDefinition(
            "azure-pipeline.toolbox.stage",
            "Stage",
            "mdi-layers-plus",
            "Drop on a stage to add another after it. It arrives with a job and a step, so it runs.",
            PipelineContextActionProvider.AddStageActionId),
        new ToolboxItemDefinition(
            "azure-pipeline.toolbox.job",
            "Job",
            "mdi-plus-box-outline",
            "Drop on a stage to add a job to it.",
            PipelineContextActionProvider.AddJobActionId),
        new ToolboxItemDefinition(
            "azure-pipeline.toolbox.deployment-job",
            "Deployment job",
            "mdi-rocket-launch-outline",
            "Drop on a stage to add a deployment job, with the environment and strategy one needs.",
            PipelineContextActionProvider.AddDeploymentJobActionId),
        new ToolboxItemDefinition(
            "azure-pipeline.toolbox.step",
            "Script step",
            "mdi-console-line",
            "Drop on a job to add a script step to it.",
            PipelineContextActionProvider.AddStepActionId),
    ];
}
