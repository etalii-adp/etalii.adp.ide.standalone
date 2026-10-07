using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The property rows of a selected node or dependency, contributed as data the panel renders
/// without understanding - this module changes nothing in the panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rows are derived from the DISL definition</b> (<see cref="DependencyGraphDefinition.Rows"/>):
/// its forms' items, labels, groups and read-only reasons, with the row ids of its
/// <c>x-dependencies</c> block. Setting a value is here: it is parsed as the file reads it, with
/// .NET's invariant number rules, and written as a splice.
/// </para>
/// <para>
/// Three rows for a node where the timeline had four or five, and the difference is the whole
/// point: begin, end and duration are <b>absent</b> rather than blank. A grid that offered an
/// empty "Begin" would be inviting a value this type has nowhere to put.
/// </para>
/// </remarks>
public sealed class DependencyGraphContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>The node's or dependency's label row.</summary>
    public const string LabelProperty = "dependencies.label";

    /// <summary>The node's horizontal coordinate row.</summary>
    public const string XProperty = "dependencies.x";

    /// <summary>The node's row row - the placement index, not a pixel.</summary>
    public const string RowProperty = "dependencies.row";

    /// <summary>A dependency's dependent end, read-only.</summary>
    public const string FromProperty = "dependencies.from";

    /// <summary>A dependency's depended-upon end, read-only.</summary>
    public const string ToProperty = "dependencies.to";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the provider over the history and the one document store.</summary>
    public DependencyGraphContextPropertyProvider(
        IHistoryStackStore historyStacks, IDependencyGraphDocumentStore documents)
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

        // From and To are read-only and labelled by what they mean rather than by their key names:
        // which end is which is this type's whole content, and "From"/"To" alone leaves the reader to
        // guess which way the arrow points. The rows are the definition's forms.
        return Rows(DependencyGraphDefinition.Rows(_documents.GetOrLoad(target.ResolvedFullPath).Model, target.ElementId));
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;
        var element = DependencyGraphEdits.ElementOf(model, target.ElementId);

        // Validated before any command is made, so the refusal names the value rather than a
        // command failure - and so an invalid value is never written.
        if (element is not null && propertyId is XProperty or RowProperty && ParseNumber(propertyId, value) is null)
        {
            return ContextPropertyResult.Failure($"'{value}' is not a number this graph can place a node at.");
        }

        var command = CommandFor(target, element, propertyId, value);
        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        // Through the project's history and out through the delta stream - never written by the
        // panel.
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private ICommand? CommandFor(ContextTarget target, DependencyGraphElement? element, string propertyId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;

        if (element is null)
        {
            // A dependency: only its label is editable, and the read-only rows explain why.
            return propertyId == LabelProperty &&
                DependencyGraphEdits.RelationOf(_documents.GetOrLoad(body).Model, id) is not null
                ? new RelabelDependencyGraphRelationCommand(body, id, value)
                : null;
        }

        return propertyId switch
        {
            LabelProperty => new RenameDependencyGraphElementCommand(body, id, value),
            XProperty when ParseNumber(propertyId, value) is { } x =>
                new SetDependencyGraphPlacementCommand(body, id, x, element.Row, "Edited"),
            RowProperty when ParseNumber(propertyId, value) is { } row =>
                new SetDependencyGraphPlacementCommand(body, id, element.X, (int)row, "Edited"),
            _ => null,
        };
    }

    /// <summary>
    /// The number a placement row was given, invariantly - or null when it is not one.
    /// </summary>
    /// <remarks>
    /// A row is whole and a coordinate need not be, so the two are parsed by different rules and
    /// a fractional row is refused rather than silently truncated.
    /// </remarks>
    private static double? ParseNumber(string propertyId, string value)
    {
        if (propertyId == RowProperty)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row)
                ? row
                : null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            ? x
            : null;
    }

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
