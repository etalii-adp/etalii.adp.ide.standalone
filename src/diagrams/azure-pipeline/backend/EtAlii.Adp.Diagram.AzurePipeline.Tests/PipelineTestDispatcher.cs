

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// Dispatches this module's commands to their handlers, without a container.
/// </summary>
/// <remarks>
/// A test that goes through the real history stack needs a dispatcher, and the production one
/// resolves handlers from DI. Listing them here instead keeps the test to this module - and the
/// list is short precisely because the editable set is deliberately narrow (Requirement 9.1),
/// so a command with no entry here is a command somebody added without noticing that.
/// </remarks>
internal sealed class PipelineTestDispatcher(IPipelineDocumentStore documents) : ICommandDispatcher
{
    public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) => command switch
    {
        RenamePipelineElementCommand rename =>
            new RenamePipelineElementCommandHandler(documents).ExecuteAsync(rename, cancellationToken),
        SetPipelineDependenciesCommand dependencies =>
            new SetPipelineDependenciesCommandHandler(documents).ExecuteAsync(dependencies, cancellationToken),
        SetPipelineElementEnabledCommand enabled =>
            new SetPipelineElementEnabledCommandHandler(documents).ExecuteAsync(enabled, cancellationToken),
        AddPipelineElementCommand add =>
            new AddPipelineElementCommandHandler(documents).ExecuteAsync(add, cancellationToken),
        RemovePipelineElementCommand remove =>
            new RemovePipelineElementCommandHandler(documents).ExecuteAsync(remove, cancellationToken),
        RestorePipelineLinesCommand restore =>
            new RestorePipelineLinesCommandHandler(documents).ExecuteAsync(restore, cancellationToken),
        MovePipelineStepCommand move =>
            new MovePipelineStepCommandHandler(documents).ExecuteAsync(move, cancellationToken),
        _ => throw new InvalidOperationException($"No handler is registered for {command.GetType().Name}."),
    };
}
