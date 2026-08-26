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
        return $"{link.Source}{link.Target}{link.Kind}";
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
        return $"{pipeline.Parent}{child.Name}";
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
}
