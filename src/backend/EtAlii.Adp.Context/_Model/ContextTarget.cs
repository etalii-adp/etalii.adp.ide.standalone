using EtAlii.Adp.Documents.Wire;
namespace EtAlii.Adp.Context;

/// <summary>
/// What a context action applies to, already resolved by the service layer: a provider
/// receives the absolute location it should act on and never resolves anything itself,
/// so containment is enforced in exactly one place.
/// </summary>
/// <param name="Scope">The surface this target belongs to.</param>
/// <param name="ResolvedFullPath">The absolute, containment-checked location this source resolved to.</param>
/// <param name="IsContainer">Whether the target holds other targets (for the hierarchy scope: a folder).</param>
/// <param name="SourceId">The id the client used to name this target, echoed back for correlation.</param>
/// <param name="Root">The project root the target was resolved within; what a provider reaches the project's own state through.</param>
/// <param name="WatchId">The connection the target was resolved for; what a provider reaches per-connection state through (a viewer's fold state, say).</param>
/// <param name="ElementId">For a target inside a diagram file, the element's id; empty for a file or folder.</param>
/// <param name="Origin">
/// The diagram type the target was resolved through, for a target inside a diagram file, and
/// <c>null</c> where it is unknown or does not apply.
/// <para>
/// One file may carry several registrations - the RDF family reads one <c>.ttl</c> as a data
/// graph, an ontology, a scheme and a shapes graph - and every reading names its elements with
/// the same ids on purpose, so the id cannot say which reading the user is looking at. Without
/// this, a provider must infer the reading from what the document happens to assert rather than
/// from the registration that actually named it, and a file whose content fits two readings gets
/// both readings' verbs whichever one was opened.
/// </para>
/// <para>
/// This does NOT separate keystrokes, and it is worth saying so because the opposite is easy to
/// assume. <c>ContextActionResolver</c> concatenates on discovery but takes the first match when
/// resolving an id or a shortcut - per provider, flattening that provider's own groups. A family
/// whose readings delegate INSIDE one provider therefore has flatten order as its shortcut order,
/// so ordering the groups is the whole fix there. The origin decides between providers, which
/// matters only once a reading registers a provider of its own.
/// </para>
/// <para>
/// The resolver that builds a target already knows this: it routes the registration file and reads the definition off it. It simply used to throw the
/// answer away. <c>DescribeToolbox</c> has always keyed on exactly this, so carrying it here
/// makes the context seams agree with the toolbox seam rather than inventing a key.
/// </para>
/// <para>
/// Null means "not known", never "no reading": a provider seeing null falls back to whatever it
/// did before this field existed, so resolvers adopt it one at a time.
/// </para>
/// </param>
/// <param name="RegistrationPath">
/// The registration the target was resolved through, for a target inside a diagram file, and
/// <c>null</c> where it is unknown or does not apply.
/// <para>
/// The origin says which tool type a file is read as; it does not say which part of the file a
/// registration shows. A registration may carry headers of its own type that pick one declaration
/// out of a file declaring several, and two registrations of one file and one type can then show
/// different ones. A provider that reads only the body cannot tell which of them the user is
/// looking at, and acts on whichever comes first; with the registration in hand it reads the same
/// headers the diagram was opened with.
/// </para>
/// <para>
/// Null means "not known", as for the origin: a provider seeing null does what it did before this
/// field existed.
/// </para>
/// </param>
public sealed record ContextTarget(
    ContextScope Scope,
    string ResolvedFullPath,
    bool IsContainer,
    ShortGuid SourceId,
    ProjectRoot Root = default,
    ShortGuid WatchId = default,
    string ElementId = "",
    ToolOrigin? Origin = null,
    string? RegistrationPath = null)
{
    /// <summary>
    /// The element <paramref name="elementId"/> of the diagram file at <paramref name="bodyPath"/>,
    /// resolved within <paramref name="root"/>: a leaf, named by no client id - the shape almost
    /// every element target takes.
    /// </summary>
    public static ContextTarget ForElement(
        string bodyPath, string elementId, ProjectRoot root = default, ShortGuid watchId = default, ToolOrigin? origin = null) =>
        new(ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, root, watchId, elementId, origin);
}
