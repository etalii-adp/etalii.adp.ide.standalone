using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Adds an element at a drop, sized and named as the design's command table says.</summary>
public sealed class AddFdgElementCommandHandler(IFdgDocumentStore documents) : FdgEditHandler<AddFdgElementCommand>(documents)
{
    /// <summary>A new element's width, for the four named types.</summary>
    private const double DefaultWidth = 160;

    /// <summary>A new Comment's width.</summary>
    private const double CommentWidth = 240;

    /// <summary>A new Comment's height; the other four share <see cref="FdgGeometry.SharedHeight"/>.</summary>
    private const double CommentHeight = 96;

    /// <inheritdoc />
    protected override CommandResult Execute(AddFdgElementCommand command)
    {
        return !FdgElementTypes.IsKnown(command.ElementType)
            ? CommandResult.Failure($"This notation has no `{command.ElementType}` element.")
            // Minted once and carried as the redo, so a redone element keeps its id.
            : base.Execute(command.ElementId.Length > 0 ? command : command with { ElementId = ShortGuid.NewShortGuid().ToString() });
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, FdgModel model, AddFdgElementCommand command)
    {
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

    private static string DefaultNameOf(string elementType) => elementType switch
    {
        FdgElementTypes.UiElement => "New UI element",
        FdgElementTypes.Action => "New action",
        FdgElementTypes.DataElement => "New data element",
        FdgElementTypes.Function => "New function",
        _ => "New element",
    };
}
