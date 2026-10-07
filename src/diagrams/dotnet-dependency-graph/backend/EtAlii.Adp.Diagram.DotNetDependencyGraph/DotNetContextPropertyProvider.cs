using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// What the property grid shows for a selected element of a .NET dependency graph -
/// everything, and none of it editable (Requirements 4 and 5) - derived from the bundled DISL
/// definition's forms (<see cref="DotNetDefinition"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row carries a non-empty <see cref="ContextPropertyDefinition.ReadOnlyReason"/></b>,
/// cause first, then the remedy, naming the file the value actually lives in: the form items'
/// <c>readOnlyReasons</c>. A reader who cannot change a value here deserves to be told what would
/// have to change instead - and for this type the answer is always a file the build owns rather
/// than anything ADP can write.
/// </para>
/// <para>
/// Read-only is enforced rather than styled: <c>ContextPropertyResolver</c> refuses a write to
/// a property its owner described as read-only, server-side, whatever a client claimed.
/// <see cref="SetAsync"/> is therefore unreachable in practice, and refuses anyway, with the
/// definition's <c>std.readOnly</c> - a provider that would accept a write if the resolver ever
/// changed is a trap rather than a design.
/// </para>
/// <para>
/// <b>An absence is shown, not omitted</b> (Requirement 4.4). Where a value cannot be
/// discovered - a project whose framework names no .NET version, a package that has never been
/// restored on this machine - the row is still present and says so, through the item's
/// <c>display</c>. A missing row and a missing value read very differently to someone wondering
/// whether the diagram simply failed to look.
/// </para>
/// </remarks>
public sealed class DotNetContextPropertyProvider : IContextPropertyProvider
{
    private static readonly ValueTask<IReadOnlyList<ContextPropertyDefinition>> Empty =
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);

    private readonly IDependencyGraphStore _store;

    public DotNetContextPropertyProvider(IDependencyGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // Every provider in this scope is consulted for every element selection in ANY diagram, so
        // the first question is whether the file is one of ours - the question every sibling
        // provider asks. Unasked, a Wardley selection made this one parse tea.owm as a solution.
        // An element's target is the routed solution body (DotNetContextSourceResolver passes it),
        // so the solution extensions are the whole test; a registration path never arrives here.
        if (target.ElementId.Length == 0 || !IsSolution(target.ResolvedFullPath))
        {
            return Empty;
        }

        var graph = _store.GetOrLoad(target.ResolvedFullPath);
        return ValueTask.FromResult(DotNetDefinition.Rows(graph, target.ElementId));
    }

    private static bool IsSolution(string path)
    {
        // Qualified: EtAlii.Adp.Documents.Wire.Path, the proto message, shadows System.IO.Path here.
        var extension = System.IO.Path.GetExtension(path);
        return extension.Equals(Diagram.DocumentExtension, StringComparison.OrdinalIgnoreCase)
               || extension.Equals(Diagram.AlternateDocumentExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Refuses, always, with the definition's <c>std.readOnly</c>.</summary>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target, string propertyId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextPropertyResult.Failure(DotNetDefinition.Refusal));
}
