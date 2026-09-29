using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Sets an element's <c>displayName</c> - a rename as a reader of the diagram means it.
/// </summary>
/// <remarks>
/// The <c>displayName</c> rather than the <c>stage:</c> or <c>job:</c> name, deliberately. The
/// name is what <c>dependsOn</c> matches on, so changing it silently breaks every reference to
/// it; the display name is what the canvas shows and nothing depends on. Renaming the identifier
/// is a refactor across a file, which is a text editor's job.
/// </remarks>
/// <param name="RootPath">The project, which the store needs to resolve templates.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="ElementId">The element to rename.</param>
/// <param name="DisplayName">Its new display name; empty removes it.</param>
public sealed record RenamePipelineElementCommand(
    string RootPath,
    string BodyPath,
    string ElementId,
    string DisplayName) : ICommand;

internal sealed class RenamePipelineElementCommandHandler(IPipelineDocumentStore documents)
    : ICommandHandler<RenamePipelineElementCommand>
{
    public Task<CommandResult> ExecuteAsync(RenamePipelineElementCommand command, CancellationToken cancellationToken = default)
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

        // The old value, captured before the write, is the whole of the inverse.
        var previous = DisplayNameOf(location);
        if (!new PipelineWriter(entry.Document).SetDisplayName(location.Target, command.DisplayName))
        {
            // Nothing changed, so there is nothing to undo either.
            return Task.FromResult(CommandResult.Success());
        }

        var saved = documents.Save(command.RootPath, command.BodyPath, entry);
        return Task.FromResult(saved.Failed
            ? CommandResult.Failure(saved.Error)
            : CommandResult.Success(new RenamePipelineElementCommand(command.RootPath, command.BodyPath, command.ElementId, previous)));
    }

    private static string DisplayNameOf(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => location.Stage.DisplayName,
        PipelineElementLocationKind.Job => location.Job!.DisplayName,
        _ => location.Step!.DisplayName,
    };
}
