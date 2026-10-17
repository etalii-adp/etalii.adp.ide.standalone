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
/// </remarks>
public sealed class DatabricksContextPropertyProvider : DiagramPropertyProvider<LineDocument, DatabricksModel>
{
    /// <summary>A task's key, read-only here: renaming rewrites references, so the menu owns it.</summary>
    public const string TaskKeyProperty = "databricks.task-key";

    /// <summary>A task's type, read-only: the type is which *_task mapping the file carries.</summary>
    public const string TaskTypeProperty = "databricks.task-type";

    /// <summary>A task's principal source, read-only in the grid.</summary>
    private const string TaskSourceProperty = "databricks.task-source";

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

    private const string IdentityGroup = "Identity";
    private const string ExecutionGroup = "Execution";
    private const string WorkspaceGroup = "Workspace";
    private const string RenameViaMenu = "Rename through the context menu, so every depends_on reference follows the key.";
    private const string TypeIsTheFile = "The type is which *_task mapping the file carries; change it in the file.";
    private const string WorkspacePlaceholder = "Not connected — a placeholder for the real workspace connection.";
    private const string SimulationIsClientSide = "Simulated runs play on the canvas and never touch the file.";

    public DatabricksContextPropertyProvider(IHistoryStackStore historyStacks, IDatabricksDocumentStore documents)
        : base(historyStacks, documents)
    {
    }

    /// <inheritdoc />
    protected override bool IsBody(string path) => DatabricksSelection.CouldBeFamilyFile(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<LineDocument, DatabricksModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (DatabricksSelection.TaskOf(entry.Model, elementId) is { } selected)
        {
            return
            [
                new ContextPropertyDefinition(TaskKeyProperty, "Key", selected.Task.Key, ReadOnlyReason: RenameViaMenu, Group: IdentityGroup),
                new ContextPropertyDefinition(TaskTypeProperty, "Type", selected.Task.Type, ReadOnlyReason: TypeIsTheFile, Group: IdentityGroup),
                new ContextPropertyDefinition(TaskSourceProperty, "Source", selected.Task.Source, ReadOnlyReason: TypeIsTheFile, Group: IdentityGroup),
                new ContextPropertyDefinition(RunIfProperty, "Run if", selected.Task.RunIf, Group: ExecutionGroup),
                new ContextPropertyDefinition(ClusterProperty, "Cluster", selected.Task.ClusterKey, Group: ExecutionGroup),
                .. Placeholders(),
            ];
        }

        if (DatabricksSelection.IsBundle(elementId))
        {
            return
            [
                new ContextPropertyDefinition(BundleNameProperty, "Name", entry.Model.Bundle.Name, Group: IdentityGroup),
                .. Placeholders(),
            ];
        }

        if (DatabricksSelection.IsPipelineNode(elementId) && entry.Model.Pipelines.FirstOrDefault() is { } pipeline)
        {
            return
            [
                new ContextPropertyDefinition($"{PipelineScalarPrefix}name", "Name", pipeline.Name, Group: IdentityGroup),
                new ContextPropertyDefinition($"{PipelineScalarPrefix}catalog", "Catalog", pipeline.Catalog, Group: IdentityGroup),
                new ContextPropertyDefinition($"{PipelineScalarPrefix}schema", "Schema", pipeline.Schema, Group: IdentityGroup),
                new ContextPropertyDefinition($"{PipelineScalarPrefix}channel", "Channel", pipeline.Channel, Group: ExecutionGroup),
                .. Placeholders(),
            ];
        }

        if (DatabricksSelection.TargetOf(entry.Model, elementId) is { } frame)
        {
            return
            [
                new ContextPropertyDefinition(TaskKeyProperty, "Target", frame.Name, ReadOnlyReason: "The target's key is the file's structure.", Group: IdentityGroup),
                new ContextPropertyDefinition(TaskTypeProperty, "Mode", frame.Mode, ReadOnlyReason: "Change the mode in the file.", Group: IdentityGroup),
                .. Placeholders(),
            ];
        }

        return [];
    }

    /// <inheritdoc />
    /// <remarks>Through the project's history and out through the delta stream (Requirement 10.5).</remarks>
    protected override DiagramPropertyChange Change(ContextTarget target, DatabricksModel model, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        var body = target.ResolvedFullPath;
        ICommand? command;
        if (propertyId.StartsWith(PipelineScalarPrefix, StringComparison.Ordinal)
            && DatabricksSelection.IsPipelineNode(target.ElementId))
        {
            command = model.Pipelines.FirstOrDefault() is { } pipeline
                ? new SetDatabricksPipelineScalarCommand(body, pipeline.Key, propertyId[PipelineScalarPrefix.Length..], trimmed)
                : null;
        }
        else
        {
            var task = DatabricksSelection.TaskOf(model, target.ElementId);
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

    /// <summary>The two rows every selection wears; see the class remarks.</summary>
    private static IEnumerable<ContextPropertyDefinition> Placeholders() =>
    [
        new(WorkspaceProperty, "Workspace connection", "Not connected", ReadOnlyReason: WorkspacePlaceholder, Group: WorkspaceGroup),
        new(LastSimulatedRunProperty, "Last simulated run", "", ReadOnlyReason: SimulationIsClientSide, Group: WorkspaceGroup),
    ];
}
