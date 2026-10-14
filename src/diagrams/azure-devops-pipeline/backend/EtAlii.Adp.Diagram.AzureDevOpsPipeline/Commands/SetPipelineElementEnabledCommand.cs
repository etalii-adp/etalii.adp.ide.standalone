using EtAlii.Adp.Documents;
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
    protected override EditOutcome Apply(LineDocument body, PipelineModel model, SetPipelineElementEnabledCommand command)
    {
        var location = PipelineEdits.Locate(model, command.ElementId);
        if (Refuses(location, out var refusal))
        {
            return refusal;
        }

        var wasEnabled = !ExecutionOf(location).IsDisabled;
        return new PipelineWriter(body).SetEnabled(location.Target, command.Enabled ? "" : "false")
            ? EditOutcome.UndoneBy(command with { Enabled = wasEnabled })
            : EditOutcome.Unchanged;
    }

    private static PipelineExecution ExecutionOf(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => location.Stage.Execution,
        PipelineElementLocationKind.Job => location.Job!.Execution,
        _ => location.Step!.Execution,
    };
}
