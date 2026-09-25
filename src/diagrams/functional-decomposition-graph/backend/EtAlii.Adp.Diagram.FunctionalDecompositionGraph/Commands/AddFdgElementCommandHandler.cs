using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Adds an element at a drop, sized and named as the design's command table says.</summary>
public sealed class AddFdgElementCommandHandler(IFdgDocumentStore documents) : ICommandHandler<AddFdgElementCommand>
{
    /// <summary>A new element's width, for the four named types.</summary>
    public const double DefaultWidth = 160;

    /// <summary>A new Comment's width.</summary>
    public const double CommentWidth = 240;

    /// <summary>A new Comment's height; the other four share <see cref="FdgGeometry.SharedHeight"/>.</summary>
    public const double CommentHeight = 96;

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddFdgElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!FdgElementTypes.IsKnown(command.ElementType))
        {
            return Task.FromResult(CommandResult.Failure($"This notation has no `{command.ElementType}` element."));
        }

        // Minted once and carried as the redo, so a redone element keeps its id.
        var minted = command.ElementId.Length > 0
            ? command
            : command with { ElementId = ShortGuid.NewShortGuid().ToString() };

        return FdgEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (model.Elements.Any(element => element.Id == minted.ElementId) ||
                model.Connections.Any(connection => connection.Id == minted.ElementId))
            {
                return FdgEdit.Refused("That id is already used in this graph.");
            }

            var isComment = minted.ElementType == FdgElementTypes.Comment;
            var width = isComment ? CommentWidth : DefaultWidth;
            double? height = isComment ? CommentHeight : null;
            var drawnHeight = height ?? FdgGeometry.SharedHeight;

            // The drop is the centre; the document holds the top-left.
            var element = new FdgElement(
                minted.ElementId,
                minted.ElementType,
                Name: isComment ? "" : UniqueName(model, DefaultNameOf(minted.ElementType)),
                Description: "",
                Text: isComment ? "New comment" : "",
                X: minted.X - (width / 2),
                Y: minted.Y - (drawnHeight / 2),
                Width: width,
                Height: height,
                Range: new LineRange(0, 0));

            return FdgWriter.AddElement(document, model, element);
        });
    }

    private static string DefaultNameOf(string elementType) => elementType switch
    {
        FdgElementTypes.UiElement => "New UI element",
        FdgElementTypes.Action => "New action",
        FdgElementTypes.DataElement => "New data element",
        FdgElementTypes.Function => "New function",
        _ => "New element",
    };

    /// <summary>The name, or the name with the lowest number that makes it unique.</summary>
    private static string UniqueName(FdgModel model, string name)
    {
        var taken = model.Elements.Select(element => element.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            var candidate = $"{name} {number}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
