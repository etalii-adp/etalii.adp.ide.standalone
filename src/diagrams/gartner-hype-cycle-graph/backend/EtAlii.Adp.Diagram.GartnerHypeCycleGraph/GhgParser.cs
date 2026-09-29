using System.Globalization;
using EtAlii.Adp.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Reads a <see cref="LineDocument"/> into a <see cref="GhgModel"/>, recording which lines declare
/// what.
/// </summary>
/// <remarks>
/// <para>
/// <b>YamlDotNet reads and never writes</b>, as in FDG: every write is a splice through
/// <see cref="LineSplice"/>, which is what makes an unchanged document byte-identical
/// (Requirement 2.5) rather than merely equivalent.
/// </para>
/// <para>
/// <b>THIS PARSER NEVER THROWS</b> (Requirement 2.4). A YAML error yields an empty model, the text
/// kept, and the problem reported with its line. An unknown key, a malformed date or a phase count
/// that is not a number is passed over: the entry is kept with whatever could be read, the lines
/// survive because nothing rewrites them, and a problem is recorded for the validator.
/// </para>
/// <para>
/// <b>What breaks a RULE is not a parse problem.</b> A stop before its start, a phase count of 7 or a
/// second influence in one direction all read perfectly well; the parser keeps them as written and
/// <see cref="GhgRuleSet"/> reports them under their own rule ids.
/// </para>
/// </remarks>
public static class GhgParser
{
    internal const string HeaderKey = "gartner-hypecycle-graph";
    private const string TrendsKey = "trends";
    private const string TriggersKey = "triggers";
    private const string NotesKey = "notes";
    private const string InfluencesKey = "influences";
    private const string UnitKey = "unit";

    /// <summary>The keys a trend entry may carry. Anything else survives and is reported.</summary>
    private static readonly string[] TrendKeys =
        ["id", "name", "start", "stop", "row", "phases", "peak-end", "trough-end", "slope-end", "tags", "description"];

    /// <summary>The keys a trigger entry may carry.</summary>
    private static readonly string[] TriggerKeys = ["id", "name", "date", "row", "tags", "description"];

    /// <summary>The keys a note entry may carry.</summary>
    private static readonly string[] NoteKeys = ["id", "text", "at", "row", "width", "height"];

    /// <summary>The keys an influence entry may carry.</summary>
    private static readonly string[] InfluenceKeys =
        ["id", "from", "from-phase", "from-edge", "from-at", "to", "to-phase", "to-edge", "to-at", "description"];

    /// <summary>Reads the document. <b>Never throws.</b></summary>
    public static GhgModel Parse(LineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        YamlStream stream = new();
        try
        {
            using StringReader reader = new(document.Text);
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            var line = Math.Max(0, (int)exception.Start.Line - 1);
            return GhgModel.Empty with { Problems = [new GhgProblem(line, $"The document could not be read as YAML: {exception.Message}")] };
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return GhgModel.Empty;
        }

        List<GhgProblem> problems = [];
        var version = ReadVersion(root, document, problems);
        var trends = ReadTrends(root, document, problems);
        var triggers = ReadTriggers(root, document, problems);
        var notes = ReadNotes(root, document, problems);
        var influences = ReadInfluences(root, document, problems, trends, triggers);
        return new GhgModel(trends, influences, problems, version, ReadUnit(root, document, problems))
        {
            Triggers = triggers,
            Notes = notes,
        };
    }

    /// <summary>The top-level <c>unit:</c>, or the month when there is none. An unknown unit is reported and drawn in months.</summary>
    private static GhgTimeUnit ReadUnit(YamlMappingNode root, LineDocument document, List<GhgProblem> problems)
    {
        if (Node(root, UnitKey) is not { } node)
        {
            return GhgTimeUnit.Month;
        }

        if (node is YamlScalarNode scalar && GhgTimeUnit.Named(scalar.Value) is { } unit)
        {
            return unit;
        }

        var names = string.Join(", ", GhgTimeUnit.All.Select(known => known.Name));
        problems.Add(new GhgProblem(LineOf(node, document), $"`{UnitKey}` is not one of {names}; the diagram is drawn in months."));
        return GhgTimeUnit.Month;
    }

    private static int? ReadVersion(YamlMappingNode root, LineDocument document, List<GhgProblem> problems)
    {
        if (Node(root, HeaderKey) is not YamlScalarNode scalar)
        {
            problems.Add(new GhgProblem(0, $"The document does not begin with `{HeaderKey}: {GhgModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new GhgProblem(LineOf(scalar, document), $"The version `{scalar.Value}` is not a number."));
            return null;
        }

        if (version != GhgModel.CurrentVersion)
        {
            problems.Add(new GhgProblem(LineOf(scalar, document), $"This document states version {version}; this module reads version {GhgModel.CurrentVersion}."));
        }

        return version;
    }

    private static List<GhgTrend> ReadTrends(YamlMappingNode root, LineDocument document, List<GhgProblem> problems)
    {
        List<GhgTrend> trends = [];
        foreach (var node in Sequence(root, TrendsKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                problems.Add(new GhgProblem(LineOf(node, document), "A trend entry is not a mapping and was passed over."));
                continue;
            }

            ReportUnknownKeys(mapping, TrendKeys, document, problems, "trend");

            trends.Add(new GhgTrend(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "name") ?? "",
                Month(mapping, "start", document, problems, required: true),
                Month(mapping, "stop", document, problems, required: true),
                Integer(mapping, "row", 0, document, problems),
                Integer(mapping, "phases", GhgPhases.Count, document, problems, required: true),
                [.. GhgPhases.BoundaryKeys.Select(key => Month(mapping, key, document, problems, required: false))],
                Tags(mapping, document, problems),
                Scalar(mapping, "description") ?? "",
                Range(mapping, document)));
        }

        return trends;
    }

    /// <summary>
    /// The triggers. A missing or malformed <c>date</c> is not reported here but by
    /// <c>ghg.trigger-date</c>, so one breach is reported once.
    /// </summary>
    private static List<GhgTrigger> ReadTriggers(YamlMappingNode root, LineDocument document, List<GhgProblem> problems)
    {
        List<GhgTrigger> triggers = [];
        foreach (var node in Sequence(root, TriggersKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                problems.Add(new GhgProblem(LineOf(node, document), "A trigger entry is not a mapping and was passed over."));
                continue;
            }

            ReportUnknownKeys(mapping, TriggerKeys, document, problems, "trigger");

            triggers.Add(new GhgTrigger(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "name") ?? "",
                GhgScale.ParseMonth(Scalar(mapping, "date")),
                Integer(mapping, "row", 0, document, problems),
                Tags(mapping, document, problems),
                Scalar(mapping, "description") ?? "",
                Range(mapping, document)));
        }

        return triggers;
    }

    /// <summary>
    /// The notes. A missing or malformed <c>at</c>, <c>width</c> or <c>height</c> is not reported
    /// here but by <c>ghg.note-position</c>.
    /// </summary>
    private static List<GhgNote> ReadNotes(YamlMappingNode root, LineDocument document, List<GhgProblem> problems)
    {
        List<GhgNote> notes = [];
        foreach (var node in Sequence(root, NotesKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                problems.Add(new GhgProblem(LineOf(node, document), "A note entry is not a mapping and was passed over."));
                continue;
            }

            ReportUnknownKeys(mapping, NoteKeys, document, problems, "note");

            notes.Add(new GhgNote(
                Scalar(mapping, "id") ?? "",
                Scalar(mapping, "text") ?? "",
                GhgScale.ParseMonth(Scalar(mapping, "at")),
                Integer(mapping, "row", 0, document, problems),
                Number(mapping, "width"),
                Number(mapping, "height"),
                Range(mapping, document)));
        }

        return notes;
    }

    /// <summary>A number, or null when it is missing or is not one.</summary>
    private static double? Number(YamlMappingNode mapping, string key) =>
        double.TryParse(Scalar(mapping, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>
    /// The influences. One from a trigger has no <c>from</c> end: it is read as <see cref="GhgEnd.None"/>,
    /// and any <c>from-*</c> key found on it is reported and otherwise ignored.
    /// </summary>
    private static List<GhgInfluence> ReadInfluences(
        YamlMappingNode root,
        LineDocument document,
        List<GhgProblem> problems,
        IReadOnlyList<GhgTrend> trends,
        IReadOnlyList<GhgTrigger> triggers)
    {
        var trendIds = trends.Select(trend => trend.Id).ToHashSet(StringComparer.Ordinal);
        var triggerIds = triggers.Select(trigger => trigger.Id).Where(id => !trendIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
        List<GhgInfluence> influences = [];
        foreach (var node in Sequence(root, InfluencesKey))
        {
            if (node is not YamlMappingNode mapping)
            {
                problems.Add(new GhgProblem(LineOf(node, document), "An influence entry is not a mapping and was passed over."));
                continue;
            }

            ReportUnknownKeys(mapping, InfluenceKeys, document, problems, "influence");

            var from = Scalar(mapping, "from") ?? "";
            var fromTrigger = triggerIds.Contains(from);
            if (fromTrigger)
            {
                foreach (var key in new[] { "from-phase", "from-edge", "from-at" })
                {
                    if (Node(mapping, key) is { } stray)
                    {
                        problems.Add(new GhgProblem(LineOf(stray, document), $"`{key}` is ignored on an influence from a trigger, which has no phases; the line is kept."));
                    }
                }
            }

            influences.Add(new GhgInfluence(
                Scalar(mapping, "id") ?? "",
                from,
                fromTrigger ? GhgEnd.None : End(mapping, "from"),
                Scalar(mapping, "to") ?? "",
                End(mapping, "to"),
                Scalar(mapping, "description") ?? "",
                Range(mapping, document)));
        }

        return influences;
    }

    /// <summary>
    /// One end of an influence, verbatim. An unknown phase or edge, or an <c>at</c> that is not a
    /// number, is kept as it is and reported by the rules as <c>ghg.bad-attachment</c> - not here, so
    /// one breach is reported once.
    /// </summary>
    private static GhgEnd End(YamlMappingNode mapping, string prefix)
    {
        var at = Scalar(mapping, $"{prefix}-at");
        return new GhgEnd(
            Scalar(mapping, $"{prefix}-phase") ?? "",
            Scalar(mapping, $"{prefix}-edge") ?? "",
            double.TryParse(at, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null);
    }

    /// <summary>A flow or block sequence of tags. A tags value that is not a sequence is reported.</summary>
    private static List<string> Tags(YamlMappingNode mapping, LineDocument document, List<GhgProblem> problems)
    {
        switch (Node(mapping, "tags"))
        {
            case null:
                return [];
            case YamlSequenceNode sequence:
                return [.. sequence.Children.OfType<YamlScalarNode>().Select(tag => tag.Value ?? "").Where(tag => tag.Length > 0)];
            case YamlScalarNode { Value: null or "" }:
                return [];
            case var other:
                problems.Add(new GhgProblem(LineOf(other, document), "`tags` is not a list of tags; the line is kept."));
                return [];
        }
    }

    /// <summary>A <c>YYYY-MM</c> date, or null. A malformed or missing required date is reported.</summary>
    private static int? Month(YamlMappingNode mapping, string key, LineDocument document, List<GhgProblem> problems, bool required)
    {
        if (Node(mapping, key) is not YamlScalarNode scalar)
        {
            if (required)
            {
                problems.Add(new GhgProblem(LineOf(mapping, document), $"A trend has no `{key}` date; it cannot be drawn."));
            }

            return null;
        }

        var month = GhgScale.ParseMonth(scalar.Value);
        if (month is null)
        {
            problems.Add(new GhgProblem(LineOf(scalar, document), $"`{key}: {scalar.Value}` is not a date written as YYYY-MM."));
        }

        return month;
    }

    /// <summary>A whole number, or <paramref name="fallback"/> and a problem.</summary>
    private static int Integer(YamlMappingNode mapping, string key, int fallback, LineDocument document, List<GhgProblem> problems, bool required = false)
    {
        if (Node(mapping, key) is not YamlScalarNode scalar)
        {
            if (required)
            {
                problems.Add(new GhgProblem(LineOf(mapping, document), $"A trend has no `{key}`; {fallback} was used."));
            }

            return fallback;
        }

        if (int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        problems.Add(new GhgProblem(LineOf(scalar, document), $"`{key}: {scalar.Value}` is not a whole number; {fallback} was used."));
        return fallback;
    }

    private static void ReportUnknownKeys(
        YamlMappingNode mapping,
        string[] known,
        LineDocument document,
        List<GhgProblem> problems,
        string what)
    {
        foreach (var (key, _) in mapping.Children)
        {
            if (key is YamlScalarNode { Value: { } name } scalar && !known.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(new GhgProblem(LineOf(scalar, document), $"`{name}` is not a key this module reads on a {what}; the line is kept."));
            }
        }
    }

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode root, string key) =>
        Node(root, key) is YamlSequenceNode sequence ? sequence.Children : [];

    private static YamlNode? Node(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        Node(mapping, key) is YamlScalarNode scalar ? scalar.Value ?? "" : null;

    private static int LineOf(YamlNode node, LineDocument document) =>
        Math.Clamp((int)node.Start.Line - 1, 0, Math.Max(0, document.Lines.Count - 1));

    /// <summary>The lines an entry occupies - the shared node range, as FDG's.</summary>
    private static LineRange Range(YamlNode node, LineDocument document) => YamlNodeRange.Of(node, document.Lines);
}
