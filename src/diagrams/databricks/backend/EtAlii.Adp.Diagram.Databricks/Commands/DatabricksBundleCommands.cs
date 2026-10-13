using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>Adds a resource skeleton under <c>resources:</c> (Requirement 11.1).</summary>
/// <param name="BodyPath">The databricks.yml.</param>
/// <param name="Kind">The resource kind - <c>jobs</c> or <c>pipelines</c>.</param>
/// <param name="Key">The new resource's key.</param>
public sealed record AddDatabricksResourceCommand(string BodyPath, string Kind, string Key) : IDocumentEditCommand;

/// <summary>Renames the bundle.</summary>
public sealed record SetDatabricksBundleNameCommand(string BodyPath, string Name) : IDocumentEditCommand;

/// <inheritdoc cref="AddDatabricksResourceCommand" />
public sealed class AddDatabricksResourceCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<AddDatabricksResourceCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, AddDatabricksResourceCommand command) => Outcome(
        BundleWriter.AddResourceSkeleton(body, model.Bundle, command.Kind, command.Key));
}

/// <inheritdoc cref="SetDatabricksBundleNameCommand" />
public sealed class SetDatabricksBundleNameCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksBundleNameCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksBundleNameCommand command) => Outcome(
        BundleWriter.SetName(body, model.Bundle, command.Name));
}
