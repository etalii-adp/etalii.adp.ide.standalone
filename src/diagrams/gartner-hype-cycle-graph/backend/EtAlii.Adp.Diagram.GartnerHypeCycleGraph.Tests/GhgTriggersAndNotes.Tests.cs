using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// ghg-triggers-and-notes task 5: triggers and notes in the <c>.ghg</c> document - read, written as
/// splices, and reported when they break a rule, while a document without them is untouched.
/// </summary>
public class GhgTriggersAndNotesTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static GhgBody Load(string name) => GhgBody.Parse(File.ReadAllText(FixturePath(name)));

    private static GhgModel Reread(GhgBody document) => GhgParser.Parse(GhgBody.Parse(document.Text));

    [Fact]
    public void TriggersAndNotes_ReadEveryKeyTheDesignStates()
    {
        var model = GhgParser.Parse(Load("triggers-and-notes.ghg"));

        Assert.Empty(model.Problems);

        var trigger = Assert.Single(model.Triggers);
        Assert.Equal("transistor-invented", trigger.Id);
        Assert.Equal("Transistor invented", trigger.Name);
        Assert.Equal(GhgScale.MonthIndex(1947, 12), trigger.Date);
        Assert.Equal(1, trigger.Row);
        Assert.Equal(["electronics", "invention"], trigger.Tags);
        Assert.Equal("Bardeen, Brattain and Shockley at Bell Labs.", trigger.Description);

        Assert.Equal(["note-1", "note-2"], model.Notes.Select(note => note.Id));
        var note = model.Notes[0];
        Assert.Equal("Dates are illustrative.\n\nSee the readme.", note.Text);
        Assert.Equal(GhgScale.MonthIndex(1950, 1), note.At);
        Assert.Equal((3, 160d, 64d), (note.Row, note.Width!.Value, note.Height!.Value));

        var fromTrigger = model.Influences.Single(influence => influence.Id == "i-12");
        Assert.True(fromTrigger.FromEnd.IsNone);
        Assert.Equal(new GhgEnd("peak", "top", 0.2), fromTrigger.ToEnd);
    }

    [Fact]
    public void ADocumentWithTriggersAndNotes_BreaksNoRule()
    {
        Assert.Empty(GhgValidator.Validate(Load("triggers-and-notes.ghg")));
    }

    /// <summary>An edit to a document with neither list leaves every line but its own as it was, and opens no list.</summary>
    [Fact]
    public void ADocumentWithNeitherList_IsWrittenExactlyAsBefore()
    {
        var document = Load("lf-line-endings.ghg");
        var before = document.Lines.Select(line => line.Text).ToList();
        var model = GhgParser.Parse(document);

        Assert.True(GhgWriter.SetName(document, model.Trends[0], "Steam power").WasApplied);

        var after = document.Lines.Select(line => line.Text).ToList();
        Assert.Equal(before.Count, after.Count);
        Assert.Single(before.Zip(after), pair => pair.First != pair.Second);
        Assert.DoesNotContain("triggers:", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("notes:", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingATriggerAndANote_OpensEachListOnce_BeforeTheInfluences()
    {
        var document = Load("lf-line-endings.ghg");

        Assert.True(Parity.HandWrittenGhgEdits.AddTrigger(document, GhgParser.Parse(document), new GhgTrigger("t1", "First", GhgScale.MonthIndex(1800, 3), 2, ["x"], "", default)).WasApplied);
        Assert.True(Parity.HandWrittenGhgEdits.AddTrigger(document, GhgParser.Parse(document), new GhgTrigger("t2", "Second", GhgScale.MonthIndex(1810, 3), 2, [], "", default)).WasApplied);
        Assert.True(Parity.HandWrittenGhgEdits.AddNote(document, GhgParser.Parse(document), new GhgNote("n1", "A note", GhgScale.MonthIndex(1820, 1), 0, 160, 64, default)).WasApplied);
        Assert.True(Parity.HandWrittenGhgEdits.AddNote(document, GhgParser.Parse(document), new GhgNote("n2", "Two\nlines", GhgScale.MonthIndex(1830, 1), 1, 160, 64, default)).WasApplied);

        var lines = document.Lines.Select(line => line.Text).ToList();
        Assert.Single(lines, line => line == "triggers:");
        Assert.Single(lines, line => line == "notes:");
        Assert.True(lines.IndexOf("triggers:") < lines.IndexOf("notes:"), "notes went before triggers");
        Assert.True(lines.IndexOf("notes:") < lines.IndexOf("influences:"), "the lists went after the influences");

        var reread = Reread(document);
        Assert.Empty(reread.Problems);
        Assert.Equal(["t1", "t2"], reread.Triggers.Select(trigger => trigger.Id));
        Assert.Equal(["n1", "n2"], reread.Notes.Select(note => note.Id));
        Assert.Equal("Two\nlines", reread.Notes[1].Text);
        Assert.Empty(GhgValidator.Validate(GhgBody.Parse(document.Text)));
    }

    [Fact]
    public void AnInfluenceFromATrigger_IsWrittenWithoutAFromEnd()
    {
        var document = Load("triggers-and-notes.ghg");
        var model = GhgParser.Parse(document);

        var influence = new GhgInfluence("i-new", "transistor-invented", GhgEnd.None, "radio", new GhgEnd("peak", "bottom", 0.5), "", default);
        Assert.True(GhgWriter.AddInfluence(document, model, influence).WasApplied);

        var reread = Reread(document);
        var written = reread.Influences.Single(entry => entry.Id == "i-new");
        Assert.True(written.FromEnd.IsNone);
        Assert.Empty(reread.Problems);
        var range = written.Range;
        Assert.Equal(-1, LineSplice.FindKey(LineDocument.Parse(document.Text), range, "from-phase"));
    }

    /// <summary>A note's text keeps its line breaks through an edit, and goes back to one line when it has none.</summary>
    [Fact]
    public void ANotesText_KeepsItsLineBreaksThroughARoundTrip()
    {
        var document = Load("triggers-and-notes.ghg");
        var note = GhgParser.Parse(document).Notes[1];

        Assert.True(GhgWriter.SetText(document, note, "First line\n  indented second\n\nfourth").WasApplied);
        var reread = Reread(document);
        Assert.Empty(reread.Problems);
        Assert.Equal("First line\n  indented second\n\nfourth", reread.Notes[1].Text);
        Assert.Equal(GhgScale.MonthIndex(1960, 1), reread.Notes[1].At);

        Assert.True(GhgWriter.SetText(document, reread.Notes[1], "A one-line remark").WasApplied);
        Assert.Equal(File.ReadAllText(FixturePath("triggers-and-notes.ghg")), document.Text);
    }

    [Fact]
    public void ReplacingABlockText_RewritesTheWholeBlock()
    {
        var document = Load("triggers-and-notes.ghg");
        var note = GhgParser.Parse(document).Notes[0];

        Assert.True(GhgWriter.SetText(document, note, "Only this.").WasApplied);

        var reread = Reread(document);
        Assert.Empty(reread.Problems);
        Assert.Equal("Only this.", reread.Notes[0].Text);
        Assert.DoesNotContain("See the readme.", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingATrigger_TakesEveryInfluenceFromIt()
    {
        var document = Load("triggers-and-notes.ghg");
        var model = GhgParser.Parse(document);

        Assert.True(Parity.HandWrittenGhgEdits.RemoveTrigger(document, model, model.Triggers[0]).WasApplied);

        var reread = Reread(document);
        Assert.Empty(reread.Triggers);
        Assert.Equal(["i-13"], reread.Influences.Select(influence => influence.Id));
        Assert.Empty(GhgValidator.Validate(GhgBody.Parse(document.Text)));
    }

    [Fact]
    public void ATriggersMoveAndANotesSize_RewriteOnlyTheirOwnKeys()
    {
        var document = Load("triggers-and-notes.ghg");
        var model = GhgParser.Parse(document);

        Assert.True(GhgWriter.SetPlacement(document, model.Triggers[0], GhgScale.MonthIndex(1948, 6), 2).WasApplied);
        Assert.True(GhgWriter.SetSize(document, GhgParser.Parse(GhgBody.Parse(document.Text)).Notes[1], GhgScale.MonthIndex(1959, 1), 5, 200.5, 48).WasApplied);

        var reread = Reread(document);
        Assert.Equal((GhgScale.MonthIndex(1948, 6), 2), (reread.Triggers[0].Date!.Value, reread.Triggers[0].Row));
        Assert.Equal((GhgScale.MonthIndex(1959, 1), 200.5, 48d), (reread.Notes[1].At!.Value, reread.Notes[1].Width!.Value, reread.Notes[1].Height!.Value));
        Assert.Contains("    width: 200.5", document.Text, StringComparison.Ordinal);
    }

    /// <summary>A malformed trigger or note never throws: it is kept, and reported.</summary>
    [Fact]
    public void AMalformedTriggerOrNote_IsKeptAndReported()
    {
        const string text = "gartner-hypecycle-graph: 1\ntrends: []\ntriggers:\n  - just text\n  - id: t\n    name: T\n    date: 1900-01\n    row: two\n    colour: red\nnotes:\n  - id: n\n    text: x\n    at: never\n    row: 0\n    width: wide\n    height: 10\ninfluences: []\n";
        var document = GhgBody.Parse(text);

        var model = GhgParser.Parse(document);

        Assert.Single(model.Triggers);
        Assert.Single(model.Notes);
        Assert.Equal(text, document.Text);
        Assert.Contains(model.Problems, problem => problem.Message.Contains("A trigger entry is not a mapping", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`row: two` is not a whole number", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`colour` is not a key this module reads on a trigger", StringComparison.Ordinal));
        Assert.Contains(GhgValidator.Validate(document), breach => breach.RuleId == GhgRuleIds.NotePosition);
    }

    /// <summary>
    /// A month outside 01 to 12 is no date on any path a document is read through: a trend's start and
    /// stop, a trigger's date and a note's position are each left unread, reported, and not drawn.
    /// </summary>
    [Theory]
    [InlineData("13")]
    [InlineData("00")]
    public void AMonthOutsideOneToTwelve_IsNoDate_WhereverItIsWritten(string month)
    {
        var text = "gartner-hypecycle-graph: 1\n"
            + "trends:\n"
            + $"  - id: a\n    name: A\n    start: 1900-{month}\n    stop: 1910-01\n    row: 0\n    phases: 4\n"
            + $"  - id: b\n    name: B\n    start: 1900-01\n    stop: 1910-{month}\n    row: 1\n    phases: 4\n"
            + $"triggers:\n  - id: t\n    name: T\n    date: 1900-{month}\n    row: 2\n"
            + $"notes:\n  - id: n\n    text: x\n    at: 1900-{month}\n    row: 3\n    width: 10\n    height: 10\n"
            + "influences: []\n";

        var model = GhgParser.Parse(GhgBody.Parse(text));

        Assert.Null(model.Trends[0].Start);
        Assert.Null(model.Trends[1].Stop);
        Assert.Null(Assert.Single(model.Triggers).Date);
        Assert.Null(Assert.Single(model.Notes).At);
        Assert.Contains(model.Problems, problem => problem.Message == $"`start: 1900-{month}` is not a date written as YYYY-MM.");
        Assert.Contains(model.Problems, problem => problem.Message == $"`stop: 1910-{month}` is not a date written as YYYY-MM.");
        var breaches = GhgValidator.Validate(GhgBody.Parse(text));
        Assert.Contains(breaches, breach => breach.RuleId == GhgRuleIds.TriggerDate);
        Assert.Contains(breaches, breach => breach.RuleId == GhgRuleIds.NotePosition);
        Assert.Empty(new GhgElementMapper().Visible(model, DiagramViewport.Unbounded));
    }

    [Fact]
    public void AnIdSharedByATriggerAndATrend_IsReported()
    {
        const string text = "gartner-hypecycle-graph: 1\ntrends:\n  - id: same\n    name: A\n    start: 1900-01\n    stop: 1910-01\n    row: 0\n    phases: 4\ntriggers:\n  - id: same\n    name: T\n    date: 1899-01\n    row: 1\ninfluences: []\n";

        var breach = Assert.Single(GhgValidator.Validate(GhgBody.Parse(text)));

        Assert.Equal(GhgRuleIds.DuplicateId, breach.RuleId);
    }

    [Fact]
    public void FromKeysOnAnInfluenceFromATrigger_AreReportedAndKept()
    {
        var document = Load("triggers-and-notes.ghg");
        var model = GhgParser.Parse(document);
        var influence = model.Influences.Single(entry => entry.Id == "i-12");
        var lines = document.Lines.Select(line => line.Text + line.Ending).ToList();
        lines.Insert(influence.Range.Start + 2, "    from-phase: peak" + lines[0][^(lines[0].EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1)..]);
        document = GhgBody.Parse(string.Concat(lines));

        var reread = Reread(document);

        Assert.True(reread.Influences.Single(entry => entry.Id == "i-12").FromEnd.IsNone);
        Assert.Contains(reread.Problems, problem => problem.Message.Contains("`from-phase` is ignored on an influence from a trigger", StringComparison.Ordinal));
        Assert.Contains("from-phase: peak", document.Text, StringComparison.Ordinal);
    }
}
