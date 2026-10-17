using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a connect, as the binding inserts a dependency. The undo is the host's restore.</summary>
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
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, ConnectDependencyGraphElementsCommand command)
    {
        if (DependencyGraphEdits.NodeOf(model, command.From) is null ||
            DependencyGraphEdits.NodeOf(model, command.To) is null)
        {
            // A relation gesture that ends on nothing cancels with a reason, and writing a
            // dependency on a node that is not there would manufacture exactly the dangling
            // reference the validator exists to report.
            return EditOutcome.Refused("Both ends of a dependency must be nodes of this graph.");
        }

        return DependencyGraphDefinition.Apply(body, DependencyGraphEdits.Transaction(
            DependencyGraphEdits.Connect(command.Id, command.From, command.To, command.Label)));
    }
}
