using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets a Name, or a Comment's text: whichever the element says.</summary>
public sealed class RenameFdgElementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<RenameFdgElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, RenameFdgElementCommand command) =>
        model.ElementOf(command.ElementId) switch
        {
            null => Gone,
            { IsComment: true } comment => FdgWriter.SetText(document, comment, command.Value),
            var element => FdgWriter.SetName(document, element, command.Value),
        };
}
