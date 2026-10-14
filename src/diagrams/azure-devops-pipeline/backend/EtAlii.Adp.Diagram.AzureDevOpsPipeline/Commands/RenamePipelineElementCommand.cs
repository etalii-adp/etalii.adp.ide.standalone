using EtAlii.Adp.Documents;
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
    string DisplayName) : IPipelineEditCommand;

internal sealed class RenamePipelineElementCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<RenamePipelineElementCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, PipelineModel model, RenamePipelineElementCommand command)
    {
        var location = PipelineEdits.Locate(model, command.ElementId);
        if (Refuses(location, out var refusal))
        {
            return refusal;
        }

        // The old value, captured before the write, is the whole of the inverse.
        var previous = DisplayNameOf(location);
        return new PipelineWriter(body).SetDisplayName(location.Target, command.DisplayName)
            ? EditOutcome.UndoneBy(command with { DisplayName = previous })
            : EditOutcome.Unchanged;
    }

    private static string DisplayNameOf(PipelineElementLocation location) => location.Kind switch
    {
        PipelineElementLocationKind.Stage => location.Stage.DisplayName,
        PipelineElementLocationKind.Job => location.Job!.DisplayName,
        _ => location.Step!.DisplayName,
    };
}
