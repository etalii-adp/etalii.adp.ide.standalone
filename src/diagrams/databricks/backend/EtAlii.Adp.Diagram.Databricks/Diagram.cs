using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The Databricks family's three diagram types, cataloged in docs/tools.md as
/// <c>databricks/bundle</c>, <c>databricks/job</c> and <c>databricks/pipeline</c>.
/// </summary>
/// <remarks>
/// One class for all three, because one engine serves all three - the C4 precedent: they share
/// a store, the parsers and the layout discipline, and differ only in which reading of a
/// configuration file each draws. Every extension here is shared: <c>.yml</c> and <c>.json</c>
/// belong to the whole world, so none of these types ever claims a bare file on sight - a file
/// becomes one of these diagrams when the user registers it (Requirement 1.2).
/// </remarks>
public static class Diagram
{
    /// <summary>The bundle: what a databricks.yml deploys, where, and with what overrides.</summary>
    public static DiagramDefinition Bundle { get; } = new(
        ServiceCollectionAddDatabricksExtension.BundleOrigin,
        "Databricks bundle",
        "What an asset bundle deploys: its resources, its variables, and the targets each lands on.",
        Icon: "mdi-package-variant-closed",
        Extension: ".yml",
        SharedExtension: true,
        // Three types over one shared engine; the services register once.
        Build: builder => builder.Services.AddDatabricks());

    /// <summary>The job: a task DAG with conditions, outcomes and cluster bindings.</summary>
    public static DiagramDefinition Job { get; } = new(
        ServiceCollectionAddDatabricksExtension.JobOrigin,
        "Databricks job",
        "A job's tasks and what each waits for - conditions, outcomes and the compute they run on.",
        Icon: "mdi-transit-connection-horizontal",
        Extension: ".yml",
        SharedExtension: true);

    /// <summary>The declarative pipeline: sources through transformation to a catalog target.</summary>
    public static DiagramDefinition Pipeline { get; } = new(
        ServiceCollectionAddDatabricksExtension.PipelineOrigin,
        "Databricks pipeline",
        "A declarative pipeline's flow: source libraries through the pipeline into its catalog target.",
        Icon: "mdi-pipe",
        Extension: ".json",
        SharedExtension: true);

    /// <summary>What discovery reads: the bundle first, then what it contains.</summary>
    public static DiagramDefinition[] Definitions { get; } =
    [
        Bundle,
        Job,
        Pipeline,
    ];
}
