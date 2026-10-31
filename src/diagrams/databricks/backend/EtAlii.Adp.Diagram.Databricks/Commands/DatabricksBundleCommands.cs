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
    /// <remarks>
    /// The resource is a new member of <c>resources.jobs</c> or <c>resources.pipelines</c>, named by its key, planned by
    /// the binding's insert (FBL 0.14 section 6.2) with its name and a skeleton complete enough to open. The module's own
    /// sentences are answered first, before anything is planned (Requirement 6.4).
    /// </remarks>
    protected override EditOutcome Apply(LineDocument body, DatabricksModel model, AddDatabricksResourceCommand command)
    {
        if (command.Key.Length == 0)
        {
            return EditOutcome.Refused("A resource needs a key.");
        }

        if (command.Kind is not ("jobs" or "pipelines"))
        {
            return EditOutcome.Refused($"There is no '{command.Kind}' resource kind to add.");
        }

        if (model.Bundle.Resources.Any(resource => resource.Kind == command.Kind && resource.Key == command.Key))
        {
            return EditOutcome.Refused($"A {command.Kind} resource named '{command.Key}' is already there.");
        }

        return DatabricksEdits.Bundle(body, _ => new ModelChange.Add(
            "Resource",
            null,
            DatabricksEdits.Attributes(("resourceKind", command.Kind), ("key", command.Key), ("name", command.Key))));
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
