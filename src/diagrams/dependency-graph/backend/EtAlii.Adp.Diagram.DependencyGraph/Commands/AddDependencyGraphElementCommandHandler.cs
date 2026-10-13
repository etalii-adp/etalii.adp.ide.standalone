using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out an add. The inverse is the removal of what was added.</summary>
public sealed class AddDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<AddDependencyGraphElementCommand>(documents)
{
    /// <inheritdoc />
    /// <remarks>
    /// The timeline refused an element with no begin and one born ending before it began. Neither
    /// has a counterpart here: every coordinate is a position, including zero and including a
    /// negative one, so there is nothing left to refuse but a missing id.
    /// </remarks>
    protected override CommandResult Execute(AddDependencyGraphElementCommand command) =>
        string.IsNullOrWhiteSpace(command.Id)
            ? CommandResult.Failure("A node needs an id.")
            : base.Execute(command);

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, AddDependencyGraphElementCommand command)
    {
        if (DependencyGraphEdits.ElementOf(model, command.Id) is not null)
        {
            // Redo lands here when the node survived - re-adding it would duplicate the id.
            return EditOutcome.Refused("A node with that id is already in this graph.");
        }

        DependencyGraphWriter.InsertElement(body, model, command.Id, command.Label, command.X, command.Row);
        return EditOutcome.UndoneBy(new RemoveDependencyGraphElementCommand(command.BodyPath, command.Id));
    }
}
