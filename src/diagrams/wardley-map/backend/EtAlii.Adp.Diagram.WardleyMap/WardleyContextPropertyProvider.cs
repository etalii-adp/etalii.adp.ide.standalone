using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What the property grid shows for a selected Wardley element, and what happens when one of
/// those values is edited (Requirement 15).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rows, their read-only reasons, what a typed value is refused for and what it writes are
/// the definition's</b> (wardley-map-disl-fbl Requirement 3.1): its inspector forms, derived and
/// committed by <see cref="WardleyDefinition"/>, and written by today's commands
/// (<see cref="WardleyTransactionCommands"/>). Typing <c>0.42</c> into the Maturity row and dragging
/// the component to the same place are still the same command and the same undo entry (Requirement 15.7).
/// </para>
/// <para>
/// <b>A file that cannot be written keeps every row</b>, each giving the definition's read-only reason,
/// and no value is taken (Requirement 15.11).
/// </para>
/// </remarks>
public sealed class WardleyContextPropertyProvider(IHistoryStackStore historyStacks, IWardleyDocumentStore documents)
    : DiagramPropertyProvider<LineDocument, WardleyMap>(historyStacks, documents)
{
    public const string NamePropertyId = "wardley.name";
    public const string KindPropertyId = "wardley.kind";
    public const string VisibilityPropertyId = "wardley.visibility";
    public const string MaturityPropertyId = "wardley.maturity";
    public const string StagePropertyId = "wardley.stage";
    public const string LabelOffsetPropertyId = "wardley.label-offset";
    public const string EvolvePropertyId = "wardley.evolve";
    public const string EvolveNamePropertyId = "wardley.evolve-name";
    public const string InertiaPropertyId = "wardley.inertia";
    public const string DecoratorsPropertyId = "wardley.decorators";
    public const string UrlPropertyId = "wardley.url";

    public const string LinkSourcePropertyId = "wardley.link.source";
    public const string LinkTargetPropertyId = "wardley.link.target";
    public const string LinkKindPropertyId = "wardley.link.kind";
    public const string LinkContextPropertyId = "wardley.link.context";

    public const string PipelineChildrenPropertyId = "wardley.pipeline.children";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <summary>An element of a map of this module's; the map itself has no rows.</summary>
    protected override bool Serves(ContextTarget target) =>
        base.Serves(target) && target.ElementId.Length > 0;

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(ContextTarget target, DocumentEntry<LineDocument, WardleyMap> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        return WardleyDefinition.Rows(entry, documents.Identities(target.ResolvedFullPath), target.ElementId, WardleyEditability.Editable(target.ResolvedFullPath));
    }

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, DocumentEntry<LineDocument, WardleyMap> entry, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return DiagramPropertyChange.Refused("That element is not on a Wardley map.");
        }

        if (!WardleyEditability.Editable(target.ResolvedFullPath))
        {
            return DiagramPropertyChange.Refused(WardleyEditability.Reason);
        }

        return WardleyDefinition.Change(entry, documents.Identities(target.ResolvedFullPath), target.ResolvedFullPath, target.ElementId, propertyId, value);
    }
}
