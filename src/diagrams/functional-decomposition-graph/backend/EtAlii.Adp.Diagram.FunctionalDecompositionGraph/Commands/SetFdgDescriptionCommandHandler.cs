using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets the Description of an element or a connection.</summary>
public sealed class SetFdgDescriptionCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<SetFdgDescriptionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, SetFdgDescriptionCommand command)
    {
        if (model.ElementOf(command.Id) is { } element)
        {
            return FdgWriter.SetDescription(document, element, command.Description);
        }

        return model.ConnectionOf(command.Id) is { } connection
            ? FdgWriter.SetConnectionDescription(document, connection, command.Description)
            : EditOutcome.Refused("That is no longer in this graph.");
    }
}
