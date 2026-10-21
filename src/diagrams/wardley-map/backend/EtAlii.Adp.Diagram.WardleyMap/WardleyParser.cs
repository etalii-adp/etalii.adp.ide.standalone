using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Reads a <see cref="LineDocument"/> into a <see cref="WardleyMap"/> (Requirement 5).
/// </summary>
/// <remarks>
/// <para>
/// Line-oriented, because the DSL is: every statement is one line, with exactly one nested
/// construct (`pipeline Parent { ... }`) that goes one level deep and no further. There is no
/// scope stack beyond "am I inside a pipeline block".
/// </para>
/// <para>
/// <b>It never fails.</b> A line it does not recognise is skipped - the document keeps it
/// (Requirement 3.3) and the validator judges what it can (Requirement 14). Parsing and judging
/// are kept apart deliberately: it is what lets a map with a dangling link still open and render
/// (Requirement 3.5). <see cref="Outcome(LineDocument)"/> reports each skipped line as a problem
/// (code-structure FR-049); a statement the DSL knows and this module does not model, such as
/// <c>y_axis</c>, is not one.
/// </para>
/// <para>
/// Coordinates stay in the document's `[visibility, maturity]` order. The conversion to
/// <c>Point2D</c> lives in the mapper and nowhere else (Requirement 5.2).
/// </para>
/// </remarks>
public static partial class WardleyParser
{
    public static WardleyMap Parse(LineDocument document) => Outcome(document).Model;

    /// <summary>
    /// What <paramref name="document"/> states, with a problem for every line it passed over: a
    /// statement whose shape it does not recognise, or a line that is no statement at all. The
    /// outcome is always usable, because a map with a bad line still draws every good one.
    /// </summary>
    public static ParseOutcome<WardleyMap> Outcome(LineDocument document) => Outcome(document, Statements);

    /// <summary>
    /// <see cref="Outcome(LineDocument)"/> with the statement table given, so that a test can
    /// show the table's order does not matter.
    /// </summary>
    internal static ParseOutcome<WardleyMap> Outcome(LineDocument document, IReadOnlyList<Statement> statements)
    {
        ArgumentNullException.ThrowIfNull(document);

        var reading = new Reading();
        foreach (var line in CodeLines(document))
        {
            var text = _comments.StripComment(line.Text).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (reading.InPipeline)
            {
                reading.ReadInPipeline(text, line);
                continue;
            }

            // An opening brace on its own line belongs to the pipeline above it; the pipeline
            // statement has already opened, so this is only reached when a document braces
            // something else, which this module does not model.
            if (!text.StartsWith('{'))
            {
                ReadStatement(reading, statements, text, line);
            }
        }

        return reading.Finish();
    }

    /// <summary>
    /// The statements the DSL starts with a keyword and this module reads, each with how it is
    /// read. A reader answers false for a statement it could not read, which is then passed over.
    /// </summary>
    /// <remarks>
    /// <b>Order does not matter.</b> A statement is found by its first word, whole and ignoring
    /// case, and no two entries share a keyword, so `annotations [0.6, 0.02]` is never tried as
    /// an `annotation`, wherever the two stand. The parser's tests shuffle this table and require
    /// the same outcome.
    /// </remarks>
    internal static readonly IReadOnlyList<Statement> Statements =
    [
        new("title", (map, rest, _) => Kept(rest, value => map.Title = value)),
        new("style", (map, rest, _) => Kept(rest, value => map.Style = value)),
        new("size", (map, rest, _) => Kept(ParseSize(rest), value => map.Size = value)),
        new("annotations", (map, rest, _) => Kept(ParseCoordinateIn(rest), value => map.AnnotationsPosition = value)),
        new("annotation", (map, rest, number) => Kept(ParseAnnotation(rest, number), map.Annotations.Add)),
        new("evolve", (map, rest, number) => Kept(ParseEvolve(rest, number), map.Evolves.Add)),
        new("note", (map, rest, number) => Kept(ParseNote(rest, number), map.Notes.Add)),
        new("url", (map, rest, number) => Kept(ParseUrlDefinition(rest, number), map.Urls.Add)),
        new("pioneers", (map, rest, number) => Kept(ParseAttitude(WardleyAttitudeKind.Pioneers, rest, number), map.Attitudes.Add)),
        new("settlers", (map, rest, number) => Kept(ParseAttitude(WardleyAttitudeKind.Settlers, rest, number), map.Attitudes.Add)),
        new("townplanners", (map, rest, number) => Kept(ParseAttitude(WardleyAttitudeKind.TownPlanners, rest, number), map.Attitudes.Add)),
        new("accelerator", (map, rest, number) => Kept(ParseAccelerator(rest, number, isDeaccelerator: false), map.Accelerators.Add)),
        new("deaccelerator", (map, rest, number) => Kept(ParseAccelerator(rest, number, isDeaccelerator: true), map.Accelerators.Add)),
        new("pipeline", (map, rest, number) => map.Open(ParsePipelineHeader(rest, number))),
        new("component", (map, rest, number) => Kept(ParseComponent(WardleyElementKind.Component, rest, number), map.Components.Add)),
        new("anchor", (map, rest, number) => Kept(ParseComponent(WardleyElementKind.Anchor, rest, number), map.Components.Add)),
        new("submap", (map, rest, number) => Kept(ParseComponent(WardleyElementKind.Submap, rest, number), map.Components.Add)),
    ];

    /// <summary>One statement of <see cref="Statements"/>: its keyword, and how the rest of its line is read.</summary>
    internal sealed record Statement(string Keyword, Func<Reading, string, uint, bool> Read);

    /// <summary>What one parse has read so far, the pipeline it is filling, and the lines it passed over.</summary>
    internal sealed class Reading
    {
        private readonly List<ParseProblem> _problems = [];
        private readonly List<WardleyPipeline> _pipelines = [];

        // The pipeline currently being filled, and the children gathered for it. Null means the
        // reader is at the top level, which is where it spends almost all of its time.
        private WardleyPipeline? _openPipeline;
        private List<WardleyPipelineChild> _openChildren = [];

        public string Title { get; set; } = "";

        public string Style { get; set; } = "";

        public WardleyMapSize? Size { get; set; }

        public WardleyCoordinate? AnnotationsPosition { get; set; }

        public List<WardleyComponent> Components { get; } = [];

        public List<WardleyLink> Links { get; } = [];

        public List<WardleyEvolve> Evolves { get; } = [];

        public List<WardleyAttitude> Attitudes { get; } = [];

        public List<WardleyAccelerator> Accelerators { get; } = [];

        public List<WardleyNote> Notes { get; } = [];

        public List<WardleyAnnotation> Annotations { get; } = [];

        public List<WardleyUrlDefinition> Urls { get; } = [];

        public bool InPipeline => _openPipeline is not null;

        public void Skipped(WardleyLine line, string message) => _problems.Add(new ParseProblem((int)line.Number - 1, message));

        /// <summary>
        /// Opens <paramref name="pipeline"/> for its children, or, in the legacy form, which is
        /// complete on its own line and has no block, keeps it at once. False when there is none.
        /// </summary>
        public bool Open(WardleyPipeline? pipeline)
        {
            if (pipeline is null)
            {
                return false;
            }

            if (pipeline.Form == WardleyPipelineForm.Legacy)
            {
                _pipelines.Add(pipeline);
            }
            else
            {
                _openPipeline = pipeline;
                _openChildren = [];
            }

            return true;
        }

        /// <summary>A line inside the open pipeline: its closing brace, one of its children, or a line passed over.</summary>
        public void ReadInPipeline(string text, WardleyLine line)
        {
            if (text.StartsWith('}'))
            {
                Close();
            }
            else if (ParsePipelineChild(text, line.Number) is { } child)
            {
                _openChildren.Add(child);
            }
            else if (!text.StartsWith('{'))
            {
                Skipped(line, "This line inside a pipeline is not a component of it, so it was passed over.");
            }
        }

        public ParseOutcome<WardleyMap> Finish()
        {
            // A document that opens a pipeline and never closes it still yields the children it
            // gathered, rather than losing them to a missing brace.
            Close();

            return new ParseOutcome<WardleyMap>(new WardleyMap(
                Title,
                Components.ToArray(),
                Links.ToArray(),
                _pipelines.ToArray(),
                Evolves.ToArray(),
                Attitudes.ToArray(),
                Accelerators.ToArray(),
                Notes.ToArray(),
                Annotations.ToArray(),
                Urls.ToArray(),
                Size,
                Style,
                AnnotationsPosition), _problems);
        }

        private void Close()
        {
            if (_openPipeline is { } pipeline)
            {
                _pipelines.Add(pipeline with { Children = _openChildren.ToArray() });
            }

            _openPipeline = null;
            _openChildren = [];
        }
    }

    /// <summary>
    /// One top-level line: the statement its keyword names, else a link, else a line passed over -
    /// unless it is a statement this module skips by design.
    /// </summary>
    private static void ReadStatement(Reading reading, IReadOnlyList<Statement> statements, string text, WardleyLine line)
    {
        (string keyword, string rest) = KeywordOf(text);
        var statement = statements.FirstOrDefault(entry => string.Equals(entry.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
        if (statement is not null)
        {
            if (!statement.Read(reading, rest, line.Number))
            {
                reading.Skipped(line, Unread(statement.Keyword));
            }

            return;
        }

        if (ParseLink(text, line.Number) is { } link)
        {
            reading.Links.Add(link);
        }
        else if (!UnmodelledKeywords.Contains(keyword))
        {
            reading.Skipped(line, "This line is not a statement of a Wardley map, so it was passed over.");
        }
    }

    /// <summary>Hands <paramref name="value"/> to <paramref name="keep"/> when there is one, and says whether there was.</summary>
    private static bool Kept<T>(T? value, Action<T> keep)
        where T : class
    {
        if (value is null)
        {
            return false;
        }

        keep(value);
        return true;
    }

    private static string Unread(string keyword) =>
        $"This {keyword.ToLowerInvariant()} statement could not be read, so it was passed over.";

    /// <summary>
    /// A statement's keyword - its first word, whole - and the rest of the line after it. Whole,
    /// so `titles` is not `title` and `annotations` is not `annotation`.
    /// </summary>
    private static (string Keyword, string Remainder) KeywordOf(string text)
    {
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return end == text.Length ? (text, "") : (text[..end], text[(end + 1)..].Trim());
    }

    /// <summary>
    /// `evolve Name x` or `evolve Name-&gt;NewName x`, where the trailing number is the target
    /// maturity and everything before it is the name (Requirement 6.1).
    /// </summary>
    private static WardleyEvolve? ParseEvolve(string rest, uint number)
    {
        var match = EvolveExpression().Match(rest);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        return name.Length == 0 || !TryNumber(match.Groups["maturity"].Value, out var maturity)
            ? null
            : new WardleyEvolve(name, maturity, number, match.Groups["override"].Value.Trim());
    }

    /// <summary>
    /// `[visibility1, maturity1, visibility2, maturity2]` - visibility first, confirmed against
    /// the reference parser rather than inferred (Requirement 6.4).
    /// </summary>
    private static WardleyAttitude? ParseAttitude(WardleyAttitudeKind kind, string rest, uint number)
    {
        var match = AttitudeExpression().Match(rest);
        if (!match.Success)
        {
            return null;
        }

        var values = match.Groups["values"].Value.Split(',');
        if (values.Length != 4)
        {
            return null;
        }

        if (!TryNumber(values[0], out var visibility) || !TryNumber(values[1], out var maturity)
            || !TryNumber(values[2], out var visibility2) || !TryNumber(values[3], out var maturity2))
        {
            return null;
        }

        return new WardleyAttitude(
            kind,
            new WardleyCoordinate(visibility, maturity),
            new WardleyCoordinate(visibility2, maturity2),
            number);
    }

    private static WardleyAccelerator? ParseAccelerator(string rest, uint number, bool isDeaccelerator)
    {
        var named = ParseNameAndPosition(rest);
        return named is null
            ? null
            : new WardleyAccelerator(named.Value.Name, named.Value.Position, number, isDeaccelerator);
    }

    private static WardleyNote? ParseNote(string rest, uint number)
    {
        var named = ParseNameAndPosition(rest);
        return named is null ? null : new WardleyNote(named.Value.Name, named.Value.Position, number);
    }

    /// <summary>`url name [address]` - the address is a URL, not a coordinate pair.</summary>
    private static WardleyUrlDefinition? ParseUrlDefinition(string rest, uint number)
    {
        var match = UrlDefinitionExpression().Match(rest);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        var address = match.Groups["address"].Value.Trim();
        return name.Length == 0 || address.Length == 0 ? null : new WardleyUrlDefinition(name, address, number);
    }

    /// <summary>
    /// `annotation N [x, y] text` or `annotation N [[x, y], [x, y]] text` - one number, one or
    /// more occurrences, then free text (Requirement 6.8).
    /// </summary>
    private static WardleyAnnotation? ParseAnnotation(string rest, uint number)
    {
        var match = AnnotationExpression().Match(rest);
        if (!match.Success || !int.TryParse(match.Groups["number"].Value, out var annotationNumber))
        {
            return null;
        }

        var occurrences = new List<WardleyCoordinate>();
        foreach (Match pair in CoordinateExpression().Matches(match.Groups["positions"].Value))
        {
            if (ParseCoordinate(pair.Groups["values"].Value) is { } coordinate)
            {
                occurrences.Add(coordinate);
            }
        }

        // No position at all is not an annotation this module can place. Losing one of several
        // would be worse, and is what the list exists to prevent.
        return occurrences.Count == 0
            ? null
            : new WardleyAnnotation(annotationNumber, occurrences.ToArray(), match.Groups["text"].Value.Trim(), number);
    }

    /// <summary>The shape `accelerator`, `deaccelerator` and `note` share: a name, then a position.</summary>
    private static (string Name, WardleyCoordinate Position)? ParseNameAndPosition(string rest)
    {
        var match = CoordinateExpression().Match(rest);
        if (!match.Success)
        {
            return null;
        }

        var name = rest[..match.Index].Trim();
        var position = ParseCoordinate(match.Groups["values"].Value);
        return name.Length == 0 || position is null ? null : (name, position);
    }

    private static WardleyCoordinate? ParseCoordinateIn(string text)
    {
        var match = CoordinateExpression().Match(text);
        return match.Success ? ParseCoordinate(match.Groups["values"].Value) : null;
    }

    /// <summary>
    /// The DSL's comments: everything from a `//` on, quoted or not.
    /// </summary>
    /// <remarks>
    /// A `//` preceded by a colon is a URL scheme, not a comment. The DSL has no quoted string,
    /// so this is the only place two slashes can appear without starting a comment - and
    /// missing it silently truncated every `url name [https://...]` definition to
    /// `url name [https:`, which parsed as nothing at all. A `//` inside a quoted name does start
    /// one (behaviour-changes B-04, kept as the rule until it is fixed as a defect of its own).
    /// </remarks>
    private static readonly LineLexer _comments = new(new CommentPolicy(["//"], OutsideQuotesOnly: false, NotAfter: ':'), EscapePolicy.None);

    /// <summary>
    /// One of the three statement kinds, from the rest of its line after the keyword. `market`
    /// and `ecosystem` look like kinds and are decorators, so they have no entry in
    /// <see cref="Statements"/> (Requirement 5.1).
    /// </summary>
    private static WardleyComponent? ParseComponent(WardleyElementKind kind, string rest, uint number)
    {
        var coordinates = CoordinateExpression().Match(rest);
        if (!coordinates.Success)
        {
            // A statement kind with no coordinate pair is not something this module can place.
            // The document keeps the line; the validator has the model to notice it is missing.
            return null;
        }

        var position = ParseCoordinate(coordinates.Groups["values"].Value);
        if (position is null)
        {
            return null;
        }

        var name = rest[..coordinates.Index].Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var trailer = rest[(coordinates.Index + coordinates.Length)..];
        return new WardleyComponent(
            name,
            kind,
            position,
            number,
            DecoratorsIn(trailer),
            InertiaIn(trailer),
            LabelOffsetIn(trailer),
            UrlIn(trailer));
    }

    /// <summary>
    /// The keywords the reference parser knows and this module does not model. A statement that
    /// starts with one is skipped by design, so a skip of one is no problem, and the document
    /// keeps it because nothing ever splices it.
    /// </summary>
    private static readonly HashSet<string> UnmodelledKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "y_axis", "x_axis", "evolution", "presentation",
    };

    /// <summary>
    /// Every keyword the DSL starts a statement with, whether or not this module models it.
    /// </summary>
    /// <remarks>
    /// This exists so an unmodelled statement is <b>skipped</b> rather than mistaken for
    /// something else. `y_axis Value chain-&gt;Invisible-&gt;Visible` contains two arrows and would
    /// otherwise read as a link, putting an element on the canvas that the document never
    /// declared - which is a worse failure than not modelling the statement at all, and the
    /// opposite of what Requirement 3.3 asks for. They are those of <see cref="Statements"/> and
    /// those of <see cref="UnmodelledKeywords"/>, declared after both because a static field is
    /// initialised in the order the fields are written.
    /// </remarks>
    private static readonly HashSet<string> KnownKeywords =
        new(Statements.Select(statement => statement.Keyword).Concat(UnmodelledKeywords), StringComparer.OrdinalIgnoreCase);

    private static WardleyLink? ParseLink(string text, uint number)
    {
        // A statement that opens with a keyword is that statement, never a link - even when it
        // happens to contain an arrow.
        if (KnownKeywords.Contains(KeywordOf(text).Keyword))
        {
            return null;
        }

        var match = LinkExpression().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var source = match.Groups["source"].Value.Trim();
        var target = match.Groups["target"].Value.Trim();
        if (source.Length == 0 || target.Length == 0)
        {
            return null;
        }

        var kind = match.Groups["arrow"].Value == "+>" ? WardleyLinkKind.Flow : WardleyLinkKind.Dependency;
        return new WardleyLink(source, target, kind, number, match.Groups["context"].Value.Trim());
    }

    private static WardleyPipeline? ParsePipelineHeader(string rest, uint number)
    {
        if (rest.Length == 0)
        {
            return null;
        }

        var coordinates = CoordinateExpression().Match(rest);
        if (!coordinates.Success)
        {
            var nested = rest.TrimEnd('{').Trim();
            return nested.Length == 0
                ? null
                : new WardleyPipeline(nested, [], number, WardleyPipelineForm.Nested);
        }

        // Two coordinates on the pipeline's own line is the legacy form (Requirement 5.4).
        var name = rest[..coordinates.Index].Trim();
        var extent = ParseCoordinate(coordinates.Groups["values"].Value);
        return name.Length == 0 || extent is null
            ? null
            : new WardleyPipeline(name, [], number, WardleyPipelineForm.Legacy, extent);
    }

    private static WardleyPipelineChild? ParsePipelineChild(string text, uint number)
    {
        (string keyword, string rest) = KeywordOf(text);
        if (!string.Equals(keyword, "component", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // A child carries ONE number - its evolution position - so the two-value coordinate
        // expression does not match it. Its visibility is the parent's, which is why it is not
        // a WardleyComponent (Requirement 5.4).
        var coordinate = SingleCoordinateExpression().Match(rest);
        if (!coordinate.Success)
        {
            return null;
        }

        var name = rest[..coordinate.Index].Trim();
        return name.Length == 0 || !TryNumber(coordinate.Groups["value"].Value, out var maturity)
            ? null
            : new WardleyPipelineChild(name, maturity, number);
    }

    private static WardleyCoordinate? ParseCoordinate(string values)
    {
        var parts = values.Split(',');
        return parts.Length != 2 || !TryNumber(parts[0], out var visibility) || !TryNumber(parts[1], out var maturity)
            ? null
            : new WardleyCoordinate(visibility, maturity);
    }

    private static WardleyMapSize? ParseSize(string rest)
    {
        var match = CoordinateExpression().Match(rest);
        if (!match.Success)
        {
            return null;
        }

        var parts = match.Groups["values"].Value.Split(',');
        return parts.Length != 2 || !TryNumber(parts[0], out var width) || !TryNumber(parts[1], out var height)
            ? null
            : new WardleyMapSize(width, height);
    }

    /// <summary>Invariant culture throughout: the DSL writes `0.79`, whatever the machine's locale says.</summary>
    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static IReadOnlyList<WardleyDecorator> DecoratorsIn(string trailer)
    {
        var found = new List<WardleyDecorator>();
        foreach (Match match in DecoratorExpression().Matches(trailer))
        {
            // Unknown decorators are ignored rather than rejected, matching the reference
            // parser, which tolerates one from a newer DSL silently. The document keeps it.
            if (DecoratorOf(match.Groups["name"].Value) is { } decorator && !found.Contains(decorator))
            {
                found.Add(decorator);
            }
        }

        return found.ToArray();
    }

    private static WardleyDecorator? DecoratorOf(string name) => name.ToLowerInvariant() switch
    {
        "market" => WardleyDecorator.Market,
        "ecosystem" => WardleyDecorator.Ecosystem,
        "build" => WardleyDecorator.Build,
        "buy" => WardleyDecorator.Buy,
        "outsource" => WardleyDecorator.Outsource,
        _ => null,
    };

    /// <summary>`inertia` is a bare word rather than a parenthesised decorator (Requirement 6.2).</summary>
    private static bool InertiaIn(string trailer) => InertiaExpression().IsMatch(trailer);

    private static WardleyLabelOffset? LabelOffsetIn(string trailer)
    {
        var match = LabelExpression().Match(trailer);
        if (!match.Success)
        {
            return null;
        }

        var parts = match.Groups["values"].Value.Split(',');
        return parts.Length != 2 || !TryNumber(parts[0], out var x) || !TryNumber(parts[1], out var y)
            ? null
            : new WardleyLabelOffset(x, y);
    }

    private static string UrlIn(string trailer)
    {
        var match = UrlExpression().Match(trailer);
        return match.Success ? match.Groups["name"].Value.Trim() : "";
    }

    [GeneratedRegex(@"\[\s*(?<values>-?[\d.]+\s*,\s*-?[\d.]+)\s*\]")]
    private static partial Regex CoordinateExpression();

    /// <summary>One bracketed number, which is what a pipeline child carries.</summary>
    [GeneratedRegex(@"\[\s*(?<value>-?[\d.]+)\s*\]")]
    private static partial Regex SingleCoordinateExpression();

    [GeneratedRegex(@"^(?<source>[^-+]+?)\s*(?<arrow>->|\+>)\s*(?<target>[^;]+?)\s*(?:;\s*(?<context>.*))?$")]
    private static partial Regex LinkExpression();

    [GeneratedRegex(@"\(\s*(?<name>[A-Za-z]+)\s*\)")]
    private static partial Regex DecoratorExpression();

    [GeneratedRegex("(?<![A-Za-z])inertia(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex InertiaExpression();

    [GeneratedRegex(@"label\s*\[\s*(?<values>-?[\d.]+\s*,\s*-?[\d.]+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex LabelExpression();

    [GeneratedRegex(@"url\s*\(\s*(?<name>[^)]+)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlExpression();

    /// <summary>`Name x` or `Name-&gt;Override x`, the number being the target maturity.</summary>
    [GeneratedRegex(@"^(?<name>.+?)\s*(?:->\s*(?<override>[^\d]+?)\s*)?(?<maturity>-?[\d.]+)\s*$")]
    private static partial Regex EvolveExpression();

    /// <summary>Four bracketed numbers: an attitude region's two corners.</summary>
    [GeneratedRegex(@"\[\s*(?<values>-?[\d.]+\s*,\s*-?[\d.]+\s*,\s*-?[\d.]+\s*,\s*-?[\d.]+)\s*\]")]
    private static partial Regex AttitudeExpression();

    /// <summary>`name [address]`, where the address is a URL rather than coordinates.</summary>
    [GeneratedRegex(@"^(?<name>\S+)\s*\[\s*(?<address>[^\]]+?)\s*\]\s*$")]
    private static partial Regex UrlDefinitionExpression();

    /// <summary>`N [positions] text`, the positions being one pair or a bracketed list of pairs.</summary>
    [GeneratedRegex(@"^(?<number>\d+)\s*(?<positions>\[.*\])\s*(?<text>.*)$")]
    private static partial Regex AnnotationExpression();

    /// <summary>The lines paired with their 1-based numbers, as a problem location numbers them.</summary>
    internal static IEnumerable<WardleyLine> CodeLines(LineDocument document) =>
        document.Lines.Select((line, index) => new WardleyLine(line.Text, (uint)(index + 1)));
}
