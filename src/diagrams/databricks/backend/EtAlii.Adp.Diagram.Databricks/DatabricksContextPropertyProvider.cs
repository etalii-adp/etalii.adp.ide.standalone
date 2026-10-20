using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The property rows of a selected Databricks element, contributed as data the panel renders
/// without understanding (databricks-diagrams Requirement 10).
/// </summary>
/// <remarks>
/// Two read-only rows appear on every selection by design: the <c>Workspace connection</c>
/// placeholder that mocks where a real connection would surface (Requirement 10.4), and the
/// last simulated run, which only ever changes client-side because simulations never reach the
/// backend (Requirement 8). Every write goes through the standard SetProperty path and the
/// project's history - never written by the panel (Requirement 10.5).
/// <para>
/// <b>The rows are the definitions'</b> (databricks-disl-fbl Requirement 3.1): <see cref="DatabricksDefinition.Rows"/>
/// derives them, their read-only reasons and the two rows every selection wears from the forms of the three bundled
/// definitions, with the ids of their <c>x-databricks</c> blocks. What a set does stays here.
/// </para>
/// </remarks>
public sealed class DatabricksContextPropertyProvider : DiagramPropertyProvider<LineDocument, DatabricksModel>
{
    /// <summary>A task's key, read-only here: renaming rewrites references, so the menu owns it.</summary>
    public const string TaskKeyProperty = "databricks.task-key";

    /// <summary>A task's type, read-only: the type is which *_task mapping the file carries.</summary>
    public const string TaskTypeProperty = "databricks.task-type";

    /// <summary>A task's run_if, editable; empty means the default ALL_SUCCESS.</summary>
    public const string RunIfProperty = "databricks.run-if";

    /// <summary>A task's cluster binding, editable; empty means serverless.</summary>
    private const string ClusterProperty = "databricks.cluster";

    /// <summary>The bundle's name, editable.</summary>
    private const string BundleNameProperty = "databricks.bundle-name";

    /// <summary>A pipeline scalar, editable: <c>databricks.pipeline:{key}</c>.</summary>
    public const string PipelineScalarPrefix = "databricks.pipeline:";

    /// <summary>The mocked workspace connection, read-only on every selection (Requirement 10.4).</summary>
    public const string WorkspaceProperty = "databricks.workspace";

    /// <summary>The last simulated run, read-only; the client narrates it (Requirement 8).</summary>
    public const string LastSimulatedRunProperty = "databricks.last-simulated-run";

    public DatabricksContextPropertyProvider(IHistoryStackStore historyStacks, IDatabricksDocumentStore documents)
        : base(historyStacks, documents)
    {
    }

    /// <inheritdoc />
    protected override bool IsBody(string path) => DatabricksSelection.CouldBeFamilyFile(path);

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the form of the reading that holds the target (<see cref="DatabricksDefinition.Rows"/>), with its
    /// read-only reasons, of the resource the target's registration names (finding B3).
    /// </remarks>
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(ContextTarget target, DocumentEntry<LineDocument, DatabricksModel> entry) =>
        DatabricksDefinition.Rows(target, entry);

    /// <inheritdoc />
    /// <remarks>Through the project's history and out through the delta stream (Requirement 10.5).</remarks>
    protected override DiagramPropertyChange Change(ContextTarget target, DatabricksModel model, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        var body = target.ResolvedFullPath;
        var reading = DatabricksSelection.ReadingOf(model, target);
        ICommand? command;
        if (propertyId.StartsWith(PipelineScalarPrefix, StringComparison.Ordinal)
            && DatabricksSelection.IsPipelineNode(target.ElementId))
        {
            command = reading.Pipelines.FirstOrDefault() is { } pipeline
                ? new SetDatabricksPipelineScalarCommand(body, pipeline.Key, propertyId[PipelineScalarPrefix.Length..], trimmed)
                : null;
        }
        else
        {
            var task = DatabricksSelection.TaskOf(reading, target.ElementId);
            command = propertyId switch
            {
                RunIfProperty when task is { } selected =>
                    new SetDatabricksRunIfCommand(body, selected.Job.Key, selected.Task.Key, trimmed),
                ClusterProperty when task is { } selected =>
                    new SetDatabricksClusterCommand(body, selected.Job.Key, selected.Task.Key, trimmed),
                BundleNameProperty when DatabricksSelection.IsBundle(target.ElementId) =>
                    new SetDatabricksBundleNameCommand(body, trimmed),
                _ => null,
            };
        }

        return command is null
            ? DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.")
            : DiagramPropertyChange.By(command);
    }
}
