using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The nine command handlers' bodies as they were before every edit was written by splices
/// (functional-decomposition-graph-disl-fbl task 10): each command applied to a <see cref="LineDocument"/>
/// through <see cref="FdgWriter"/>, at the lines <see cref="FdgParser"/> finds, with the refusals the handlers
/// gave. The oracle that <see cref="FdgDerivedOperationsTests"/> compares the derived edits with.
/// </summary>
/// <remarks>
/// <b>Kept word for word</b> but for the commands being taken as arguments: what each did before its record
/// reached the handler's run (an unknown element type refused, an id minted) is done here too, with the id a
/// test gives in place of a minted one.
/// </remarks>
internal static class HandWrittenFdgEdits
{
    /// <summary>A new element's width, for the four named types.</summary>
    private const double DefaultWidth = 160;

    /// <summary>A new Comment's width.</summary>
    private const double CommentWidth = 240;

    /// <summary>A new Comment's height; the other four share <see cref="FdgGeometry.SharedHeight"/>.</summary>
    private const double CommentHeight = 96;

    /// <summary>The refusal for an element that is not there, in one wording.</summary>
    private static EditOutcome Gone => EditOutcome.Refused("That element is no longer in this graph.");

    /// <summary>Applies <paramref name="command"/> to <paramref name="document"/> as its handler did.</summary>
    public static EditOutcome Apply(LineDocument document, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(document);
        var model = FdgParser.Parse(document);
        return command switch
        {
            AddFdgElementCommand add => Add(document, model, add),
            ConnectFdgElementsCommand connect => Connect(document, model, connect),
            DisconnectFdgConnectionCommand disconnect => model.ConnectionOf(disconnect.ConnectionId) is { } connection
                ? FdgWriter.Disconnect(document, connection)
                : EditOutcome.Refused("That connection is no longer in this graph."),
            RemoveFdgElementCommand remove => model.ElementOf(remove.ElementId) is { } element
                ? FdgWriter.RemoveElement(document, model, element)
                : Gone,
            RenameFdgConnectionCommand rename => model.ConnectionOf(rename.ConnectionId) is { } connection
                ? FdgWriter.SetConnectionName(document, connection, rename.Name)
                : EditOutcome.Refused("That connection is no longer in this graph."),
            RenameFdgElementCommand rename => model.ElementOf(rename.ElementId) switch
            {
                null => Gone,
                { IsComment: true } comment => FdgWriter.SetText(document, comment, rename.Value),
                var element => FdgWriter.SetName(document, element, rename.Value),
            },
            SetFdgDescriptionCommand describe => Describe(document, model, describe),
            SetFdgPlacementCommand move => model.ElementOf(move.ElementId) is { } element
                ? FdgWriter.MoveTo(document, element, move.X, move.Y)
                : Gone,
            SetFdgSizeCommand size => model.ElementOf(size.ElementId) is { } element
                ? FdgWriter.Resize(document, element, size.Width, size.Height)
                : Gone,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "No hand-written edit for this command."),
        };
    }

    private static EditOutcome Add(LineDocument document, FdgModel model, AddFdgElementCommand command)
    {
        if (!FdgElementTypes.IsKnown(command.ElementType))
        {
            return EditOutcome.Refused($"This notation has no `{command.ElementType}` element.");
        }

        if (model.IsTaken(command.ElementId))
        {
            return EditOutcome.Refused("That id is already used in this graph.");
        }

        var isComment = command.ElementType == FdgElementTypes.Comment;
        var width = isComment ? CommentWidth : DefaultWidth;
        double? height = isComment ? CommentHeight : null;
        var drawnHeight = height ?? FdgGeometry.SharedHeight;

        // The drop is the centre; the document holds the top-left.
        var element = new FdgElement(
            command.ElementId,
            command.ElementType,
            Name: isComment ? "" : UniqueName.Of(model.Elements.Select(existing => existing.Name), DefaultNameOf(command.ElementType)),
            Description: "",
            Text: isComment ? "New comment" : "",
            X: command.X - (width / 2),
            Y: command.Y - (drawnHeight / 2),
            Width: width,
            Height: height,
            Range: new LineRange(0, 0));

        return FdgWriter.AddElement(document, model, element);
    }

    private static EditOutcome Connect(LineDocument document, FdgModel model, ConnectFdgElementsCommand command)
    {
        if (ConnectFdgElementsCommandHandler.RefusalOf(FdgBody.Parse(document.Text).Disl.Diagram, command.RelationType, command.FromId, command.ToId) is { } refusal)
        {
            return EditOutcome.Refused(refusal);
        }

        if (model.IsTaken(command.ConnectionId))
        {
            return EditOutcome.Refused("That id is already used in this graph.");
        }

        var connection = new FdgConnection(
            command.ConnectionId,
            command.RelationType,
            command.FromId,
            command.ToId,
            Name: "",
            Description: "",
            Range: new LineRange(0, 0));

        return FdgWriter.Connect(document, model, connection);
    }

    private static EditOutcome Describe(LineDocument document, FdgModel model, SetFdgDescriptionCommand command)
    {
        if (model.ElementOf(command.Id) is { } element)
        {
            return FdgWriter.SetDescription(document, element, command.Description);
        }

        return model.ConnectionOf(command.Id) is { } connection
            ? FdgWriter.SetConnectionDescription(document, connection, command.Description)
            : EditOutcome.Refused("That is no longer in this graph.");
    }

    private static string DefaultNameOf(string elementType) => elementType switch
    {
        FdgElementTypes.UiElement => "New UI element",
        FdgElementTypes.Action => "New action",
        FdgElementTypes.DataElement => "New data element",
        FdgElementTypes.Function => "New function",
        _ => "New element",
    };
}
