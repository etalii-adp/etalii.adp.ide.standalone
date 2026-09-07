using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Adds a resource skeleton under <c>resources:</c> (Requirement 11.1).</summary>
/// <param name="BodyPath">The databricks.yml.</param>
/// <param name="Kind">The resource kind - <c>jobs</c> or <c>pipelines</c>.</param>
/// <param name="Key">The new resource's key.</param>
public sealed record AddDatabricksResourceCommand(string BodyPath, string Kind, string Key) : ICommand;

/// <summary>Renames the bundle.</summary>
public sealed record SetDatabricksBundleNameCommand(string BodyPath, string Name) : ICommand;

/// <inheritdoc cref="AddDatabricksResourceCommand" />
public sealed class AddDatabricksResourceCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<AddDatabricksResourceCommand>
{
    public Task<CommandResult> ExecuteAsync(AddDatabricksResourceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            BundleWriter.AddResourceSkeleton(entry.Document, entry.Bundle, command.Kind, command.Key));
    }
}

/// <inheritdoc cref="SetDatabricksBundleNameCommand" />
public sealed class SetDatabricksBundleNameCommandHandler(IDatabricksDocumentStore documents)
    : ICommandHandler<SetDatabricksBundleNameCommand>
{
    public Task<CommandResult> ExecuteAsync(SetDatabricksBundleNameCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return DatabricksEdits.Run(documents, command.BodyPath, command, entry =>
            BundleWriter.SetName(entry.Document, entry.Bundle, command.Name));
    }
}
