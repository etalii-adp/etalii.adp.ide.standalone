namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The handle each kind of element offers for finding it again across a reload
/// (Requirement 4.2).
/// </summary>
/// <remarks>
/// The `.owm` format has no identifier of any kind, so every key here is derived from what the
/// document says. They vary in how well they survive an edit made outside ADP, and that is
/// worth being honest about: a component's name is stable and a rename inside ADP rewrites the
/// key with it, while a note's text is the only handle a note has, so editing a note elsewhere
/// loses its identity. The alternative - writing an ADP id into the file - is what
/// Requirement 3.7 forbids.
/// </remarks>
public static class WardleyIdentityKeys
{
    /// <summary>
    /// What separates the parts of a composite key - ASCII unit separator.
    /// </summary>
    /// <remarks>
    /// A key made of several parts needs a separator no part can contain, or two different
    /// elements can produce one key: a link from `A` to `BC` and one from `AB` to `C` would
    /// otherwise both key as `ABC` and share an identity. A control character cannot appear in a
    /// component name, so it is the one separator that is always safe.
    /// </remarks>
    /// <example>
    /// It is named rather than written into the interpolations directly, because a raw U+001F in
    /// a string literal is invisible in every editor and every review - a reader comparing a key
    /// against what the format says would find them equal and be wrong.
    /// </example>
    public const char Separator = '\u001F';

    /// <summary>A component, anchor or submap: its name.</summary>
    public static string Of(WardleyComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Name;
    }

    /// <summary>A link: both endpoints and its kind, so two links between one pair stay distinct.</summary>
    public static string Of(WardleyLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return $"{link.Source}{Separator}{link.Target}{Separator}{link.Kind}";
    }

    /// <summary>A pipeline: its parent's name, since a component has at most one.</summary>
    public static string Of(WardleyPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return pipeline.Parent;
    }

    /// <summary>A pipeline child: its parent's name and its own, since names repeat across pipelines.</summary>
    public static string Of(WardleyPipeline pipeline, WardleyPipelineChild child)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(child);
        return $"{pipeline.Parent}{Separator}{child.Name}";
    }

    /// <summary>A note: its text, which is the only handle it has.</summary>
    public static string Of(WardleyNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return note.Text;
    }

    /// <summary>An annotation: its number, which the map itself shows.</summary>
    public static string Of(WardleyAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return annotation.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// An accelerator: its name and its direction, since an accelerator and a deaccelerator may
    /// legitimately carry the same name for opposing forces on the same thing.
    /// </summary>
    public static string Of(WardleyAccelerator accelerator)
    {
        ArgumentNullException.ThrowIfNull(accelerator);
        return $"{accelerator.Name}{Separator}{(accelerator.IsDeaccelerator ? "de" : "ac")}";
    }

    /// <summary>
    /// An attitude region: its kind and one corner. A map may carry several regions of one
    /// kind, and a region has no name to be known by - the corner is the only other handle it
    /// offers, so moving a region loses its identity. That is acceptable for a backdrop nothing
    /// links to.
    /// </summary>
    public static string Of(WardleyAttitude attitude)
    {
        ArgumentNullException.ThrowIfNull(attitude);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{attitude.Kind}{Separator}{attitude.From.Visibility}{Separator}{attitude.From.Maturity}");
    }
}
