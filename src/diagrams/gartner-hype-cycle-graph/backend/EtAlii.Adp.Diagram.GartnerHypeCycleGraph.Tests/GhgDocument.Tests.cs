using System.Text;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 14: the <c>.ghg</c> document reads, splices, and an unchanged document comes back byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The round trip is compared as BYTES, not as text</b> (Requirement 2.5): a string comparison
/// passes on a document whose CRLF endings became LF.
/// </para>
/// <para>
/// <b>`*.ghg -text` is in `.gitattributes`</b>, added in its own commit before the first fixture, so
/// the CRLF and LF fixtures are still different files after a checkout.
/// </para>
/// </remarks>
public class GhgDocumentTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] Bytes(string name) => File.ReadAllBytes(FixturePath(name));

    private static GhgBody Load(string name) => GhgBody.Parse(File.ReadAllText(FixturePath(name)));

    public static TheoryData<string> EveryFixture =>
    [
        "crlf-line-endings.ghg",
        "lf-line-endings.ghg",
        "no-trailing-newline.ghg",
        "malformed-entries.ghg",
        "not-yaml.ghg",
        "triggers-and-notes.ghg",
        "rule-influence-into-trigger.ghg",
        "rule-trigger-date.ghg",
        "rule-note-position.ghg",
    ];

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void AnUnchangedDocument_ComesBackByteForByte(string fixture)
    {
        var original = Bytes(fixture);

        var document = GhgBody.Parse(Encoding.UTF8.GetString(original));
        _ = GhgParser.Parse(document);

        Assert.Equal(original, Encoding.UTF8.GetBytes(document.Text));
    }

    /// <summary>The pair the `-text` attribute protects: if git normalised them they would be one file.</summary>
    [Fact]
    public void TheCrlfAndLfFixtures_AreStillDifferentBytes()
    {
        var crlf = Bytes("crlf-line-endings.ghg");
        var lf = Bytes("lf-line-endings.ghg");

        Assert.NotEqual(crlf, lf);
        Assert.Contains("\r\n", Encoding.UTF8.GetString(crlf), StringComparison.Ordinal);
        Assert.DoesNotContain("\r", Encoding.UTF8.GetString(lf), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocument_ReadsEveryKeyTheDesignStates()
    {
        var model = GhgParser.Parse(Load("crlf-line-endings.ghg"));

        Assert.Equal(GhgModel.CurrentVersion, model.Version);
        Assert.Empty(model.Problems);
        Assert.Equal(["steam-engine", "railways"], model.Trends.Select(trend => trend.Id));

        var steam = model.Trends[0];
        Assert.Equal("Steam engine", steam.Name);
        Assert.Equal(GhgScale.MonthIndex(1765, 1), steam.Start);
        Assert.Equal(GhgScale.MonthIndex(1900, 1), steam.Stop);
        Assert.Equal(4, steam.Row);
        Assert.Equal(4, steam.Phases);
        Assert.Equal([null, GhgScale.MonthIndex(1800, 1), null], steam.DraggedEnds);
        Assert.Equal(["energy", "industry"], steam.Tags);
        Assert.Equal("Watt's separate condenser onwards.", steam.Description);

        var influence = Assert.Single(model.Influences);
        Assert.Equal(("steam-engine", "railways"), (influence.From, influence.To));
        Assert.Equal(new GhgEnd("plateau", "bottom", 0.3), influence.FromEnd);
        Assert.Equal(new GhgEnd("peak", "top", 0.1), influence.ToEnd);
        Assert.Equal("Locomotion needed a dependable engine first.", influence.Description);
    }

    public static TheoryData<string> MultiLineDescriptions =>
    [
        "Line one\nline two",
        "Line one\r\nline two",
        "Line one\rline two",
    ];

    /// <summary>
    /// A multi-line Description, as the Description row lets a user type, is written so the document
    /// still reads and the same text comes back - not as a second line at column 0, which made the
    /// document unparseable and blanked the canvas.
    /// </summary>
    [Theory]
    [MemberData(nameof(MultiLineDescriptions))]
    public void AMultiLineDescription_RoundTripsAndTheDocumentStillReads(string description)
    {
        var document = Load("crlf-line-endings.ghg");
        var model = GhgParser.Parse(document);

        Assert.True(GhgWriter.SetDescription(document, model.Trends.Single(trend => trend.Id == "steam-engine"), description).WasApplied);
        Assert.True(GhgWriter.SetDescription(document, Assert.Single(model.Influences), description).WasApplied);

        var reread = GhgParser.Parse(GhgBody.Parse(document.Text));

        Assert.Empty(reread.Problems);
        Assert.Equal(["steam-engine", "railways"], reread.Trends.Select(trend => trend.Id));
        Assert.Equal(description, reread.Trends[0].Description);
        Assert.Equal(description, Assert.Single(reread.Influences).Description);
    }

    /// <summary>What the parser does not understand survives, is reported, and never throws.</summary>
    [Fact]
    public void WhatTheParserDoesNotUnderstand_SurvivesAndIsReported()
    {
        var document = Load("malformed-entries.ghg");
        var model = GhgParser.Parse(document);

        Assert.Equal(["good", "odd"], model.Trends.Select(trend => trend.Id));
        Assert.Null(model.Trends[1].Start);
        Assert.Contains("colour: puce", document.Text, StringComparison.Ordinal);
        Assert.Contains("weight: heavy", document.Text, StringComparison.Ordinal);

        Assert.Contains(model.Problems, problem => problem.Message.Contains("is not a date written as YYYY-MM", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`row: two` is not a whole number", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`colour` is not a key", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`weight` is not a key", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("not a mapping", StringComparison.Ordinal));
    }

    [Fact]
    public void ADocumentThatIsNotYaml_YieldsAModelAndAProblem()
    {
        var document = Load("not-yaml.ghg");

        var model = GhgParser.Parse(document);

        Assert.Empty(model.Trends);
        Assert.NotEmpty(model.Problems);
        Assert.Contains("bad-indent", document.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requirement 2.5: an edit rewrites only its entry's lines - and in the same order and quoting as
    /// before, which a writer that re-serialised the document would not keep.
    /// </summary>
    [Fact]
    public void AnEdit_RewritesOnlyItsOwnEntrysLines()
    {
        var document = Load("crlf-line-endings.ghg");
        var before = document.Lines.Select(line => line.Text).ToList();
        var model = GhgParser.Parse(document);

        var railways = model.Trends.Single(trend => trend.Id == "railways");
        Assert.True(GhgWriter.SetSpan(document, railways, railways.Start!.Value + 12, railways.Stop!.Value + 12, 5, [null, null, null]).WasApplied);

        var after = document.Lines.Select(line => line.Text).ToList();
        Assert.Equal(before.Count, after.Count);

        var changed = before.Zip(after).Index()
            .Where(pair => pair.Item.First != pair.Item.Second)
            .Select(pair => pair.Index)
            .ToList();

        Assert.Equal(2, changed.Count);
        Assert.All(changed, line => Assert.InRange(line, railways.Range.Start, railways.Range.End));
        Assert.Contains("start: 1826-09", document.Text, StringComparison.Ordinal);
        Assert.Contains("stop: 1931-01", document.Text, StringComparison.Ordinal);
        Assert.Contains("# Comments and blank lines survive every edit.", document.Text, StringComparison.Ordinal);
        Assert.Contains("  # A trend with even phases.", document.Text, StringComparison.Ordinal);
        Assert.Contains("    tags: [energy, industry]", document.Text, StringComparison.Ordinal);
    }

    /// <summary>A key an edit adds goes where a reader expects it, and the entry's range follows it.</summary>
    [Fact]
    public void ADraggedBoundary_IsWrittenBesideThePhases_AndRemovedWhenCleared()
    {
        var document = Load("lf-line-endings.ghg");
        var railways = GhgParser.Parse(document).Trends.Single(trend => trend.Id == "railways");

        Assert.True(GhgWriter.SetBoundaries(document, railways, [GhgScale.MonthIndex(1840, 1), null, GhgScale.MonthIndex(1900, 1)]).WasApplied);

        var lines = document.Lines.Select(line => line.Text).ToList();
        var phases = lines.IndexOf("    phases: 4", railways.Range.Start);
        Assert.Equal("    peak-end: 1840-01", lines[phases + 1]);
        Assert.Equal("    slope-end: 1900-01", lines[phases + 2]);
        Assert.DoesNotContain("\r", document.Text, StringComparison.Ordinal);

        var reread = GhgParser.Parse(GhgBody.Parse(document.Text)).Trends.Single(trend => trend.Id == "railways");
        Assert.True(GhgWriter.SetBoundaries(document, reread, [null, null, null]).WasApplied);
        Assert.Equal(File.ReadAllText(FixturePath("lf-line-endings.ghg")), document.Text);
    }

    [Fact]
    public void TheEmptyDocument_IsAHeaderAndTwoEmptySections_ThatParseWithNoProblems()
    {
        var text = GhgDocumentFactory.EmptyDocument("\n");

        Assert.Equal("gartner-hypecycle-graph: 1\ntrends: []\ninfluences: []\n", text);

        var model = GhgParser.Parse(GhgBody.Parse(text));
        Assert.Equal(GhgModel.CurrentVersion, model.Version);
        Assert.Empty(model.Trends);
        Assert.Empty(model.Influences);
        Assert.Empty(model.Problems);
        Assert.Empty(GhgValidator.Validate(GhgBody.Parse(text)));
    }

    [Fact]
    public void AddingToAnEmptyDocument_OpensBothFlowSections()
    {
        var document = GhgBody.Parse(GhgDocumentFactory.EmptyDocument("\n"));
        var a = new GhgTrend("a", "A", 22800, 22812, 0, 4, [null, null, null], ["x"], "", default);
        var b = new GhgTrend("b", "B", 22806, 22830, 1, 2, [null, null, null], [], "", default);

        Assert.True(Parity.HandWrittenGhgEdits.AddTrend(document, GhgParser.Parse(document), a).WasApplied);
        Assert.True(Parity.HandWrittenGhgEdits.AddTrend(document, GhgParser.Parse(document), b).WasApplied);
        var influence = new GhgInfluence("ab", "a", new GhgEnd("slope", "bottom", 0.25), "b", new GhgEnd("peak", "top", 0.5), "", default);
        Assert.True(GhgWriter.AddInfluence(document, GhgParser.Parse(document), influence).WasApplied);

        var reread = GhgParser.Parse(GhgBody.Parse(document.Text));
        Assert.Empty(reread.Problems);
        Assert.Equal(["a", "b"], reread.Trends.Select(trend => trend.Id));
        Assert.Equal(new GhgEnd("slope", "bottom", 0.25), Assert.Single(reread.Influences).FromEnd);
        Assert.Empty(GhgValidator.Validate(GhgBody.Parse(document.Text)));
    }

    [Fact]
    public void AddingAnInfluenceWithoutAnInfluencesSection_IsRefusedInTheModulesOwnWords()
    {
        var document = GhgBody.Parse("gartner-hypecycle-graph: 1\ntrends:\n  - id: a\n    name: A\n    start: 1900-01\n    stop: 1920-01\n    row: 0\n    phases: 4\n");
        var influence = new GhgInfluence("aa", "a", new GhgEnd("slope", "bottom", 0.25), "a", new GhgEnd("peak", "top", 0.5), "", default);

        var edit = GhgWriter.AddInfluence(document, GhgParser.Parse(document), influence);

        Assert.Equal("The document has no `influences:` section to add to.", edit.Refusal);
    }

    [Fact]
    public void AddingATriggerWithoutAnInfluencesSection_OpensItsListAtTheEnd()
    {
        var text = "gartner-hypecycle-graph: 1\ntrends:\n  - id: a\n    name: A\n    start: 1900-01\n    stop: 1920-01\n    row: 0\n    phases: 4\n";
        var document = GhgBody.Parse(text);

        var edit = Parity.HandWrittenGhgEdits.AddTrigger(document, GhgParser.Parse(document), new GhgTrigger("t", "T", 22810, 1, [], "", default));

        Assert.True(edit.WasApplied);
        Assert.Equal(text + "triggers:\n  - id: t\n    name: T\n    date: 1900-11\n    row: 1\n", document.Text);
    }

    [Fact]
    public void AddingANoteWithNoText_WritesItsTextEmpty()
    {
        var document = GhgBody.Parse(GhgDocumentFactory.EmptyDocument("\n"));

        var edit = Parity.HandWrittenGhgEdits.AddNote(document, GhgParser.Parse(document), new GhgNote("note", "", 22810, 1, 160, 64, default));

        Assert.True(edit.WasApplied);
        Assert.Contains("  - id: note\n    text: \"\"\n    at: 1900-11\n", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingATrendBeforeYearOne_WritesItsMonthsPlain()
    {
        var document = Load("lf-line-endings.ghg");
        var trend = GhgParser.Parse(document).Trends[0];

        var edit = GhgWriter.SetSpan(document, trend, -3200 * 12, -3180 * 12, trend.Row, [null, null, null]);

        Assert.True(edit.WasApplied);
        Assert.Contains("    start: -3200-01\n    stop: -3180-01\n", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingATrend_TakesEveryInfluenceTouchingIt()
    {
        var document = Load("crlf-line-endings.ghg");
        var model = GhgParser.Parse(document);

        Assert.True(Parity.HandWrittenGhgEdits.RemoveTrend(document, model, model.Trends[1]).WasApplied);

        var reread = GhgParser.Parse(GhgBody.Parse(document.Text));
        Assert.Equal(["steam-engine"], reread.Trends.Select(trend => trend.Id));
        Assert.Empty(reread.Influences);
    }

    /// <summary>Tags are one flow sequence line, quoted only where a flow sequence needs it.</summary>
    [Fact]
    public void Tags_AreWrittenAsOneFlowSequenceLine()
    {
        var document = Load("lf-line-endings.ghg");
        var steam = GhgParser.Parse(document).Trends[0];
        var before = document.Lines.Count;

        Assert.True(GhgWriter.SetTags(document, steam, ["energy", "a, b", "c:d"]).WasApplied);

        Assert.Equal(before, document.Lines.Count);
        Assert.Contains("    tags: [energy, \"a, b\", \"c:d\"]", document.Text, StringComparison.Ordinal);
        Assert.Equal(["energy", "a, b", "c:d"], GhgParser.Parse(GhgBody.Parse(document.Text)).Trends[0].Tags);
    }
}
