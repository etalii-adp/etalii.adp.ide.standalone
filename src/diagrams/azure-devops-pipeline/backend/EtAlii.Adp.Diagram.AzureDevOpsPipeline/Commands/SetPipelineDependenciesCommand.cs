using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Sets what an element waits for. Adding and removing a dependency edge are both this.
/// </summary>
/// <remarks>
/// <para>
/// One command rather than two, because the file has one key: drawing an edge and deleting one
/// are both "this element's <c>dependsOn</c> is now that list", and modelling them separately
/// would mean two handlers that had to agree about the same line of YAML.
/// </para>
/// <para>
/// It is also what makes the implicit explicit (Requirement 9.3). A stage relying on the default
/// sequential order has no <c>dependsOn</c> at all; the moment the user draws an edge that
/// contradicts that, the default has to be written down or the file would say something the
/// diagram does not.
/// </para>
/// </remarks>
/// <param name="RootPath">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="ElementId">The element whose dependencies these are.</param>
/// <param name="DependsOn">The names it waits for, in order.</param>
/// <param name="Declared">
/// Whether the key should be present at all. False writes no <c>dependsOn</c>, putting the element
/// back on the schema's default; true with an empty list writes <c>dependsOn: []</c>, which is the
/// opposite instruction and has to stay tellable from it.
/// </param>
public sealed record SetPipelineDependenciesCommand(
    string RootPath,
    string BodyPath,
    string ElementId,
    IReadOnlyList<string> DependsOn,
    bool Declared) : ICommand;

internal sealed class SetPipelineDependenciesCommandHandler(IPipelineDocumentStore documents)
    : ICommandHandler<SetPipelineDependenciesCommand>
{
    public Task<CommandResult> ExecuteAsync(SetPipelineDependenciesCommand command, CancellationToken cancellationToken = default)
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

        if (location.Kind == PipelineElementLocationKind.Step)
        {
            // Steps are a sequence, not a graph - they run in the order they are written and have
            // no dependsOn to set.
            return Task.FromResult(CommandResult.Failure("Steps run in order, so a step has nothing to wait for."));
        }

        if (PipelineEdits.RefusalFor(location) is { Length: > 0 } refusal)
        {
            return Task.FromResult(CommandResult.Failure(refusal));
        }

        if (Unknown(entry.Model, location, command.DependsOn) is { Length: > 0 } unknown)
        {
            return Task.FromResult(CommandResult.Failure($"This pipeline has nothing called '{unknown}' to wait for."));
        }

        if (PipelineEdits.WouldCycle(entry.Model, command.ElementId, command.DependsOn))
        {
            return Task.FromResult(CommandResult.Failure(
                $"That would make {location.Label} wait for something that is already waiting for it."));
        }

        if (location.Kind == PipelineElementLocationKind.Stage &&
            command.Declared &&
            PipelineEdits.WouldLeaveNoStartingStage(entry.Model, command.ElementId, command.DependsOn))
        {
            return Task.FromResult(CommandResult.Failure(
                "A pipeline must contain at least one stage with no dependencies, or it has nothing to start with."));
        }

        var (previous, wasDeclared) = Current(location);
        var writer = new PipelineWriter(entry.Document);
        var changed = command.Declared
            ? writer.SetDependsOn(location.Target, command.DependsOn)
            : writer.ClearDependsOn(location.Target);

        if (!changed)
        {
            return Task.FromResult(CommandResult.Success());
        }

        var saved = documents.Save(command.RootPath, command.BodyPath, entry);
        return Task.FromResult(saved.Failed
            ? CommandResult.Failure(saved.Error)
            : CommandResult.Success(new SetPipelineDependenciesCommand(
                command.RootPath,
                command.BodyPath,
                command.ElementId,
                previous,
                wasDeclared)));
    }

    /// <summary>The first name that matches nothing, or empty when they all do.</summary>
    private static string Unknown(PipelineModel model, PipelineElementLocation location, IReadOnlyList<string> dependsOn)
    {
        var names = PipelineEdits.NamesToIds(model, location);
        return dependsOn.FirstOrDefault(name => !names.ContainsKey(name)) ?? "";
    }

    private static (IReadOnlyList<string> DependsOn, bool Declared) Current(PipelineElementLocation location) =>
        location.Kind == PipelineElementLocationKind.Stage
            ? (location.Stage.DependsOn, location.Stage.DependsOnDeclared)
            : (location.Job!.DependsOn, location.Job.DependsOnDeclared);
}
