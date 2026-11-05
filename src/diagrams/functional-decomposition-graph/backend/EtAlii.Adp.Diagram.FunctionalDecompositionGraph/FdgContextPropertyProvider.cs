using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The property rows of a selected element or connection, contributed as data the grid renders
/// without understanding - and the one route a canvas resize reaches the document by.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rows are the definition's</b> (<see cref="FdgDefinition.Rows"/>): its forms, mapped to the ids
/// of its <c>x-fdg</c> block. What a set does and what it refuses stays here.
/// </para>
/// <para>
/// <b>Every row is one of the client's ids</b>, stated once in the client module's
/// <c>fdgIds.ts</c>: this provider answers exactly those strings and invents none.
/// </para>
/// <para>
/// <b>A Description for all five types and every connection; a Name for the four named types and
/// for a connection; a Comment's Text instead of a Name.</b> A Comment has no name, so the grid
/// offers none rather than an empty one - an empty "Name" would invite a value the document has
/// nowhere to keep.
/// </para>
/// <para>
/// <b>Width, and a Comment's Height, are rows because the canvas resizes through them.</b> The
/// context service lets a set reach only a property its provider describes as editable, so a size
/// the grid did not list would be a resize the canvas could never make.
/// </para>
/// </remarks>
public sealed class FdgContextPropertyProvider(IHistoryStackStore historyStacks, IFdgDocumentStore documents)
    : DiagramPropertyProvider<FdgBody, DislModel>(historyStacks, documents)
{
    /// <summary>An element's Name, for the four named types.</summary>
    public const string NameProperty = "fdg.name";

    /// <summary>A Comment's text.</summary>
    public const string TextProperty = "fdg.text";

    /// <summary>The Description of any element or connection.</summary>
    public const string DescriptionProperty = "fdg.description";

    /// <summary>An element's width.</summary>
    public const string WidthProperty = "fdg.width";

    /// <summary>A Comment's own height.</summary>
    public const string HeightProperty = "fdg.height";

    /// <summary>A connection's name.</summary>
    public const string ConnectionNameProperty = "fdg.connection-name";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<FdgBody, DislModel> entry, string elementId) =>
        // The definition's form for the element or connection, with the ids of its x-fdg block.
        FdgDefinition.Rows(entry, elementId);

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, DislModel model, string propertyId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var element = FdgDefinition.EndOf(model.Diagram, id);

        // A size is validated before any command exists, so the refusal names the value - and an
        // invalid value is never written.
        double? size = null;
        if (propertyId is WidthProperty or HeightProperty)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                return DiagramPropertyChange.Refused($"'{value}' is not a size this graph can draw.");
            }

            size = parsed;
        }

        // Through the project's history and out through the delta stream - never written by the grid.
        ICommand? command = propertyId switch
        {
            NameProperty or TextProperty when element is not null => new RenameFdgElementCommand(body, id, value),
            DescriptionProperty => new SetFdgDescriptionCommand(body, id, value),
            WidthProperty when element is not null => new SetFdgSizeCommand(body, id, size!.Value),
            HeightProperty when element is not null => new SetFdgSizeCommand(body, id, FdgDefinition.NumberOf(element, "width"), size!.Value),
            ConnectionNameProperty => new RenameFdgConnectionCommand(body, id, value),
            _ => null,
        };

        return command is null
            ? DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.")
            : DiagramPropertyChange.By(command);
    }
}
