using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Reads a <see cref="WardleyDocument"/> into a <see cref="WardleyMap"/> (Requirement 5).
/// </summary>
/// <remarks>
/// <para>
/// Line-oriented, because the DSL is: every statement is one line, with exactly one nested
/// construct (`pipeline Parent { ... }`) that goes one level deep and no further. There is no
/// scope stack beyond "am I inside a pipeline block".
/// </para>
/// <para>
/// <b>It never fails.</b> A line it does not recognise is skipped, not reported - the document
/// keeps it (Requirement 3.3) and the validator judges what it can (Requirement 14). Parsing
/// and judging are kept apart deliberately: it is what lets a map with a dangling link still
/// open and render (Requirement 3.5).
/// </para>
/// <para>
/// Coordinates stay in the document's `[visibility, maturity]` order. The conversion to
/// <c>Point2D</c> lives in the mapper and nowhere else (Requirement 5.2).
/// </para>
/// </remarks>
public static partial class WardleyParser
{
    public static WardleyMap Parse(WardleyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var title = "";
        var style = "";
        WardleyMapSize? size = null;
        var components = new List<WardleyComponent>();
        var links = new List<WardleyLink>();
        var pipelines = new List<WardleyPipeline>();

        // The pipeline currently being filled, and the children gathered for it. Null means the
        // reader is at the top level, which is where it spends almost all of its time.
        WardleyPipeline? openPipeline = null;
        var openChildren = new List<WardleyPipelineChild>();

        foreach (var line in document.CodeLines)
        {
            var text = WithoutComment(line.Text).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (openPipeline is not null)
            {
                if (text.StartsWith('}'))
                {
                    pipelines.Add(openPipeline with { Children = openChildren.ToArray() });
                    openPipeline = null;
                    openChildren = [];
                    continue;
                }

                if (ParsePipelineChild(text, line.Number) is { } child)
                {
                    openChildren.Add(child);
                }

                continue;
            }

            // An opening brace on its own line belongs to the pipeline above it; the pipeline
            // branch below has already opened, so this is only reached when a document braces
            // something else, which this module does not model.
            if (text.StartsWith('{'))
            {
                continue;
            }

            if (TryKeyword(text, "title", out var titleRest))
            {
                title = titleRest;
                continue;
            }

            if (TryKeyword(text, "style", out var styleRest))
            {
                style = styleRest;
                continue;
            }

            if (TryKeyword(text, "size", out var sizeRest))
            {
                size = ParseSize(sizeRest) ?? size;
                continue;
            }

            if (TryKeyword(text, "pipeline", out var pipelineRest))
            {
                openPipeline = ParsePipelineHeader(pipelineRest, line.Number);
                openChildren = [];

                // The legacy form is complete on its own line: no block follows, so close it now.
                if (openPipeline is { Form: WardleyPipelineForm.Legacy })
                {
                    pipelines.Add(openPipeline);
                    openPipeline = null;
                }

                continue;
            }

            if (ParseComponent(text, line.Number) is { } component)
            {
                components.Add(component);
                continue;
            }

            if (ParseLink(text, line.Number) is { } link)
            {
                links.Add(link);
            }
        }

        // A document that opens a pipeline and never closes it still yields the children it
        // gathered, rather than losing them to a missing brace.
        if (openPipeline is not null)
        {
            pipelines.Add(openPipeline with { Children = openChildren.ToArray() });
        }

        return new WardleyMap(title, components.ToArray(), links.ToArray(), pipelines.ToArray(), size, style);
    }

    /// <summary>
    /// The line without its `//` comment. Naive on purpose: the DSL has no string literal a
    /// `//` could hide inside, so there is nothing to escape.
    /// </summary>
    private static string WithoutComment(string text)
    {
        var index = text.IndexOf("//", StringComparison.Ordinal);
        return index < 0 ? text : text[..index];
    }

    /// <summary>Matches `keyword rest`, requiring whitespace after the keyword so `titles` is not `title`.</summary>
    private static bool TryKeyword(string text, string keyword, out string rest)
    {
        rest = "";
        if (!text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length == keyword.Length)
        {
            return true;
        }

        if (!char.IsWhiteSpace(text[keyword.Length]))
        {
            return false;
        }

        rest = text[(keyword.Length + 1)..].Trim();
        return true;
    }

    private static WardleyComponent? ParseComponent(string text, uint number)
    {
        var kind = KindOf(text, out var rest);
        if (kind is null)
        {
            return null;
        }

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
            kind.Value,
            position,
            number,
            DecoratorsIn(trailer),
            InertiaIn(trailer),
            LabelOffsetIn(trailer),
            UrlIn(trailer));
    }

    /// <summary>
    /// Which of the three statement kinds this line declares, and the rest of the line after
    /// the keyword. Null when the line is not one of them - including `market` and `ecosystem`,
    /// which look like kinds and are decorators (Requirement 5.1).
    /// </summary>
    private static WardleyElementKind? KindOf(string text, out string rest)
    {
        if (TryKeyword(text, "component", out rest))
        {
            return WardleyElementKind.Component;
        }

        if (TryKeyword(text, "anchor", out rest))
        {
            return WardleyElementKind.Anchor;
        }

        return TryKeyword(text, "submap", out rest) ? WardleyElementKind.Submap : null;
    }

    /// <summary>
    /// Every keyword the DSL starts a statement with, whether or not this module models it.
    /// </summary>
    /// <remarks>
    /// This exists so an unmodelled statement is <b>skipped</b> rather than mistaken for
    /// something else. `y_axis Value chain-&gt;Invisible-&gt;Visible` contains two arrows and would
    /// otherwise read as a link, putting an element on the canvas that the document never
    /// declared - which is a worse failure than not modelling the statement at all, and the
    /// opposite of what Requirement 3.3 asks for.
    /// </remarks>
    private static readonly HashSet<string> KnownKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Modelled here, in task 5.
        "component", "anchor", "submap", "pipeline", "title", "size", "style",
        // Modelled in task 6, and already guarded so they never read as links.
        "evolve", "note", "annotation", "annotations", "url",
        "pioneers", "settlers", "townplanners", "accelerator", "deaccelerator",
        // Known to the reference parser, not modelled by this spec. Skipped, and preserved by
        // the document because nothing ever splices them.
        "y_axis", "x_axis", "evolution", "presentation",
    };

    private static WardleyLink? ParseLink(string text, uint number)
    {
        // A statement that opens with a keyword is that statement, never a link - even when it
        // happens to contain an arrow.
        var firstToken = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (KnownKeywords.Contains(firstToken))
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
        if (!TryKeyword(text, "component", out var rest))
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

    [GeneratedRegex(@"(?<![A-Za-z])inertia(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex InertiaExpression();

    [GeneratedRegex(@"label\s*\[\s*(?<values>-?[\d.]+\s*,\s*-?[\d.]+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex LabelExpression();

    [GeneratedRegex(@"url\s*\(\s*(?<name>[^)]+)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlExpression();
}
