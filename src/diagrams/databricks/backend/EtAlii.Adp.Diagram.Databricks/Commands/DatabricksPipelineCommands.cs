using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Adds a library entry to a pipeline (Requirement 11.2).</summary>
/// <param name="BodyPath">The pipeline's file - settings JSON or a bundle's YAML.</param>
/// <param name="PipelineKey">Which pipeline; empty means the file's first.</param>
/// <param name="Kind">notebook, file or glob.</param>
/// <param name="Path">The path or include pattern.</param>
public sealed record InsertDatabricksLibraryCommand(
    string BodyPath, string PipelineKey, string Kind, string Path) : ICommand;

/// <summary>Removes one library entry; the last one is refused - the schema requires it.</summary>
public sealed record RemoveDatabricksLibraryCommand(
    string BodyPath, string PipelineKey, string Kind, string Path) : ICommand;

/// <summary>Rewrites one of the pipeline's own scalars - name, catalog, schema, channel.</summary>
public sealed record SetDatabricksPipelineScalarCommand(
    string BodyPath, string PipelineKey, string Key, string Value) : ICommand;

/// <inheritdoc cref="InsertDatabricksLibraryCommand" />
public sealed class InsertDatabricksLibraryCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<InsertDatabricksLibraryCommand>
{
    public Task<CommandResult> ExecuteAsync(InsertDatabricksLibraryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.PipelineOf(entry, command.PipelineKey) is { } pipeline
                ? PipelineWriter.InsertLibrary(entry.Document, pipeline, command.Kind, command.Path)
                : "There is no pipeline in this file any more.");
    }
}

/// <inheritdoc cref="RemoveDatabricksLibraryCommand" />
public sealed class RemoveDatabricksLibraryCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<RemoveDatabricksLibraryCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveDatabricksLibraryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.PipelineOf(entry, command.PipelineKey) is { } pipeline
                ? PipelineWriter.RemoveLibrary(entry.Document, pipeline, command.Kind, command.Path)
                : "There is no pipeline in this file any more.");
    }
}

/// <inheritdoc cref="SetDatabricksPipelineScalarCommand" />
public sealed class SetDatabricksPipelineScalarCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<SetDatabricksPipelineScalarCommand>
{
    public Task<CommandResult> ExecuteAsync(SetDatabricksPipelineScalarCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            DatabricksEdits.PipelineOf(entry, command.PipelineKey) is { } pipeline
                ? PipelineWriter.SetScalar(entry.Document, pipeline, command.Key, command.Value)
                : "There is no pipeline in this file any more.");
    }
}
