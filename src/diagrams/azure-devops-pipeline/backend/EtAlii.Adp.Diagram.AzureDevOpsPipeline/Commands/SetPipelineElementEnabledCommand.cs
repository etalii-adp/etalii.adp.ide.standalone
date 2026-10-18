using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Switches an element on or off.
/// </summary>
/// <remarks>
/// On is written by removing the key rather than by writing <c>enabled: true</c>. The schema's
/// default is enabled, so the explicit form says nothing the file did not already say - and a
/// pipeline that accumulated an <c>enabled: true</c> on everything anyone had ever toggled would
/// be noisier for it.
/// </remarks>
/// <param name="RootPath">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="ElementId">The element to switch.</param>
/// <param name="Enabled">Whether it should run.</param>
public sealed record SetPipelineElementEnabledCommand(
    string RootPath,
    string BodyPath,
    string ElementId,
    bool Enabled) : IPipelineEditCommand;

internal sealed class SetPipelineElementEnabledCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<SetPipelineElementEnabledCommand>(documents)
{
    /// <remarks>
    /// The definition's <c>toggleEnabled</c>, run where the element is not already as asked: it unsets
    /// <c>enabled</c> on an element that is disabled and writes <c>false</c> on one that is not. An element already
    /// as asked is left as it is, so nothing is written and nothing lands on the history.
    /// </remarks>
    protected override EditOutcome Apply(PipelineBody body, SetPipelineElementEnabledCommand command)
    {
        if (PipelineDefinition.ElementOf(body, command.ElementId) is not { } element)
        {
            return EditOutcome.Refused(PipelineDefinition.Gone);
        }

        if (PipelineDefinition.ChangeRefusal(element, Enabled, command.Enabled ? null : "false") is { } refused)
        {
            return EditOutcome.Refused(refused);
        }

        return (element.ValueOf("isDisabled") is true) != command.Enabled
            ? EditOutcome.Unchanged
            : PipelineDefinition.Apply(body, PipelineDefinition.Run("toggleEnabled", element.Diagram, element));
    }

    private const string Enabled = "enabled";
}
