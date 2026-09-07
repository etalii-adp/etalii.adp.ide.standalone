using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzurePipeline;

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
    bool Enabled) : ICommand;

internal sealed class SetPipelineElementEnabledCommandHandler(IPipelineDocumentStore documents)
    : ICommandHandler<SetPipelineElementEnabledCommand>
{
    public Task<CommandResult> ExecuteAsync(SetPipelineElementEnabledCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.RootPath, command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(entry.Error));
        }

        var location = PipelineEdits.Locate(entry.Model, command.ElementId);
        if (location is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer in this pipeline."));
        }

        if (PipelineEdits.RefusalFor(location) is { Length: > 0 } refusal)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        var wasEnabled = !ExecutionOf(location).IsDisabled;
        if (!new PipelineWriter(entry.Document).SetEnabled(location.Target, command.Enabled ? "" : "false"))
        {
            return Task.FromResult(CommandResult.Success());
        }

        var error = documents.Save(command.RootPath, command.BodyPath);
        return Task.FromResult(error.Length > 0
            ? CommandResult.Failure(error)
            : CommandResult.Success(new SetPipelineElementEnabledCommand(
                command.RootPath,
                command.BodyPath,
                command.ElementId,
                wasEnabled)));
    }

    private static PipelineExecution ExecutionOf(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => location.Stage.Execution,
        PipelineElementLocationKind.Job => location.Job!.Execution,
        _ => location.Step!.Execution,
    };
}
