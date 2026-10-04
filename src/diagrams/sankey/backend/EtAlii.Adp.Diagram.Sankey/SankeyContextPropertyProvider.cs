using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The property rows of a selected node or flow, contributed as data the grid renders without
/// understanding.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row edits through <see cref="SetSankeyPropertyCommand"/></b>, so a value typed in the
/// grid is checked by the same rules as one stepped on the canvas, and lands on the project's undo
/// stack like any other edit.
/// </para>
/// <para>
/// <b>A colour is offered twice</b>: as a palette word to pick, which reads well in both themes,
/// and as a custom <c>#rrggbb</c> to type, for a brand colour the palette does not have. Both write
/// the one <c>color</c> key.
/// </para>
/// <para>
/// <b>A node's value is shown and not edited</b>: it is what flows through it, so it changes by
/// changing a flow.
/// </para>
/// </remarks>
public sealed class SankeyContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>The name of a node.</summary>
    public const string NameProperty = "sankey.name";

    /// <summary>The palette colour of a node or a flow.</summary>
    public const string ColorProperty = "sankey.color";

    /// <summary>A custom <c>#rrggbb</c> colour of a node or a flow.</summary>
    public const string CustomColorProperty = "sankey.custom-color";

    /// <summary>The line under a node's value.</summary>
    public const string NoteProperty = "sankey.note";

    /// <summary>How a node's value is written.</summary>
    public const string FormatProperty = "sankey.format";

    /// <summary>The column a node is drawn in.</summary>
    public const string ColumnProperty = "sankey.column";

    /// <summary>The description of a node or a flow.</summary>
    public const string DescriptionProperty = "sankey.description";

    /// <summary>A node's value, read-only, or a flow's value.</summary>
    public const string ValueProperty = "sankey.value";

    /// <summary>How much one + or − changes a flow's value.</summary>
    public const string StepProperty = "sankey.step";

    /// <summary>The colour choice that removes the entry's own colour.</summary>
    public const string DefaultColor = "(default)";

    /// <summary>The colour choice shown while a custom colour is in use.</summary>
    public const string CustomColor = "(custom)";

    /// <summary>The column choice that lets the flows decide.</summary>
    public const string AutoColumn = "auto";

    private const string IdentityGroup = "Identity";
    private const string LookGroup = "Look";
    private const string AmountGroup = "Amount";

    private readonly IHistoryStackStore _historyStacks;
    private readonly ISankeyDocumentStore _documents;

    public SankeyContextPropertyProvider(IHistoryStackStore historyStacks, ISankeyDocumentStore documents)
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

        if (SankeyEdits.NodeOf(model, target.ElementId) is { } node)
        {
            var layout = SankeyLayout.Of(model);
            var value = layout.Values.GetValueOrDefault(node.Id);
            var columns = layout.ColumnOrder.Count;
            return Rows(
            [
                new(NameProperty, "Name", node.Name, Group: IdentityGroup),
                new(DescriptionProperty, "Description", node.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new(ColorProperty, "Colour", ColorChoice(node.Color), ContextPropertyEditor.Choice, Group: LookGroup, Candidates: ColorChoices(node.Color)),
                new(CustomColorProperty, "Custom colour", SankeyColors.IsHex(node.Color) ? node.Color : "", Group: LookGroup),
                new(ColumnProperty, "Column", node.Column is { } column ? Number(column) : AutoColumn, ContextPropertyEditor.Choice, Group: LookGroup,
                    Candidates: [AutoColumn, .. Enumerable.Range(1, Math.Max(columns, node.Column ?? 0) + 1).Select(c => Number(c))]),
                new(ValueProperty, "Value", Number(value), ReadOnlyReason: "A node's value is what flows through it; change a flow's value instead.", Group: AmountGroup),
                new(NoteProperty, "Note", node.Note, Group: AmountGroup),
                new(FormatProperty, "Format", node.Format, Group: AmountGroup),
            ]);
        }

        if (SankeyEdits.FlowOf(model, target.ElementId) is { } flow)
        {
            return Rows(
            [
                new(DescriptionProperty, "Description", flow.Description, ContextPropertyEditor.Text, Group: IdentityGroup),
                new(ColorProperty, "Colour", ColorChoice(flow.Color), ContextPropertyEditor.Choice, Group: LookGroup, Candidates: ColorChoices(flow.Color)),
                new(CustomColorProperty, "Custom colour", SankeyColors.IsHex(flow.Color) ? flow.Color : "", Group: LookGroup),
                new(ValueProperty, "Value", Number(flow.Value), Group: AmountGroup),
                new(StepProperty, "Step", Number(flow.Step ?? StepSankeyValueCommandHandler.DefaultStep(flow.Value)), Group: AmountGroup),
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

        string? key = propertyId switch
        {
            NameProperty => SankeyKeys.Name,
            ColorProperty or CustomColorProperty => SankeyKeys.Color,
            NoteProperty => SankeyKeys.Note,
            FormatProperty => SankeyKeys.Format,
            ColumnProperty => SankeyKeys.Column,
            DescriptionProperty => SankeyKeys.Description,
            ValueProperty => SankeyKeys.Value,
            StepProperty => SankeyKeys.Step,
            _ => null,
        };

        if (key is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        if (propertyId == ColorProperty && value == CustomColor)
        {
            // Picking the custom entry keeps the custom colour; a new one is typed in its own row.
            return ContextPropertyResult.Success;
        }

        value = propertyId switch
        {
            ColorProperty when value == DefaultColor => "",
            ColumnProperty when value == AutoColumn => "",
            _ => value,
        };

        var command = new SetSankeyPropertyCommand(body, target.ElementId, key, value);
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static string ColorChoice(string color) =>
        color.Length == 0 ? DefaultColor : SankeyColors.IsHex(color) ? CustomColor : color;

    private static IReadOnlyList<string> ColorChoices(string color) =>
    [
        DefaultColor,
        .. SankeyColors.Palette,
        .. SankeyColors.IsHex(color) ? new[] { CustomColor } : [],
    ];

    private static string Number(double? value) => value is { } number ? number.ToString("0.####", CultureInfo.InvariantCulture) : "";

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
