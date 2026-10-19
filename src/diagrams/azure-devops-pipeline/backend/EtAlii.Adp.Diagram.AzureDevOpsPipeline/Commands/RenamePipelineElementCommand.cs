using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

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
/// <param name="Root">The project, which the store needs to resolve templates.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="ElementId">The element to rename.</param>
/// <param name="DisplayName">Its new display name; empty removes it.</param>
public sealed record RenamePipelineElementCommand(
    ProjectRoot Root,
    string BodyPath,
    string ElementId,
    string DisplayName) : IPipelineEditCommand;

internal sealed class RenamePipelineElementCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<RenamePipelineElementCommand>(documents)
{
    /// <remarks>
    /// The definition's own row commits it (<see cref="FormCommit.Row"/>): its read-only reasons refuse an element
    /// that is implicit or from a template, and an empty name unsets the key.
    /// </remarks>
    protected override EditOutcome Apply(PipelineBody body, RenamePipelineElementCommand command) =>
        PipelineDefinition.ElementOf(body, command.ElementId) is { } element
            ? PipelineDefinition.Apply(body, FormCommit.Row(PipelineDefinition.Specification, element, DisplayName, command.DisplayName, new DislEnv()))
            : EditOutcome.Refused(PipelineDefinition.Gone);

    /// <summary>The form row, and the attribute, a rename writes.</summary>
    private const string DisplayName = "displayName";
}
