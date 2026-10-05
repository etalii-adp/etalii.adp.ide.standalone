using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Turns what the FBL binding reads from a <see cref="GhgBody"/> into a <see cref="GhgModel"/>,
/// recording which lines declare what.
/// </summary>
/// <remarks>
/// <para>
/// <b>The YAML is read by FBL, not here.</b> The binding (<c>gartner-hype-cycle-graph.fbl</c>) says
/// which entries are trends, triggers, notes and influences and which key holds which attribute; this
/// class only gives the values their module types and reports the ones that do not fit.
/// </para>
/// <para>
/// <b>THIS PARSER NEVER THROWS</b> (Requirement 2.4). An unreadable body yields an empty model and the
/// library's reason with its line. A malformed date or a phase count that is not a number is passed
/// over: the entry is kept with whatever could be read, and a problem is recorded for the validator.
/// </para>
/// <para>
/// <b>Unknown keys are kept and no longer reported.</b> FBL reads past unbound content (FBL §4.1.2) and
/// exposes none of it, so the old "is not a key this module reads" problem has no source here.
/// </para>
/// <para>
/// <b>What breaks a RULE is not a parse problem.</b> A stop before its start, a phase count of 7 or a
/// second influence in one direction all read perfectly well; <see cref="GhgRuleSet"/> reports them
/// under their own rule ids, as it does a duplicate id or a dangling end, which is why the library's
/// own findings for those are not repeated as problems.
/// </para>
/// </remarks>
public static class GhgParser
{
    internal const string HeaderKey = "gartner-hypecycle-graph";
    private const string UnitKey = "unit";

    /// <summary>The library's findings the module's rules already report under their own ids.</summary>
    private static readonly HashSet<string> ReportedByRules =
    [
        FindingCodes.DuplicateId,
        FindingCodes.MissingId,
        FindingCodes.DanglingReference,
        FindingCodes.HeaderMismatch,
    ];

    /// <summary>The trend keys whose attributes hold the stored phase boundaries, in boundary order.</summary>
    private static readonly string[] BoundaryAttributes = ["peakEnd", "troughEnd", "slopeEnd"];

    /// <summary>Reads the document. <b>Never throws.</b></summary>
    public static GhgModel Parse(string text) => Parse(GhgBody.Parse(text));

    /// <summary>Reads the body. <b>Never throws.</b></summary>
    public static GhgModel Parse(GhgBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var model = body.Model;
        if (model.Unreadable)
        {
            var finding = model.Findings.FirstOrDefault();
            return GhgModel.Empty with
            {
                Problems = [new GhgProblem(LineOf(finding), $"The document could not be read as YAML: {YamlMessage(finding)}")],
            };
        }

        List<GhgProblem> problems = [];
        var graph = model.Elements.FirstOrDefault(element => element.Type == "Graph");
        if (graph is null)
        {
            return GhgModel.Empty;
        }

        // Problems in document order per list, as the panel has always listed them: the version, then
        // each list's entries with their unknown keys before their values, and the unit last.
        var version = ReadVersion(body, graph, problems);
        var trends = Entries(body, model, "trend", problems).Select(element => ReadTrend(body, element, problems)).ToList();
        var triggers = Entries(body, model, "trigger", problems).Select(element => ReadTrigger(body, element, problems)).ToList();
        var notes = Entries(body, model, "note", problems).Select(element => ReadNote(body, element, problems)).ToList();
        var influences = ReadInfluences(body, Entries(body, model, "influence", problems), trends, triggers, problems);
        var unit = ReadUnit(body, model.Elements.FirstOrDefault(element => element.Type == "Unit"), problems);

        problems.AddRange(model.Findings
            .Where(finding => !ReportedByRules.Contains(finding.Code))
            .Select(finding => new GhgProblem(LineOf(finding), finding.Message)));

        return new GhgModel(trends, influences, problems, version, unit)
        {
            Triggers = triggers,
            Notes = notes,
        };
    }

    /// <summary>A scalar's text as the module reads it: strings as they are, numbers and booleans as written; null for a list or nothing.</summary>
    internal static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        System.Collections.IEnumerable => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>The top-level <c>unit:</c>, or the month when there is none. An unknown unit is reported on its line and drawn in months.</summary>
    private static GhgTimeUnit ReadUnit(GhgBody body, FblElement? unit, List<GhgProblem> problems)
    {
        if (unit is null)
        {
            return GhgTimeUnit.Month;
        }

        if (GhgTimeUnit.Named(Text(unit.Attributes.GetValueOrDefault("value"))) is { } named)
        {
            return named;
        }

        var names = string.Join(", ", GhgTimeUnit.All.Select(known => known.Name));
        problems.Add(new GhgProblem(body.LinesOf(unit.OwnSpan).Start, $"`{UnitKey}` is not one of {names}; the diagram is drawn in months."));
        return GhgTimeUnit.Month;
    }

    /// <summary>
    /// The entries of one list in document order. One that is not a mapping is reported and passed
    /// over; for every other one its unknown keys are reported before it is read.
    /// </summary>
    private static IEnumerable<FblElement> Entries(GhgBody body, FblModel model, string list, List<GhgProblem> problems)
    {
        var unreadable = list + "-not-a-mapping";
        foreach (var element in model.Elements.Where(element => element.Rule == list || element.Rule == unreadable).OrderBy(element => element.OwnSpan.Start))
        {
            if (element.Rule == unreadable)
            {
                problems.Add(new GhgProblem(body.LinesOf(element.OwnSpan).Start, $"A{(list.StartsWith('i') ? "n" : "")} {list} entry is not a mapping and was passed over."));
                continue;
            }

            ReportUnknownKeys(body, element, list, problems);
            yield return element;
        }
    }

    /// <summary>
    /// The keys an entry carries that this module does not read: the binding's computed
    /// <c>unknownKeys</c>. They survive every edit, as all unbound content does (FBL §4.1.2), and are reported on their own lines.
    /// </summary>
    private static void ReportUnknownKeys(GhgBody body, FblElement element, string what, List<GhgProblem> problems)
    {
        if (element.Attributes.GetValueOrDefault("unknownKeys") is not IEnumerable<object?> keys)
        {
            return;
        }

        var range = body.LinesOf(element.OwnSpan);
        foreach (var key in keys.Select(Text).OfType<string>())
        {
            problems.Add(new GhgProblem(body.KeyLine(range, key), $"`{key}` is not a key this module reads on a {what}; the line is kept."));
        }
    }

    private static int? ReadVersion(GhgBody body, FblElement graph, List<GhgProblem> problems)
    {
        if (!graph.Attributes.TryGetValue("version", out var value) || Text(value) is not { } text)
        {
            problems.Add(new GhgProblem(0, $"The document does not begin with `{HeaderKey}: {GhgModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new GhgProblem(body.KeyLine(new LineRange(0, body.Lines.Count - 1), HeaderKey), $"The version `{text}` is not a number."));
            return null;
        }

        if (version != GhgModel.CurrentVersion)
        {
            problems.Add(new GhgProblem(body.KeyLine(new LineRange(0, body.Lines.Count - 1), HeaderKey), $"This document states version {version}; this module reads version {GhgModel.CurrentVersion}."));
        }

        return version;
    }

    private static GhgTrend ReadTrend(GhgBody body, FblElement element, List<GhgProblem> problems)
    {
        var range = body.LinesOf(element.OwnSpan);
        return new GhgTrend(
            StoredId(element),
            Scalar(element, "name") ?? "",
            Month(body, element, "start", "start", range, problems, required: true),
            Month(body, element, "stop", "stop", range, problems, required: true),
            Integer(body, element, "row", "row", 0, range, problems),
            Integer(body, element, "phases", "phases", GhgPhases.Count, range, problems, required: true),
            [.. BoundaryAttributes.Select((attribute, index) => Month(body, element, attribute, GhgPhases.BoundaryKeys[index], range, problems, required: false))],
            Tags(body, element, range, problems),
            Scalar(element, "description") ?? "",
            range);
    }

    /// <summary>
    /// A trigger. A missing or malformed <c>date</c> is not reported here but by
    /// <c>ghg.trigger-date</c>, so one breach is reported once.
    /// </summary>
    private static GhgTrigger ReadTrigger(GhgBody body, FblElement element, List<GhgProblem> problems)
    {
        var range = body.LinesOf(element.OwnSpan);
        return new GhgTrigger(
            StoredId(element),
            Scalar(element, "name") ?? "",
            GhgScale.ParseMonth(Scalar(element, "date")),
            Integer(body, element, "row", "row", 0, range, problems),
            Tags(body, element, range, problems),
            Scalar(element, "description") ?? "",
            range);
    }

    /// <summary>
    /// A note. A missing or malformed <c>at</c>, <c>width</c> or <c>height</c> is not reported here but
    /// by <c>ghg.note-position</c>.
    /// </summary>
    private static GhgNote ReadNote(GhgBody body, FblElement element, List<GhgProblem> problems)
    {
        var range = body.LinesOf(element.OwnSpan);
        return new GhgNote(
            StoredId(element),
            Scalar(element, "text") ?? "",
            GhgScale.ParseMonth(Scalar(element, "at")),
            Integer(body, element, "row", "row", 0, range, problems),
            Number(element, "width"),
            Number(element, "height"),
            range);
    }

    /// <summary>
    /// The influences. One from a trigger has no <c>from</c> end: it is read as <see cref="GhgEnd.None"/>,
    /// and any <c>from-*</c> key found on it is reported and otherwise ignored.
    /// </summary>
    private static List<GhgInfluence> ReadInfluences(
        GhgBody body,
        IEnumerable<FblElement> entries,
        IReadOnlyList<GhgTrend> trends,
        IReadOnlyList<GhgTrigger> triggers,
        List<GhgProblem> problems)
    {
        var trendIds = trends.Select(trend => trend.Id).ToHashSet(StringComparer.Ordinal);
        var triggerIds = triggers.Select(trigger => trigger.Id).Where(id => !trendIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
        List<GhgInfluence> influences = [];
        foreach (var element in entries)
        {
            var range = body.LinesOf(element.OwnSpan);
            var from = Scalar(element, "from") ?? "";
            var fromTrigger = triggerIds.Contains(from);
            if (fromTrigger)
            {
                foreach ((string attribute, string key) in new[] { ("fromPhase", "from-phase"), ("fromEdge", "from-edge"), ("fromAt", "from-at") })
                {
                    if (element.Attributes.ContainsKey(attribute))
                    {
                        problems.Add(new GhgProblem(body.KeyLine(range, key), $"`{key}` is ignored on an influence from a trigger, which has no phases; the line is kept."));
                    }
                }
            }

            influences.Add(new GhgInfluence(
                StoredId(element),
                from,
                fromTrigger ? GhgEnd.None : End(element, "from"),
                Scalar(element, "to") ?? "",
                End(element, "to"),
                Scalar(element, "description") ?? "",
                range));
        }

        return influences;
    }

    /// <summary>
    /// One end of an influence, verbatim. An unknown phase or edge, or an <c>at</c> that is not a
    /// number, is kept as it is and reported by the rules as <c>ghg.bad-attachment</c>.
    /// </summary>
    private static GhgEnd End(FblElement element, string side)
    {
        var at = Scalar(element, $"{side}At");
        return new GhgEnd(
            Scalar(element, $"{side}Phase") ?? "",
            Scalar(element, $"{side}Edge") ?? "",
            double.TryParse(at, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null);
    }

    /// <summary>The id as written, which is what the module knows an entry by, duplicates included.</summary>
    private static string StoredId(FblElement element) => Scalar(element, "storedId") ?? "";

    /// <summary>A flow or block sequence of tags. A tags value that is not a sequence is reported.</summary>
    private static List<string> Tags(GhgBody body, FblElement element, LineRange range, List<GhgProblem> problems)
    {
        switch (element.Attributes.GetValueOrDefault("tags"))
        {
            case null:
            case "":
                return [];
            case IEnumerable<object?> sequence:
                return [.. sequence.Select(Text).OfType<string>().Where(tag => tag.Length > 0)];
            default:
                problems.Add(new GhgProblem(body.KeyLine(range, "tags"), "`tags` is not a list of tags; the line is kept."));
                return [];
        }
    }

    /// <summary>A <c>YYYY-MM</c> date, or null. A malformed or missing required date is reported.</summary>
    private static int? Month(GhgBody body, FblElement element, string attribute, string key, LineRange range, List<GhgProblem> problems, bool required)
    {
        if (Scalar(element, attribute) is not { } text)
        {
            if (required)
            {
                problems.Add(new GhgProblem(range.Start, $"A trend has no `{key}` date; it cannot be drawn."));
            }

            return null;
        }

        var month = GhgScale.ParseMonth(text);
        if (month is null)
        {
            problems.Add(new GhgProblem(body.KeyLine(range, key), $"`{key}: {text}` is not a date written as YYYY-MM."));
        }

        return month;
    }

    /// <summary>A whole number, or <paramref name="fallback"/> and a problem.</summary>
    private static int Integer(GhgBody body, FblElement element, string attribute, string key, int fallback, LineRange range, List<GhgProblem> problems, bool required = false)
    {
        if (Scalar(element, attribute) is not { } text)
        {
            if (required)
            {
                problems.Add(new GhgProblem(range.Start, $"A trend has no `{key}`; {fallback} was used."));
            }

            return fallback;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        problems.Add(new GhgProblem(body.KeyLine(range, key), $"`{key}: {text}` is not a whole number; {fallback} was used."));
        return fallback;
    }

    /// <summary>A number, or null when it is missing or is not one.</summary>
    private static double? Number(FblElement element, string attribute) =>
        double.TryParse(Scalar(element, attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>A scalar attribute's text: null when it is absent, empty for an empty scalar.</summary>
    private static string? Scalar(FblElement element, string attribute) =>
        element.Attributes.TryGetValue(attribute, out var value) ? Text(value) ?? (value is null ? "" : null) : null;

    /// <summary>The YAML reader's own sentence, without the library's lead-in, which the module words itself.</summary>
    private static string YamlMessage(Finding? finding)
    {
        const string leadIn = "The body is not well-formed YAML: ";
        var message = finding?.Message ?? "it is not well-formed";
        return message.StartsWith(leadIn, StringComparison.Ordinal) ? message[leadIn.Length..] : message;
    }

    private static int LineOf(Finding? finding) => Math.Max(0, (finding?.Location?.Line ?? 1) - 1);
}
