namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A parsed Wardley map: what the document says, in the document's own vocabulary
/// (Requirements 5.1-5.6).
/// </summary>
/// <param name="Title">The `title` line's text, or empty.</param>
/// <param name="Components">Every positioned element, in written order.</param>
/// <param name="Links">Every link, endpoints held by name.</param>
/// <param name="Pipelines">Every pipeline, each remembering which form it was written in.</param>
/// <param name="Size">The `size [w, h]` the document asks to be drawn at, or null.</param>
/// <param name="Style">The `style` name, or empty.</param>
/// <remarks>
/// <para>
/// This is a <b>reading</b>, not a resolved model. Links name their endpoints as strings, a
/// pipeline names its parent as a string, and nothing here has been checked for consistency:
/// a link to a component that does not exist parses perfectly well and becomes a problem
/// Requirement 14.2 reports. Keeping the reading and the judging apart is what lets a map with
/// one broken link still open and render (Requirement 3.5).
/// </para>
/// <para>
/// Statements the module does not model are absent from this record and present in the
/// document, which is the whole of Requirement 3.3: the writer only ever splices lines it was
/// asked to change, so an unmodelled statement survives by never being touched.
/// </para>
/// </remarks>
public sealed record WardleyMap(
    string Title,
    IReadOnlyList<WardleyComponent> Components,
    IReadOnlyList<WardleyLink> Links,
    IReadOnlyList<WardleyPipeline> Pipelines,
    WardleyMapSize? Size = null,
    string Style = "")
{
    /// <summary>An empty map - what a missing or blank `.owm` reads as (Requirement 2.4).</summary>
    public static WardleyMap Empty { get; } = new("", [], [], []);
}
