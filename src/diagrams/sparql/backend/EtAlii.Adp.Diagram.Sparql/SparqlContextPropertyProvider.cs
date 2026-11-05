using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The property rows of a selected query element, contributed as data the panel renders without
/// understanding (Requirement 6.2). <b>Every row is read-only, and every row carries the reason
/// why</b> - naming the text editor as where a query is edited. The property-grid contract
/// enforces that a read-only row states a reason, so this is not decoration: it is the shape the
/// panel requires, and it is what makes the diagram honest about being a reading surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>It hands out what the definition derives</b> (sparql-disl-fbl, task 8): an element's rows are its form's
/// (<see cref="SparqlDefinition.Rows"/>), the header band's the query's own (<see cref="SparqlChrome.Rows"/>), and a set
/// is refused with the definition's <c>std.readOnly</c>. It holds no row, label or reason of its own.
/// </para>
/// <para>
/// <b>It stays off <see cref="DiagramPropertyProvider{TBody, TModel}"/></b> (code-structure T099):
/// that frame exists to dispatch a changed value through the project's history, which would make this
/// provider take a history it never uses and answer a set on an unreadable query with the frame's
/// "cannot be edited" rather than the one reason every row of this diagram gives.
/// </para>
/// </remarks>
public sealed class SparqlContextPropertyProvider : IContextPropertyProvider
{
    private readonly ISparqlDocumentStore _documents;

    public SparqlContextPropertyProvider(ISparqlDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!SparqlSelection.CouldBeQueryFile(target.ResolvedFullPath))
        {
            return [];
        }

        var entry = await _documents.GetOrLoadAsync(target.ResolvedFullPath, cancellationToken);
        if (!entry.IsUsable || target.ElementId is not { Length: > 0 } elementId)
        {
            return [];
        }

        return SparqlChrome.IsChrome(elementId)
            ? SparqlChrome.Rows(entry.Model, elementId)
            : SparqlDefinition.Rows(entry.Model, elementId);
    }

    /// <summary>
    /// Refused, always: no property here is editable, so a set arriving anyway is answered with
    /// the reason rather than quietly ignored.
    /// </summary>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        _ = propertyId;
        _ = value;

        return ValueTask.FromResult(ContextPropertyResult.Failure(SparqlDefinition.Refusal));
    }
}
