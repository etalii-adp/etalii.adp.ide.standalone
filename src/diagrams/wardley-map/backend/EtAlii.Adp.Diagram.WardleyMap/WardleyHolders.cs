namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// From an identity to the element it names, on a map read through the binding: the holder of that kind and key is found
/// among <see cref="WardleyMap.Holders"/>, and the element is the one its line declares (finding B9, ruling 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The keys are the binding's</b> (FBL 0.12 §8.6): this computes none, it only looks them up, so a selection, a move, a
/// row and a menu each reach the one statement the identity was given for. A later holder of a repeated key has the key
/// that carries its ordinal, so it is found as itself and never as the first holder.
/// </para>
/// <para>
/// What it replaced, <c>WardleyIdentityKeys</c>, built the keys itself from the parsed map; it left the product in
/// wardley-map-disl-fbl task 12, and stays beside the tests as the oracle the binding's keys are checked against.
/// </para>
/// </remarks>
public static class WardleyHolders
{
    /// <summary>The 1-based line of the element of <paramref name="kind"/> whose key is <paramref name="key"/>, or null.</summary>
    public static uint? LineOf(WardleyMap map, string kind, string key)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Holders.FirstOrDefault(holder => holder.Kind == kind && holder.Key == key)?.Line;
    }

    /// <summary>The component, anchor or submap whose key is <paramref name="key"/>, or null.</summary>
    public static WardleyComponent? Component(WardleyMap map, string key) =>
        Find(map, WardleyIdentityKind.Component, key, map.Components, component => component.Line);

    /// <summary>The pipeline component whose key is <paramref name="key"/>, with its pipeline, or null.</summary>
    public static (WardleyPipeline Pipeline, WardleyPipelineChild Child)? Child(WardleyMap map, string key)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (LineOf(map, WardleyIdentityKind.PipelineChild, key) is not { } line) return null;

        foreach (var pipeline in map.Pipelines)
        {
            if (pipeline.Children.FirstOrDefault(child => child.Line == line) is { } found) return (pipeline, found);
        }

        return null;
    }

    /// <summary>The one of <paramref name="items"/> declared on the line of the holder of <paramref name="kind"/> and <paramref name="key"/>, or null.</summary>
    public static T? Find<T>(WardleyMap map, string kind, string key, IEnumerable<T> items, Func<T, uint> lineOf)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lineOf);
        return LineOf(map, kind, key) is { } line ? items.FirstOrDefault(item => lineOf(item) == line) : null;
    }

    /// <summary>
    /// Whether <paramref name="component"/> is the first in document order to carry its name - the holder every link,
    /// `evolve` and `pipeline` naming it attaches to (ruling 3).
    /// </summary>
    public static bool IsFirstHolder(WardleyMap map, WardleyComponent component)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(component);
        return map.Components.FirstOrDefault(candidate => candidate.Name == component.Name)?.Line == component.Line;
    }
}
