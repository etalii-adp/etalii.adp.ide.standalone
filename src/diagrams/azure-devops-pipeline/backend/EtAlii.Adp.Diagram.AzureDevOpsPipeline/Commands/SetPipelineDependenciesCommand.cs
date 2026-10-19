using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

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
/// <param name="Root">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="ElementId">The element whose dependencies these are.</param>
/// <param name="DependsOn">The names it waits for, in order.</param>
/// <param name="Declared">
/// Whether the key should be present at all. False writes no <c>dependsOn</c>, putting the element
/// back on the schema's default; true with an empty list writes <c>dependsOn: []</c>, which is the
/// opposite instruction and has to stay tellable from it.
/// </param>
public sealed record SetPipelineDependenciesCommand(
    ProjectRoot Root,
    string BodyPath,
    string ElementId,
    IReadOnlyList<string> DependsOn,
    bool Declared) : IPipelineEditCommand;

internal sealed class SetPipelineDependenciesCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<SetPipelineDependenciesCommand>(documents)
{
    /// <remarks>
    /// <para>
    /// <b>Refused by the definition's change rules before anything is planned</b>: a step has nothing to wait for, a
    /// name must name a stage, or a sibling job, an edit must not close a cycle (over <c>reachable</c> and
    /// <c>inCycle</c>), and a pipeline must keep a stage that waits for nothing.
    /// </para>
    /// <para>
    /// <b>Undeclared is the definition's <c>clearDependencies</c></b>, which unsets the key. A declared list is the
    /// value the gesture asks for, written as the dependsOn row would write it, which the definition's row cannot
    /// state for its two special entries, "(nothing)" and "(the default order)".
    /// </para>
    /// </remarks>
    protected override EditOutcome Apply(PipelineBody body, SetPipelineDependenciesCommand command)
    {
        if (PipelineDefinition.ElementOf(body, command.ElementId) is not { } element)
        {
            return EditOutcome.Refused(PipelineDefinition.Gone);
        }

        List<object?>? names = command.Declared ? [.. command.DependsOn] : null;
        if (PipelineDefinition.ChangeRefusal(element, DependsOn, names) is { } refused)
        {
            return EditOutcome.Refused(refused);
        }

        return PipelineDefinition.Apply(body, names is null
            ? PipelineDefinition.Run("clearDependencies", element.Diagram, element)
            : new DislTransaction([new DislChange.Set(element.Id, element.Type.Name, new Dictionary<string, object?>(StringComparer.Ordinal) { [DependsOn] = names })], [], null));
    }

    private const string DependsOn = "dependsOn";
}
