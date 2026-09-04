using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// One Lakeflow declarative pipeline: its identity, where it writes, how it runs, and the
/// libraries that define its transformations - the flow the pipeline diagram draws (Requirement 5).
/// </summary>
/// <param name="Key">The pipeline's key under <c>resources: pipelines:</c>; empty for a bare settings file.</param>
/// <param name="Name">The pipeline's <c>name:</c>; empty when it has none.</param>
/// <param name="Catalog">The Unity Catalog written to; empty when unspecified.</param>
/// <param name="Schema">The target schema (<c>schema:</c> or the legacy <c>target:</c>); empty when unspecified.</param>
/// <param name="Serverless">Whether the pipeline declares <c>serverless: true</c>.</param>
/// <param name="Continuous">Whether the pipeline declares <c>continuous: true</c>.</param>
/// <param name="Development">Whether the pipeline declares <c>development: true</c>.</param>
/// <param name="Channel">The release <c>channel:</c> as written; empty when unspecified.</param>
/// <param name="Libraries">The <c>libraries:</c> entries, in file order.</param>
/// <param name="Notifications">The <c>notifications:</c> entries.</param>
/// <param name="UnknownNodes">Constructs present but not modelled - drawn generically, never written.</param>
/// <param name="Lines">The lines that declare the pipeline.</param>
public sealed record PipelineModel(
    string Key,
    string Name,
    string Catalog,
    string Schema,
    bool Serverless,
    bool Continuous,
    bool Development,
    string Channel,
    IReadOnlyList<PipelineLibrary> Libraries,
    IReadOnlyList<PipelineNotification> Notifications,
    IReadOnlyList<UnknownNode> UnknownNodes,
    LineRange Lines);
