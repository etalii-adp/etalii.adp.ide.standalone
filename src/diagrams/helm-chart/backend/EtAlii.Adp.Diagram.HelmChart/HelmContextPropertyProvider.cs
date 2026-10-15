using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// What the property grid shows for a selected chart element - everything real, and none of it
/// editable (Requirement 9.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds no logic</b>: it finds the chart folder of the registration an element was selected in
/// and hands out <see cref="HelmDefinition.Rows"/>, the rows derived from the bundled definition, and
/// refuses a write with <see cref="HelmDefinition.Refusal"/> (helm-chart-disl-fbl, Requirements 5.1 and 5.5).
/// </para>
/// <para>
/// Every row carries a non-empty <see cref="ContextPropertyDefinition.ReadOnlyReason"/> naming
/// the file the value actually lives in - the "Defined in X; edit it in a text editor."
/// convention the first read-only provider established. Read-only is enforced rather than
/// styled: <c>ContextPropertyResolver</c> refuses a write to a read-only property server-side,
/// and <see cref="SetAsync"/> refuses anyway, because a provider that would silently accept a
/// write if the resolver ever changed is a trap rather than a design.
/// </para>
/// <para>
/// The chart node's grid carries the whole-chart summary (name, version, type, apiVersion and
/// the counts) - Requirement 9.4's nothing-selected summary rides the chart node, the one
/// element that means "this chart".
/// </para>
/// <para>
/// <b>It does not derive from <see cref="DiagramPropertyProvider{TBody, TModel}"/></b>, because
/// everything that frame carries is for a document that can be edited: it loads a document of a
/// store, guards one that could not be read and dispatches a change through the project's history,
/// while this reads a folder and edits nothing (code-structure, spec 025: a module that cannot adopt
/// a shared type keeps its own).
/// </para>
/// </remarks>
public sealed class HelmContextPropertyProvider : IContextPropertyProvider
{
    private readonly IHelmChartStore _store;

    public HelmContextPropertyProvider(IHelmChartStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // The target is the registration the element was selected in; the chart is its folder.
        var folder = IoPath.GetDirectoryName(target.ResolvedFullPath);
        if (target.ElementId.Length == 0 || folder is null || !Directory.Exists(folder))
        {
            return Empty;
        }

        return ValueTask.FromResult(HelmDefinition.Rows(_store.GetOrLoad(folder), target.ElementId));
    }

    /// <summary>Refuses, always, with the definition's <c>std.readOnly</c> - a refusal, never an exception.</summary>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target, string propertyId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextPropertyResult.Failure(HelmDefinition.Refusal));

    private static readonly ValueTask<IReadOnlyList<ContextPropertyDefinition>> Empty =
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
}
