using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Adds a library entry to a pipeline (Requirement 11.2).</summary>
/// <param name="BodyPath">The pipeline's file - settings JSON or a bundle's YAML.</param>
/// <param name="PipelineKey">Which pipeline; empty means the file's first.</param>
/// <param name="Kind">notebook, file or glob.</param>
/// <param name="Path">The path or include pattern.</param>
public sealed record InsertDatabricksLibraryCommand(
    string BodyPath, string PipelineKey, string Kind, string Path) : IDocumentEditCommand;

/// <summary>Removes one library entry; the last one is refused - the schema requires it.</summary>
public sealed record RemoveDatabricksLibraryCommand(
    string BodyPath, string PipelineKey, string Kind, string Path) : IDocumentEditCommand;

/// <summary>Rewrites one of the pipeline's own scalars - name, catalog, schema, channel.</summary>
public sealed record SetDatabricksPipelineScalarCommand(
    string BodyPath, string PipelineKey, string Key, string Value) : IDocumentEditCommand;

/// <inheritdoc cref="InsertDatabricksLibraryCommand" />
public sealed class InsertDatabricksLibraryCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<InsertDatabricksLibraryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, InsertDatabricksLibraryCommand command) => Outcome(
        PipelineOf(model, command.PipelineKey) is { } pipeline
            ? PipelineWriter.InsertLibrary(body, pipeline, command.Kind, command.Path)
            : "There is no pipeline in this file any more.");
}

/// <inheritdoc cref="RemoveDatabricksLibraryCommand" />
public sealed class RemoveDatabricksLibraryCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<RemoveDatabricksLibraryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, RemoveDatabricksLibraryCommand command) => Outcome(
        PipelineOf(model, command.PipelineKey) is { } pipeline
            ? PipelineWriter.RemoveLibrary(body, pipeline, command.Kind, command.Path)
            : "There is no pipeline in this file any more.");
}

/// <inheritdoc cref="SetDatabricksPipelineScalarCommand" />
public sealed class SetDatabricksPipelineScalarCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksPipelineScalarCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksPipelineScalarCommand command) => Outcome(
        PipelineOf(model, command.PipelineKey) is { } pipeline
            ? PipelineWriter.SetScalar(body, pipeline, command.Key, command.Value)
            : "There is no pipeline in this file any more.");
}
