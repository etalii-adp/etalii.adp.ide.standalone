using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The property rows of a selected element or connection, contributed as data the grid renders
/// without understanding - and the one route a canvas resize reaches the document by.
/// </summary>
/// <remarks>
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
public sealed class FdgContextPropertyProvider : IContextPropertyProvider
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

    private const string IdentityGroup = "Identity";
    private const string SizeGroup = "Size";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IFdgDocumentStore _documents;

    public FdgContextPropertyProvider(IHistoryStackStore historyStacks, IFdgDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return Rows([]);
        }

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;

        if (FdgEdits.ElementOf(model, target.ElementId) is { } element)
        {
            List<ContextPropertyDefinition> rows = element.IsComment
                ? [new(TextProperty, "Text", element.Text, ContextPropertyEditor.Text, Group: IdentityGroup)]
                : [new(NameProperty, "Name", element.Name, Group: IdentityGroup)];
            rows.Add(new(DescriptionProperty, "Description", element.Description, ContextPropertyEditor.Text, Group: IdentityGroup));
            rows.Add(new(WidthProperty, "Width", Number(element.Width), Group: SizeGroup));
            if (element.IsComment)
            {
                rows.Add(new(HeightProperty, "Height", Number(element.DrawnHeight), Group: SizeGroup));
            }

            return Rows(rows);
        }

        if (FdgEdits.ConnectionOf(model, target.ElementId) is { } connection)
        {
            return Rows(
            [
                new(ConnectionNameProperty, "Name", connection.Name, Group: IdentityGroup),
                new(DescriptionProperty, "Description", connection.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
            ]);
        }

        return Rows([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var model = _documents.GetOrLoad(body).Model;
        var element = FdgEdits.ElementOf(model, id);

        // A size is validated before any command exists, so the refusal names the value - and an
        // invalid value is never written.
        double? size = null;
        if (propertyId is WidthProperty or HeightProperty)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                return ContextPropertyResult.Failure($"'{value}' is not a size this graph can draw.");
            }

            size = parsed;
        }

        ICommand? command = propertyId switch
        {
            NameProperty or TextProperty when element is not null => new RenameFdgElementCommand(body, id, value),
            DescriptionProperty => new SetFdgDescriptionCommand(body, id, value),
            WidthProperty when element is not null => new SetFdgSizeCommand(body, id, size!.Value),
            HeightProperty when element is not null => new SetFdgSizeCommand(body, id, element.Width, size!.Value),
            ConnectionNameProperty => new RenameFdgConnectionCommand(body, id, value),
            _ => null,
        };

        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        // Through the project's history and out through the delta stream - never written by the grid.
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
