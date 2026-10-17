namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

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
    public ToolOrigin Origin { get; } = Diagram.Pipeline.Origin;

    /// <inheritdoc />
    /// <remarks>Derived from the bundled definition's toolbox, mapped onto today's ids by <c>x-azure-pipeline</c>.</remarks>
    public IReadOnlyList<ToolboxItemDefinition> Items => PipelineDefinition.Toolbox;
}
