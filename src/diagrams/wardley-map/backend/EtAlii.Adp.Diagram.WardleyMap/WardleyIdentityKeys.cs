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
    /// <summary>
    /// The last character of the key of a second or later holder of one kind and key: a tab.
    /// </summary>
    /// <remarks>
    /// Why it cannot collide with the key of an element: every key ends with its last part, and
    /// every last part is a text the parser trimmed, a number written without white space or a
    /// fixed word, so no key an element has ends with white space (finding B9, ruling 1 of
    /// 2026-10-12, the design's <i>Identities of a repeated key</i>).
    /// </remarks>
    private const char LaterHolderMark = '\t';

    /// <summary>
    /// The key of the <paramref name="ordinal"/>-th holder of <paramref name="key"/>: the key itself
    /// for the first, and for each later one the key, U+001F, the ordinal in decimal digits and a tab.
    /// </summary>
    public static string Numbered(string key, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);

        return ordinal == 1
            ? key
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{key}{Separator}{ordinal}{LaterHolderMark}");
    }

    /// <summary>
    /// Each of <paramref name="items"/> with the key of its holder, in their order: the first of
    /// each key takes the key itself, each later one the key with its ordinal.
    /// </summary>
    public static IEnumerable<(string Key, T Item)> Holders<T>(IEnumerable<T> items, Func<T, string> keyOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keyOf);

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = keyOf(item);
            var ordinal = seen.TryGetValue(key, out var count) ? count + 1 : 1;
            seen[key] = ordinal;
            yield return (Numbered(key, ordinal), item);
        }
    }

    /// <summary>
    /// Every element of <paramref name="map"/> with its kind, the key of its holder and its line,
    /// in the order <see cref="WardleyIdentities.Reconcile"/> walks them: document order within a kind.
    /// </summary>
    public static IReadOnlyList<WardleyHolder> All(WardleyMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return
        [
            .. Holders(map.Components, Of).Select(held => new WardleyHolder(WardleyIdentityKind.Component, held.Key, held.Item.Line)),
            .. Holders(map.Links, Of).Select(held => new WardleyHolder(WardleyIdentityKind.Link, held.Key, held.Item.Line)),
            .. PipelinesAndChildren(map),
            .. Holders(map.Notes, Of).Select(held => new WardleyHolder(WardleyIdentityKind.Note, held.Key, held.Item.Line)),
            .. Holders(map.Annotations, Of).Select(held => new WardleyHolder(WardleyIdentityKind.Annotation, held.Key, held.Item.Line)),
            .. Holders(map.Accelerators, Of).Select(held => new WardleyHolder(WardleyIdentityKind.Accelerator, held.Key, held.Item.Line)),
            .. Holders(map.Attitudes, Of).Select(held => new WardleyHolder(WardleyIdentityKind.Attitude, held.Key, held.Item.Line)),
        ];
    }

    /// <summary>The component whose holder key is <paramref name="key"/>, or null.</summary>
    public static WardleyComponent? Component(WardleyMap map, string key) => Find(map.Components, Of, key);

    /// <summary>The pipeline child whose holder key is <paramref name="key"/>, with its pipeline, or null.</summary>
    public static (WardleyPipeline Pipeline, WardleyPipelineChild Child)? Child(WardleyMap map, string key)
    {
        ArgumentNullException.ThrowIfNull(map);

        foreach ((string held, (WardleyPipeline Pipeline, WardleyPipelineChild Child) item) in Holders(ChildrenOf(map), item => Of(item.Pipeline, item.Child)))
        {
            if (held == key)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>The one of <paramref name="items"/> whose holder key is <paramref name="key"/>, or null.</summary>
    public static T? Find<T>(IEnumerable<T> items, Func<T, string> keyOf, string key)
        where T : class =>
        Holders(items, keyOf).FirstOrDefault(held => held.Key == key).Item;

    /// <summary>
    /// Whether <paramref name="component"/> is the first in document order to carry its name - the
    /// holder every link, `evolve` and `pipeline` naming it attaches to (ruling 3).
    /// </summary>
    public static bool IsFirstHolder(WardleyMap map, WardleyComponent component)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(component);

        return map.Components.FirstOrDefault(candidate => candidate.Name == component.Name)?.Line == component.Line;
    }

    private static IEnumerable<(WardleyPipeline Pipeline, WardleyPipelineChild Child)> ChildrenOf(WardleyMap map) =>
        map.Pipelines.SelectMany(pipeline => pipeline.Children.Select(child => (pipeline, child)));

    /// <summary>Each pipeline followed by its children, as Reconcile has always walked them.</summary>
    private static IEnumerable<WardleyHolder> PipelinesAndChildren(WardleyMap map)
    {
        var pipelines = Holders(map.Pipelines, Of).ToArray();
        var children = Holders(ChildrenOf(map), item => Of(item.Pipeline, item.Child)).ToArray();
        var next = 0;

        foreach ((string key, WardleyPipeline pipeline) in pipelines)
        {
            yield return new WardleyHolder(WardleyIdentityKind.Pipeline, key, pipeline.Line);
            for (var index = 0; index < pipeline.Children.Count; index++, next++)
            {
                yield return new WardleyHolder(WardleyIdentityKind.PipelineChild, children[next].Key, children[next].Item.Child.Line);
            }
        }
    }
}
