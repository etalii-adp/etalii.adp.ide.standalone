namespace EtAlii.Adp.Specification.Fbl.Rules;

/// <summary>
/// FBL §4.2 selectors over the entries of a tree (yaml, json) or of an xml document: keys and names,
/// <c>*</c>, <c>**</c>, <c>{capture}</c> and <c>name[@A='v']</c>, absolute from the root or relative
/// to an entry.
/// </summary>
internal static class Selector
{
    /// <summary>The entries <paramref name="selector"/> reaches from <paramref name="from"/>, in document order, each with its captures.</summary>
    public static IReadOnlyList<(Entry Entry, IReadOnlyDictionary<string, string> Captures)> Match(
        Entry from, string selector, Func<Entry, string, string, bool>? attributeEquals = null)
    {
        var segments = selector.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Segment.Parse).ToArray();
        var results = new List<(Entry, IReadOnlyDictionary<string, string>)>();
        var seen = new HashSet<Entry>();
        Walk(from, 0, new Dictionary<string, string>(StringComparer.Ordinal));
        return results.OrderBy(r => r.Item1.Own.Start).ThenByDescending(r => r.Item1.Own.End).ToList();

        void Walk(Entry entry, int index, Dictionary<string, string> captures)
        {
            if (index == segments.Length)
            {
                if (seen.Add(entry)) results.Add((entry, new Dictionary<string, string>(captures, StringComparer.Ordinal)));
                return;
            }
            var segment = segments[index];
            if (segment.Kind == SegmentKind.Descendants)
            {
                Walk(entry, index + 1, captures);
                foreach (var child in entry.Children) Walk(child, index, captures);
                return;
            }
            foreach (var child in entry.Children)
            {
                switch (segment.Kind)
                {
                    case SegmentKind.Any:
                        Walk(child, index + 1, captures);
                        break;
                    case SegmentKind.Capture:
                        if (child.Name is null) break;
                        var inner = new Dictionary<string, string>(captures, StringComparer.Ordinal) { [segment.Name] = child.Name };
                        Walk(child, index + 1, inner);
                        break;
                    case SegmentKind.Name:
                        if (child.Name != segment.Name) break;
                        if (segment.Attribute is { } attribute && (attributeEquals is null || !attributeEquals(child, attribute, segment.Value!))) break;
                        Walk(child, index + 1, captures);
                        break;
                }
            }
        }
    }

    /// <summary>The root a selector starts from: the document root for an absolute selector, else <paramref name="relativeTo"/>.</summary>
    public static Entry Start(string selector, Entry root, Entry? relativeTo) => selector.StartsWith('/') || relativeTo is null ? root : relativeTo;

    private enum SegmentKind
    {
        Name,
        Any,
        Descendants,
        Capture,
    }

    private sealed record Segment(SegmentKind Kind, string Name, string? Attribute, string? Value)
    {
        public static Segment Parse(string text)
        {
            if (text == "*") return new Segment(SegmentKind.Any, "*", null, null);
            if (text == "**") return new Segment(SegmentKind.Descendants, "**", null, null);
            if (text.StartsWith('{') && text.EndsWith('}')) return new Segment(SegmentKind.Capture, text[1..^1], null, null);
            var bracket = text.IndexOf("[@", StringComparison.Ordinal);
            if (bracket > 0 && text.EndsWith("']", StringComparison.Ordinal))
            {
                var predicate = text[(bracket + 2)..^1];
                var equals = predicate.IndexOf("='", StringComparison.Ordinal);
                if (equals > 0) return new Segment(SegmentKind.Name, text[..bracket], predicate[..equals], predicate[(equals + 2)..^1]);
            }
            return new Segment(SegmentKind.Name, text, null, null);
        }
    }
}
