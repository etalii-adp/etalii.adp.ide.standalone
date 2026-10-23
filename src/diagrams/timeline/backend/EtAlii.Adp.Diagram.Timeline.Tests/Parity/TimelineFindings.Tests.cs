using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests.Parity;

/// <summary>
/// One test per finding of timeline-disl-fbl that was known only by reading (B4 to B7, B9 to B15),
/// and one for the finding the fixtures added (B16):
/// its fixture read or edited through the vendored example binding, and what the binding gives
/// compared with what the transcript records today's code doing. Every difference observed is
/// pinned in <c>Parity/timeline.findings.json</c>, in the manner of the FBL suite's
/// <c>divergences.json</c>: an unlisted difference fails, and so does a listed one that has changed
/// or no longer occurs.
/// </summary>
/// <remarks>
/// <para>
/// The binding is <c>src/backend/EtAlii.Adp.Specification.Fbl.Tests/Conformance/timeline.fbl</c>,
/// linked into this project unchanged. It is read with the attribute types of the bundled
/// definition (a label is a string, a begin and an end a date-time, a row an int), as
/// <see cref="DislAttributeTypes"/> gives them through the definition's <c>typeMap</c>.
/// </para>
/// <para>
/// An edit is compared step for step: the transcript's text before the step is rebuilt by applying
/// the recorded hunks of every earlier step to the fixture, the step's change is planned through
/// the binding on that text, and the two results are compared. A minted id, written
/// <c>{minted-N}</c> in the transcript, is given to the binding as <c>minted-N</c>, which is YAML.
/// </para>
/// </remarks>
public sealed partial class TimelineFindingsTests
{
    private const string FixturesFolder = "diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline.Tests/Fixtures";

    [Fact]
    public void B4_TheFirstElementOfANewTimeline_OpensTheEmptyFlowSequence()
    {
        // Arrange.
        const string fixture = "findings/first-element.tml";

        // Act.
        var observed = Join(
            Step(fixture, "execute timeline.add-element on new:1767225600,2", _ => Add("Period", "minted-1", "New element", "2026-01-01", "2026-01-15", 2)),
            Step(fixture, "execute timeline.add-moment on new:1767312000,4", _ => Add("Moment", "minted-2", "New moment", "2026-01-02", null, 4)));

        // Assert.
        Pinned.Check("B4", fixture, observed);
    }

    [Theory]
    [InlineData("trailing-comment.tml", "tcperiod", "tcconn01")]
    [InlineData("quoted-values.tml", "qvperiod", "qvconn01")]
    public void B5_AChangedValue_KeepsItsTrailingCommentAndItsQuotes(string fixture, string period, string connection)
    {
        // Act.
        var observed = Join(
            Step(fixture, $"set {period} timeline.label = \"Parity element\"", _ => Set(period, "label", "Parity element")),
            Step(fixture, $"set {period} timeline.begin = \"2026-01-05\"", _ => Set(period, "begin", "2026-01-05")),
            Step(fixture, $"set {period} timeline.end = \"2026-02-01\"", _ => Set(period, "end", "2026-02-01")),
            Step(fixture, $"set {period} timeline.row = \"3\"", _ => Set(period, "row", 3)),
            Step(fixture, $"set {connection} timeline.label = \"Parity relation\"", _ => Set(connection, "label", "Parity relation")));

        // Assert.
        Pinned.Check("B5", fixture, observed);
    }

    [Fact]
    public void B6_AGivenEnd_GoesWhereTheBindingsKeyOrderPutsIt()
    {
        // Arrange.
        const string fixture = "give-end.tml";

        // Act.
        var observed = Step(fixture, "commit timeline.give-end on gemoment = \"2100-01-01\"", _ => Set("gemoment", "end", "2100-01-01"));

        // Assert.
        Pinned.Check("B6", fixture, observed);
    }

    [Fact]
    public void B7_ANewEntry_CopiesTheGapAfterTheDash()
    {
        // Arrange.
        const string fixture = "indentation.tml";

        // Act.
        var observed = Step(fixture, "execute timeline.add-element on new:1767225600,2", _ => Add("Period", "minted-5", "New element", "2026-01-01", "2026-01-15", 2));

        // Assert.
        Pinned.Check("B7", fixture, observed);
    }

    /// <summary>
    /// Found by B7's fixture: the tool appends a new element after the last entry of
    /// <c>elements</c>, and the example binding's <c>insert.place</c> is <c>after-last</c>, which
    /// FBL defines as after the last entry of the same rule. They differ once the last entry is a
    /// period and a moment is added, as after the script's retypes of <c>indentation.tml</c>.
    /// </summary>
    [Fact]
    public void B16_ANewMoment_GoesAfterTheLastElement()
    {
        // Arrange.
        const string fixture = "indentation.tml";

        // Act.
        var observed = Step(fixture, "execute timeline.add-moment on new:1767312000,4", _ => Add("Moment", "minted-6", "New moment", "2026-01-02", null, 4));

        // Assert.
        Pinned.Check("B16", fixture, observed);
    }

    [Theory]
    [InlineData("no-header.tml")]
    [InlineData("other-version.tml")]
    public void B9_AMissingOrOtherHeader_RaisesNothing(string fixture)
    {
        // Act.
        var model = Read(fixture);
        var recorded = Recorded(fixture).GetProperty("findings").EnumerateArray().Select(line => line.GetString()!).ToList();
        var added = model.Findings.Select(Line).ToList();
        var observed = added.Count == 0
            ? null
            : $"the transcript records {Findings(recorded)}; the binding reports {string.Join("; ", added)}";

        // Assert.
        Pinned.Check("B9", fixture, observed);
    }

    [Fact]
    public void B10_AKeyWrittenTwice_IsReadWithAWarningWhereTheToolRefusesTheFile()
    {
        // Arrange.
        const string fixture = "findings/duplicate-key.tml";

        // Act.
        var model = Read(fixture);
        var recorded = Recorded(fixture).GetProperty("findings").EnumerateArray().Select(line => line.GetString()!).ToList();
        var unparseable = recorded.Any(line => line.Contains("| timeline.unparseable |", StringComparison.Ordinal));
        var observed = unparseable == model.Unreadable
            ? null
            : $"the transcript records {Findings(recorded)}; the binding reads {Elements(model)} and reports {(model.Findings.Count == 0 ? "nothing" : string.Join("; ", model.Findings.Select(Line)))}";

        // Assert.
        Pinned.Check("B10", fixture, observed);
    }

    [Fact]
    public void B11_AnEndKeyHoldingNothingAMappingOrASequence_DecidesPeriodOrMoment()
    {
        // Arrange.
        const string fixture = "findings/end-shapes.tml";

        // Act.
        var model = Read(fixture);
        var differences = new List<string>();
        foreach ((string id, string type) in RecordedTypes(fixture))
        {
            var read = model.Elements.FirstOrDefault(element => element is { IsRelation: false, IdIsStored: true } && element.Id == id);
            var readType = read switch
            {
                null => "no element",
                { Type: "Period" } => "a period",
                _ => "a moment",
            };
            var recordedType = type == "period" ? "a period" : "a moment";
            if (readType != recordedType)
            {
                differences.Add($"{id}: the transcript {recordedType}, the binding {readType}");
            }
        }

        var unread = model.Findings.Where(finding => finding.Code is FindingCodes.UnreadableEntry or FindingCodes.UnconvertedValue).Select(Line).ToList();
        if (unread.Count > 0)
        {
            differences.Add($"the binding reports {string.Join("; ", unread)}");
        }

        // Assert.
        Pinned.Check("B11", fixture, differences.Count == 0 ? null : string.Join(" | ", differences));
    }

    [Fact]
    public void B12_AMoveOverATimeOnlyDotNetReads_WritesWhatTheToolWrites()
    {
        // Arrange.
        const string fixture = "dotnet-times.tml";

        // Act.
        // The values are the ones the mapper places the moved element at, as the command carries them.
        var observed = Join(
            Step(fixture, "move moveslashes by 1 rows and 0 seconds", _ => Placement("moveslashes", "2026-07-01", "2026-07-04", 1)),
            Step(fixture, "move moveslashes by 0 rows and 86400 seconds", _ => Placement("moveslashes", "2026-07-02", "2026-07-05", 1)),
            Step(fixture, "move movewords by 1 rows and 0 seconds", _ => Placement("movewords", "2026-07-05", null, 2)),
            Step(fixture, "move movewords by 0 rows and 86400 seconds", _ => Placement("movewords", "2026-07-06", null, 2)));

        // Assert.
        Pinned.Check("B12", fixture, observed);
    }

    [Fact]
    public void B13_ARow_IsReadAsTheToolReadsIt()
    {
        // Arrange.
        const string fixture = "rows.tml";

        // Act.
        var model = Read(fixture);
        var differences = new List<string>();
        foreach ((string id, string row) in RecordedRows(fixture, "timeline.row"))
        {
            var read = model.Elements.FirstOrDefault(element => !element.IsRelation && element.Id == id);
            var readRow = read is null ? "no element" : Text(read.Attributes.GetValueOrDefault("row"));
            if (readRow != row)
            {
                differences.Add($"{id}: the transcript row {row}, the binding {readRow}");
            }
        }

        // Assert.
        Pinned.Check("B13", fixture, differences.Count == 0 ? null : string.Join(" | ", differences));
    }

    [Fact]
    public void B14_AMissingBegin_ReadsAsTheEmptyText()
    {
        // Arrange.
        const string fixture = "findings/no-begin.tml";

        // Act.
        var model = Read(fixture);
        var differences = new List<string>();
        foreach ((string id, string begin) in RecordedRows(fixture, "timeline.begin"))
        {
            var read = model.Elements.FirstOrDefault(element => !element.IsRelation && element.Id == id);
            var readBegin = read is null ? "no element" : read.Attributes.TryGetValue("begin", out var value) && value is not null ? TranscriptText.Quoted(Text(value)) : "unset";
            if (readBegin != TranscriptText.Quoted(begin))
            {
                differences.Add($"{id}: the transcript begin {TranscriptText.Quoted(begin)}, the binding {readBegin}");
            }
        }

        // Assert.
        Pinned.Check("B14", fixture, differences.Count == 0 ? null : string.Join(" | ", differences));
    }

    [Fact]
    public void B15_AConnectionsEntryThatIsNotAMapping_IsPassedOver()
    {
        // Arrange.
        const string fixture = "findings/scalar-connection.tml";

        // Act.
        var model = Read(fixture);
        var recorded = RecordedRelations(fixture);
        var added = model.Elements
            .Where(element => element.IsRelation && !(element.IdIsStored && recorded.Contains(element.Id)))
            .Select(element => $"a {element.Type} at line {element.Line} ({(element.IdIsStored ? $"id {element.Id}" : "no stored id")}, from {element.Source ?? "nothing"}, to {element.Target ?? "nothing"})")
            .ToList();
        var observed = added.Count == 0
            ? null
            : $"the transcript records the relations {string.Join(", ", recorded)}; the binding also reads {string.Join("; ", added)}";

        // Assert.
        Pinned.Check("B15", fixture, observed);
    }

    // The binding, its options and the fixtures.

    private static readonly Lazy<FblBinding> _binding = new(LoadBinding);

    private static readonly Lazy<IReadOnlySet<string>> _timeAttributes = new(() => DislAttributeTypes.TimeAttributes(TimelineDefinition.Specification, _binding.Value));

    private static FblBinding LoadBinding()
    {
        var problems = FblDocumentLoader.Load(Path.Combine(AppContext.BaseDirectory, "Parity", "timeline.fbl"), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["timeline"];
    }

    /// <summary>The attribute types of the bundled definition, by the binding's type and attribute.</summary>
    private static readonly Lazy<Func<string, string, string?>> _attributeType = new(() => DislAttributeTypes.Of(TimelineDefinition.Specification));

    private static FblOptions Options(string fixture) => new()
    {
        FileName = fixture,
        AttributeType = _attributeType.Value,
        TimeAttributes = _timeAttributes.Value,
    };

    private static byte[] Bytes(string fixture) =>
        File.ReadAllBytes(RepositoryPaths.Under(["src", .. $"{FixturesFolder}/{fixture}".Split('/')]));

    private static FblModel Read(string fixture) => OpenBody.Open(Bytes(fixture), _binding.Value, Options(fixture)).Model;

    // The transcript.

    private static readonly Lazy<JsonDocument> _transcript = new(() => JsonDocument.Parse(File.ReadAllBytes(new TimelineTranscript().CheckedInPath)));

    private static JsonElement Recorded(string fixture)
    {
        var relative = $"{FixturesFolder}/{fixture}";
        return _transcript.Value.RootElement.GetProperty("documents").EnumerateArray()
            .Single(document => document.GetProperty("document").GetString() == relative);
    }

    private static IEnumerable<(string Id, string Type)> RecordedTypes(string fixture) =>
        Recorded(fixture).GetProperty("payloads").EnumerateArray()
            .Select(line => line.GetString()!.Split(" | "))
            .Where(parts => parts[1].StartsWith("generic/timeline+", StringComparison.Ordinal) && parts[1] != "generic/timeline+connection")
            .Select(parts => (parts[0], parts[1]["generic/timeline+".Length..]));

    /// <summary>Per element, the value its property row of <paramref name="property"/> shows.</summary>
    private static IEnumerable<(string Id, string Value)> RecordedRows(string fixture, string property)
    {
        foreach (var target in Recorded(fixture).GetProperty("rows").EnumerateArray())
        {
            foreach (var row in target.GetProperty("rows").EnumerateArray().Select(line => line.GetString()!.Split(" | ")))
            {
                if (row[0] == property)
                {
                    yield return (target.GetProperty("target").GetString()!, JsonSerializer.Deserialize<string>(row[2])!);
                }
            }
        }
    }

    /// <summary>The ids of the relations the transcript shows rows for.</summary>
    private static List<string> RecordedRelations(string fixture) =>
    [
        .. Recorded(fixture).GetProperty("rows").EnumerateArray()
            .Where(target => target.GetProperty("rows").EnumerateArray().Any(line => line.GetString()!.StartsWith("timeline.from |", StringComparison.Ordinal)))
            .Select(target => target.GetProperty("target").GetString()!),
    ];

    /// <summary>
    /// The step of the transcript's edit script whose <c>do</c> is <paramref name="step"/>, planned
    /// through the binding on the text the transcript had before it: null when the binding writes the
    /// bytes the transcript records, else both, as hunks.
    /// </summary>
    private static string? Step(string fixture, string step, Func<FblModel, ModelChange> change)
    {
        var text = TranscriptText.Decode(Bytes(fixture));
        foreach (var edit in Recorded(fixture).GetProperty("edits").EnumerateArray())
        {
            var after = Replay(text, edit);
            if (edit.GetProperty("do").GetString() != step)
            {
                text = after;
                continue;
            }

            var before = Unmasked(text);
            var expected = Unmasked(after);
            var body = OpenBody.Open(TranscriptText.Encode(before), _binding.Value, Options(fixture));
            var result = body.Change(change(body.Model));
            if (result is PlanResult.Refused refused)
            {
                return $"{step}: the transcript writes {Hunks(before, expected)}; the binding refuses: {refused.Reason}";
            }

            var written = TranscriptText.Decode(body.Bytes);
            return string.Equals(written, expected, StringComparison.Ordinal)
                ? null
                : $"{step}: the transcript writes {Hunks(before, expected)}; the binding writes {Hunks(before, written)}";
        }

        throw new InvalidOperationException($"The transcript of {fixture} has no step '{step}'.");
    }

    [GeneratedRegex(@"^@(\d+) -(\d+) \+(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex HunkPattern();

    [GeneratedRegex(@"\{minted-(\d+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex MintedPattern();

    /// <summary>The text after one recorded step: its hunks applied, each counted in the text before it.</summary>
    private static string Replay(string text, JsonElement edit)
    {
        if (!edit.TryGetProperty("hunks", out var hunks))
        {
            Assert.False(edit.TryGetProperty("summary", out _), "A summarised step cannot be replayed.");
            return text;
        }

        var lines = Lines(text);
        foreach (var hunk in hunks.EnumerateArray().Select(value => HunkPattern().Match(value.GetString()!)).Reverse())
        {
            var start = int.Parse(hunk.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
            var removed = int.Parse(hunk.Groups[2].Value, CultureInfo.InvariantCulture);
            lines.RemoveRange(start, removed);
            lines.InsertRange(start, Lines(JsonSerializer.Deserialize<string>(hunk.Groups[3].Value)!));
        }

        var replayed = string.Concat(lines);
        Assert.Equal(edit.GetProperty("sha256").GetString(), TranscriptText.Sha256(TranscriptText.Encode(replayed)));
        return replayed;
    }

    private static string Unmasked(string text) => MintedPattern().Replace(text, match => $"minted-{match.Groups[1].Value}");

    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                lines.Add(text[start..(index + 1)]);
                start = index + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    /// <summary>How <paramref name="before"/> became <paramref name="after"/>, as the transcript writes it.</summary>
    private static string Hunks(string before, string after)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteChange(before, after);
            json.WriteEndObject();
        }

        var change = JsonDocument.Parse(stream.ToArray()).RootElement;
        if (change.TryGetProperty("hunks", out var hunks))
        {
            return string.Join(" ", hunks.EnumerateArray().Select(hunk => hunk.GetString()));
        }

        return change.TryGetProperty("unchanged", out _) ? "nothing" : change.GetProperty("summary").GetString()!;
    }

    // The changes.

    private static ModelChange.Add Add(string type, string id, string label, string begin, string? end, int row)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = label, ["begin"] = begin };
        if (end is not null)
        {
            attributes["end"] = end;
        }

        attributes["row"] = row;
        return new ModelChange.Add(type, id, attributes);
    }

    private static ModelChange.Set Set(string id, string attribute, object value) =>
        new(id, new Dictionary<string, object?>(StringComparer.Ordinal) { [attribute] = value });

    private static ModelChange.Set Placement(string id, string begin, string? end, int row)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["begin"] = begin };
        if (end is not null)
        {
            attributes["end"] = end;
        }

        attributes["row"] = row;
        return new ModelChange.Set(id, attributes);
    }

    // The observations.

    private static string? Join(params string?[] observations) =>
        observations.Any(observation => observation is not null) ? string.Join(" || ", observations.OfType<string>()) : null;

    private static string Line(Finding finding) => $"{finding.Code} at line {finding.Location?.Line.ToString(CultureInfo.InvariantCulture) ?? "-"}: {finding.Message}";

    private static string Findings(List<string> recorded) => recorded.Count == 0 ? "no finding" : string.Join("; ", recorded);

    private static string Elements(FblModel model) => model.Unreadable ? "nothing, the body unreadable," : $"{model.Elements.Count} elements";

    private static string Text(object? value) => value switch
    {
        null => "unset",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "unset",
    };

    /// <summary><c>Parity/timeline.findings.json</c>: what each finding's test observed, pinned.</summary>
    private static class Pinned
    {
        private static readonly Lazy<IReadOnlyList<(string Finding, string Fixture, string Observed)>> _all = new(Load);

        private static string FilePath => Path.Combine(Path.GetDirectoryName(new TimelineTranscript().CheckedInPath)!, "timeline.findings.json");

        /// <summary>
        /// <paramref name="observed"/> is null when the binding gives what the transcript records,
        /// else the exact difference, which must then be listed with that observation.
        /// </summary>
        public static void Check(string finding, string fixture, string? observed)
        {
            var listed = _all.Value.Where(entry => entry.Finding == finding && entry.Fixture == fixture).ToList();
            if (observed is null)
            {
                Assert.True(listed.Count == 0, $"{finding} no longer occurs on {fixture}; remove it from timeline.findings.json.");
                return;
            }

            Assert.True(listed.Count == 1, $"{finding} occurs on {fixture} and is not listed in timeline.findings.json:\n{observed}");
            Assert.True(listed[0].Observed == observed, $"{finding} on {fixture} has changed.\nListed:   {listed[0].Observed}\nObserved: {observed}");
        }

        private static IReadOnlyList<(string, string, string)> Load()
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(FilePath));
            return
            [
                .. document.RootElement.GetProperty("findings").EnumerateArray()
                    .Select(entry => (entry.GetProperty("finding").GetString()!, entry.GetProperty("fixture").GetString()!, entry.GetProperty("observed").GetString()!)),
            ];
        }
    }
}
