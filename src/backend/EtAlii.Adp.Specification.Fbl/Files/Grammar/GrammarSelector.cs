namespace EtAlii.Adp.Specification.Fbl.Files.Grammar;

/// <summary>The kinds of segment of a selector of the grammar family (FBL 0.9 §4.9).</summary>
internal enum GrammarSegmentKind
{
    /// <summary>The child nodes of a rule or alias, and the child tokens of a terminal, of that name.</summary>
    Name,

    /// <summary><c>*</c>: any child node or token.</summary>
    Any,

    /// <summary><c>**</c>: any number of levels, including none.</summary>
    Descendants,

    /// <summary><c>.</c>, as the whole selector of a <c>{node}</c> slot: the entry itself.</summary>
    Self,
}

/// <summary>
/// One segment of a selector of the grammar family: a name, <c>*</c>, <c>**</c> or <c>.</c>, and for a name the test
/// <c>name[path='v']</c>, a relative selector of segments without tests and the text a node or token there has.
/// </summary>
internal sealed record GrammarSegment(GrammarSegmentKind Kind, string Name, IReadOnlyList<GrammarSegment>? TestPath = null, string? TestValue = null);

/// <summary>The selectors of the grammar family (FBL 0.9 §4.9): parsed, and checked against the grammar's names.</summary>
internal static class GrammarSelector
{
    /// <summary>
    /// The segments of <paramref name="selector"/>, absolute or relative; null when it is not a selector of the family.
    /// <paramref name="slot"/> admits <c>.</c>, which only a <c>{node}</c> slot may write, and then as its whole selector.
    /// </summary>
    public static IReadOnlyList<GrammarSegment>? Parse(string selector, bool slot)
    {
        if (selector == ".") return slot ? [new GrammarSegment(GrammarSegmentKind.Self, ".")] : null;
        var body = selector.StartsWith('/') ? selector[1..] : selector;
        if (body.Length == 0) return selector == "/" && !slot ? [] : null;
        var segments = new List<GrammarSegment>();
        foreach (var text in Split(body))
        {
            if (ParseSegment(text, tests: true) is not { } segment) return null;
            segments.Add(segment);
        }
        return segments;
    }

    /// <summary>
    /// The names <paramref name="selector"/> writes, its tests' included, that are not among <paramref name="names"/>:
    /// the rules, aliases and terminals of the grammar.
    /// </summary>
    public static IEnumerable<string> UnknownNames(IReadOnlyList<GrammarSegment> selector, IReadOnlySet<string> names) =>
        selector.SelectMany(Named).Where(name => !names.Contains(name)).Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> Named(GrammarSegment segment) =>
        segment.Kind == GrammarSegmentKind.Name ? [segment.Name, .. (segment.TestPath ?? []).SelectMany(Named)] : [];

    private static GrammarSegment? ParseSegment(string text, bool tests)
    {
        if (text == "*") return new GrammarSegment(GrammarSegmentKind.Any, "*");
        if (text == "**") return new GrammarSegment(GrammarSegmentKind.Descendants, "**");
        var bracket = text.IndexOf('[');
        var name = bracket < 0 ? text : text[..bracket];
        if (!IsName(name)) return null;
        if (bracket < 0) return new GrammarSegment(GrammarSegmentKind.Name, name);
        if (!tests || !text.EndsWith("']", StringComparison.Ordinal)) return null;
        var test = text[(bracket + 1)..^1];
        var equals = test.IndexOf("='", StringComparison.Ordinal);
        if (equals <= 0) return null;
        var value = test[(equals + 2)..^1];
        if (value.Contains('\'')) return null;
        var path = new List<GrammarSegment>();
        foreach (var part in test[..equals].Split('/'))
        {
            if (ParseSegment(part, tests: false) is not { } inner) return null;
            path.Add(inner);
        }
        return new GrammarSegment(GrammarSegmentKind.Name, name, path, value);
    }

    /// <summary>The segments of a selector, cut at every <c>/</c> outside a test's quoted text.</summary>
    private static List<string> Split(string selector)
    {
        var parts = new List<string>();
        var start = 0;
        var quoted = false;
        for (var i = 0; i < selector.Length; i++)
        {
            if (selector[i] == '\'') quoted = !quoted;
            else if (selector[i] == '/' && !quoted)
            {
                parts.Add(selector[start..i]);
                start = i + 1;
            }
        }
        parts.Add(selector[start..]);
        return parts;
    }

    /// <summary>A name of a rule, alias or terminal: a letter or <c>_</c>, then letters, digits and <c>_</c>.</summary>
    private static bool IsName(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
