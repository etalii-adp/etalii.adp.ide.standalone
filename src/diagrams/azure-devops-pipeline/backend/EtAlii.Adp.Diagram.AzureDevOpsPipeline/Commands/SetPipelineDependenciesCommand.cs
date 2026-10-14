using EtAlii.Adp.Documents;
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
    bool Declared) : IPipelineEditCommand;

internal sealed class SetPipelineDependenciesCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<SetPipelineDependenciesCommand>(documents)
{
    protected override EditOutcome Apply(LineDocument body, PipelineModel model, SetPipelineDependenciesCommand command)
    {
        var location = PipelineEdits.Locate(model, command.ElementId);
        if (location?.Kind == PipelineElementLocationKind.Step)
        {
            // Steps are a sequence, not a graph - they run in the order they are written and have
            // no dependsOn to set.
            return EditOutcome.Refused("Steps run in order, so a step has nothing to wait for.");
        }

        if (Refuses(location, out var refusal))
        {
            return refusal;
        }

        if (Unknown(model, location, command.DependsOn) is { Length: > 0 } unknown)
        {
            return EditOutcome.Refused($"This pipeline has nothing called '{unknown}' to wait for.");
        }

        if (PipelineEdits.WouldCycle(model, command.ElementId, command.DependsOn))
        {
            return EditOutcome.Refused($"That would make {location.Label} wait for something that is already waiting for it.");
        }

        if (location.Kind == PipelineElementLocationKind.Stage &&
            command.Declared &&
            PipelineEdits.WouldLeaveNoStartingStage(model, command.ElementId, command.DependsOn))
        {
            return EditOutcome.Refused(
                "A pipeline must contain at least one stage with no dependencies, or it has nothing to start with.");
        }

        (IReadOnlyList<string> previous, bool wasDeclared) = Current(location);
        var writer = new PipelineWriter(body);
        var changed = command.Declared
            ? writer.SetDependsOn(location.Target, command.DependsOn)
            : writer.ClearDependsOn(location.Target);

        return changed
            ? EditOutcome.UndoneBy(command with { DependsOn = previous, Declared = wasDeclared })
            : EditOutcome.Unchanged;
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
