using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Planning;

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
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, AddDatabricksResourceCommand command)
    {
        // TODO(fork): FBL cannot add a mapping entry keyed by a capture; pending the ruling.
        var refusal = BundleWriter.AddResourceSkeleton(body, model.Bundle, command.Kind, command.Key);
        return refusal.Length > 0 ? EditOutcome.Refused(refusal) : EditOutcome.Applied;
    }
}

/// <inheritdoc cref="SetDatabricksBundleNameCommand" />
public sealed class SetDatabricksBundleNameCommandHandler(IDatabricksDocumentStore documents)
    : DatabricksEditHandler<SetDatabricksBundleNameCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, SetDatabricksBundleNameCommand command)
    {
        if (command.Name.Length == 0)
        {
            return EditOutcome.Refused("A bundle needs a name.");
        }

        if (model.Bundle.Reading is not { } reading || DatabricksEdits.OneOf(reading, "Bundle") is null)
        {
            return EditOutcome.Refused("The file has no bundle: section to name.");
        }

        return DatabricksEdits.Bundle(body, read => new ModelChange.Set(DatabricksEdits.OneOf(read, "Bundle")!, DatabricksEdits.Attributes(("name", command.Name))));
    }
}
