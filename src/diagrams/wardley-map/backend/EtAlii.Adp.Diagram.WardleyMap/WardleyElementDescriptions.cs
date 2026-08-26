using System.Globalization;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What to call an element, given only its id. The selection, the actions and the property grid
/// all start from an id and all need the same answer, so it is worked out once here
/// (Requirement 11.3).
/// </summary>
/// <remarks>
/// An identity's <c>Key</c> is a <b>handle</b>, not a label: a link's key is its two endpoint
/// names and its kind run together, and an attitude's is its kind and a corner. Those find the
/// element again; they are not what a human should be shown. So this looks the element up by
/// key and then describes the element itself.
/// </remarks>
public static class WardleyElementDescriptions
{
    /// <summary>
    /// The element <paramref name="elementId"/> names, or null when the map no longer has it -
    /// which is the ordinary answer after another connection deleted it, not an error.
    /// </summary>
    public static WardleyElementDescription? Of(
        WardleyMap map,
        IReadOnlyList<WardleyIdentityEntry> identities,
        string elementId)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(identities);

        var entry = identities.FirstOrDefault(candidate => candidate.Id == elementId);
        return entry is null ? null : Of(map, entry);
    }

    /// <summary>The element one identity entry names, or null when it is gone.</summary>
    public static WardleyElementDescription? Of(WardleyMap map, WardleyIdentityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind switch
        {
            WardleyIdentityKind.Component => Component(map, entry.Key),
            WardleyIdentityKind.PipelineChild => PipelineChild(map, entry.Key),
            WardleyIdentityKind.Link => Link(map, entry.Key),
            WardleyIdentityKind.Note => Note(map, entry.Key),
            WardleyIdentityKind.Annotation => Annotation(map, entry.Key),
            WardleyIdentityKind.Accelerator => Accelerator(map, entry.Key),
            WardleyIdentityKind.Attitude => Attitude(map, entry.Key),
            _ => null,
        };
    }

    private static WardleyElementDescription? Component(WardleyMap map, string key)
    {
        var component = map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == key);
        if (component is null)
        {
            return null;
        }

        // A component "has children" exactly when it is a pipeline parent. Nothing else in this
        // notation contains anything: a Wardley map is a flat space, and the containment the
        // selection chain models is the one the pipeline introduces.
        var hasChildren = map.Pipelines.Any(pipeline =>
            pipeline.Parent == component.Name && pipeline.Children.Count > 0);

        return new WardleyElementDescription(component.Name, [component.Name], hasChildren);
    }

    private static WardleyElementDescription? PipelineChild(WardleyMap map, string key)
    {
        foreach (var pipeline in map.Pipelines)
        {
            foreach (var child in pipeline.Children)
            {
                if (WardleyIdentityKeys.Of(pipeline, child) == key)
                {
                    // The only two-segment path this notation produces: a child is named
                    // beneath the component whose pipeline holds it.
                    return new WardleyElementDescription(child.Name, [pipeline.Parent, child.Name], HasChildren: false);
                }
            }
        }

        return null;
    }

    private static WardleyElementDescription? Link(WardleyMap map, string key)
    {
        var link = map.Links.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == key);
        if (link is null)
        {
            return null;
        }

        // A link has no name of its own, so it is described by what it joins - the same shape
        // C4 uses for a relationship. The context, where the document gives one, is what
        // distinguishes two links between the same pair.
        var arrow = link.Kind == WardleyLinkKind.Flow ? "+>" : "->";
        var text = link.Context.Length > 0
            ? $"{link.Source} {arrow} {link.Target}: {link.Context}"
            : $"{link.Source} {arrow} {link.Target}";

        return new WardleyElementDescription(text, [text], HasChildren: false);
    }

    private static WardleyElementDescription? Note(WardleyMap map, string key)
    {
        var note = map.Notes.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == key);
        return note is null ? null : new WardleyElementDescription(note.Text, [note.Text], HasChildren: false);
    }

    private static WardleyElementDescription? Annotation(WardleyMap map, string key)
    {
        var annotation = map.Annotations.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == key);
        if (annotation is null)
        {
            return null;
        }

        var number = annotation.Number.ToString(CultureInfo.InvariantCulture);
        var text = annotation.Text.Length > 0 ? $"{number}. {annotation.Text}" : number;
        return new WardleyElementDescription(text, [text], HasChildren: false);
    }

    private static WardleyElementDescription? Accelerator(WardleyMap map, string key)
    {
        var accelerator = map.Accelerators.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == key);
        if (accelerator is null)
        {
            return null;
        }

        // An accelerator and a deaccelerator may carry the same name for opposing forces on the
        // same thing, so the direction is part of what is shown as well as part of the key.
        var text = accelerator.IsDeaccelerator ? $"{accelerator.Name} (deaccelerator)" : accelerator.Name;
        return new WardleyElementDescription(text, [text], HasChildren: false);
    }

    private static WardleyElementDescription? Attitude(WardleyMap map, string key)
    {
        var attitude = map.Attitudes.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == key);
        if (attitude is null)
        {
            return null;
        }

        var text = Label(attitude.Kind);
        return new WardleyElementDescription(text, [text], HasChildren: false);
    }

    private static string Label(WardleyAttitudeKind kind) => kind switch
    {
        WardleyAttitudeKind.Settlers => "Settlers",
        WardleyAttitudeKind.TownPlanners => "Town Planners",
        _ => "Pioneers",
    };
}
