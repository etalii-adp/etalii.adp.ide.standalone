using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a connect. The inverse is the disconnection of what was connected.</summary>
public sealed class ConnectDependencyGraphElementsCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<ConnectDependencyGraphElementsCommand>(documents)
{
    /// <inheritdoc />
    protected override CommandResult Execute(ConnectDependencyGraphElementsCommand command) =>
        string.Equals(command.From, command.To, StringComparison.Ordinal)
            // A node depending on itself says nothing about what needs what. Refused with the
            // reason rather than silently dropped.
            ? CommandResult.Failure("A node cannot depend on itself.")
            : base.Execute(command);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, ConnectDependencyGraphElementsCommand command)
    {
        if (DependencyGraphEdits.ElementOf(model, command.From) is null ||
            DependencyGraphEdits.ElementOf(model, command.To) is null)
        {
            // A relation gesture that ends on nothing cancels with a reason, and writing a
            // dependency on a node that is not there would manufacture exactly the dangling
            // reference the validator exists to report.
            return EditOutcome.Refused("Both ends of a dependency must be nodes of this graph.");
        }

        DependencyGraphWriter.InsertRelation(body, model, command.Id, command.From, command.To, command.Label);
        return EditOutcome.Applied;
    }
}
