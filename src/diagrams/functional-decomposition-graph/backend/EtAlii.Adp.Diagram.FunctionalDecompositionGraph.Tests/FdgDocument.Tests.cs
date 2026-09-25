using System.Text;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// The `.fdg` document: it reads, it splices, and an unchanged document comes back byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The round trip is compared as BYTES, not as text</b> (Requirement 2.3). A string comparison
/// passes on a document whose CRLF endings became LF, which is the one failure the guarantee
/// exists to prevent, and is invisible to every other test in the suite.
/// </para>
/// <para>
/// <b>The fixtures are the test subject, so `*.fdg -text` is in `.gitattributes`</b> - added before
/// the first fixture was written. Without it git normalises the working tree on checkout, the
/// CRLF and LF fixtures collapse into the same bytes, and this file then measures git's rewriting
/// rather than the writer's output while still passing.
/// </para>
/// </remarks>
public class FdgDocumentTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] Bytes(string name) => File.ReadAllBytes(FixturePath(name));

    private static LineDocument Load(string name) =>
        LineDocument.Parse(File.ReadAllText(FixturePath(name)));

    public static TheoryData<string> EveryFixture =>
    [
        "crlf-line-endings.fdg",
        "lf-line-endings.fdg",
        "no-trailing-newline.fdg",
        "malformed-entries.fdg",
        "not-yaml.fdg",
    ];

    /// <summary>
    /// A document read and written back without an edit is byte-identical.
    /// </summary>
    /// <remarks>
    /// This is the whole of Requirement 2.3 and it passes by construction rather than by care:
    /// nothing serialises the model back, so the only bytes that can change are the ones a splice
    /// touched, and no splice ran here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void AnUnchangedDocument_ComesBackByteForByte(string fixture)
    {
        var original = Bytes(fixture);

        var document = LineDocument.Parse(Encoding.UTF8.GetString(original));
        _ = FdgParser.Parse(document);

        Assert.Equal(original, Encoding.UTF8.GetBytes(document.Text));
    }

    /// <summary>
    /// THE PAIR THE `-text` ATTRIBUTE PROTECTS. If git ever normalises these files, the two
    /// fixtures become the same bytes and every round-trip case above still passes - so this is
    /// the case that notices, and it is the reason the attribute is added before the fixtures.
    /// </summary>
    [Fact]
    public void TheCrlfAndLfFixtures_AreStillDifferentBytes()
    {
        var crlf = Bytes("crlf-line-endings.fdg");
        var lf = Bytes("lf-line-endings.fdg");

        Assert.NotEqual(crlf, lf);
        Assert.Contains("\r\n", Encoding.UTF8.GetString(crlf), StringComparison.Ordinal);
        Assert.DoesNotContain("\r", Encoding.UTF8.GetString(lf), StringComparison.Ordinal);
    }

    [Fact]
    public void TheExample_ReadsEveryEntryTheDesignStates()
    {
        var model = FdgParser.Parse(Load("crlf-line-endings.fdg"));

        Assert.Equal(FdgModel.CurrentVersion, model.Version);
        Assert.Equal(["task-list", "open-task", "note-offline"], model.Elements.Select(element => element.Id));
        Assert.Equal(["c-open"], model.Connections.Select(connection => connection.Id));

        var list = model.Elements[0];
        Assert.Equal(FdgElementTypes.UiElement, list.Type);
        Assert.Equal("Task list", list.Name);
        Assert.Equal(120, list.X);
        Assert.Equal(80, list.Y);
        Assert.Equal(160, list.Width);
    }

    /// <summary>
    /// Four types share one height and never state it; only a Comment carries its own.
    /// </summary>
    [Fact]
    public void OnlyAComment_CarriesItsOwnHeight()
    {
        var model = FdgParser.Parse(Load("crlf-line-endings.fdg"));

        var action = model.Elements.Single(element => element.Id == "open-task");
        Assert.Null(action.Height);
        Assert.Equal(FdgGeometry.SharedHeight, action.DrawnHeight);

        var comment = model.Elements.Single(element => element.Id == "note-offline");
        Assert.Equal(96, comment.Height);
        Assert.Equal(96, comment.DrawnHeight);
    }

    /// <summary>A Comment's block scalar keeps its newlines.</summary>
    [Fact]
    public void ACommentsText_KeepsItsNewlines()
    {
        var model = FdgParser.Parse(Load("crlf-line-endings.fdg"));

        var comment = model.Elements.Single(element => element.Id == "note-offline");
        Assert.Equal(
            "Everything here must work offline.\nSync happens when the device reconnects.",
            comment.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// An unknown key, an unknown type and a Comment with a name all survive, and each is reported.
    /// </summary>
    [Fact]
    public void WhatTheParserDoesNotUnderstand_SurvivesAndIsReported()
    {
        var document = Load("malformed-entries.fdg");
        var model = FdgParser.Parse(document);

        // Every entry is still there - nothing was dropped for being odd.
        Assert.Equal(["good", "odd", "commented"], model.Elements.Select(element => element.Id));

        // And the lines are untouched, which is what "survives" means.
        Assert.Contains("colour: puce", document.Text, StringComparison.Ordinal);

        Assert.Contains(model.Problems, problem => problem.Message.Contains("not an element type", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("`colour` is not a key", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("A Comment has no name", StringComparison.Ordinal));
        Assert.Contains(model.Problems, problem => problem.Message.Contains("is not a number", StringComparison.Ordinal));
    }

    /// <summary>
    /// A document that is not YAML yields a model and a problem rather than an exception - the
    /// deliberate difference from the `.dgr` parser, whose own summary says it throws here.
    /// </summary>
    [Fact]
    public void ADocumentThatIsNotYaml_YieldsAModelAndAProblem()
    {
        var document = Load("not-yaml.fdg");

        var model = FdgParser.Parse(document);

        Assert.Empty(model.Elements);
        Assert.Empty(model.Connections);
        Assert.NotEmpty(model.Problems);
        // The text is the caller's and is kept, so the editing session survives the bad keystroke.
        Assert.Contains("bad-indent", document.Text, StringComparison.Ordinal);
    }

    /// <summary>An edit rewrites only the lines of the entry it changes.</summary>
    /// <remarks>
    /// Compared line by line rather than by length: a splice that replaced the right number of
    /// lines in the wrong place would pass a length check, which is the failure this shape exists
    /// to catch.
    /// </remarks>
    [Fact]
    public void AnEdit_RewritesOnlyItsOwnEntrysLines()
    {
        var document = Load("crlf-line-endings.fdg");
        var before = document.Lines.Select(line => line.Text).ToList();
        var model = FdgParser.Parse(document);

        var moved = model.Elements.Single(element => element.Id == "open-task");
        Assert.True(FdgWriter.MoveTo(document, moved, 300, 400).WasApplied);

        var after = document.Lines.Select(line => line.Text).ToList();
        Assert.Equal(before.Count, after.Count);

        var changed = before.Zip(after).Index()
            .Where(pair => pair.Item.First != pair.Item.Second)
            .Select(pair => pair.Index)
            .ToList();

        Assert.Equal(2, changed.Count);
        Assert.All(changed, line => Assert.InRange(line, moved.Range.Start, moved.Range.End));
        Assert.Contains("x: 300", document.Text, StringComparison.Ordinal);
        Assert.Contains("y: 400", document.Text, StringComparison.Ordinal);

        // And the comment two entries above it is untouched, which is what the range walk-back buys.
        Assert.Contains("# Comments and blank lines survive every edit.", document.Text, StringComparison.Ordinal);
    }

    /// <summary>An edit leaves the document's line endings alone.</summary>
    [Fact]
    public void AnEditOnAnLfDocument_DoesNotIntroduceCarriageReturns()
    {
        var document = Load("lf-line-endings.fdg");
        var model = FdgParser.Parse(document);

        Assert.True(FdgWriter.MoveTo(document, model.Elements[0], 1, 2).WasApplied);

        Assert.DoesNotContain("\r", document.Text, StringComparison.Ordinal);
    }

    /// <summary>The empty document a new diagram starts from.</summary>
    [Fact]
    public void TheEmptyDocument_IsAHeaderAndTwoEmptySections()
    {
        var text = FdgDocumentFactory.EmptyDocument("\n");

        Assert.Equal("functional-decomposition-graph: 1\nelements: []\nconnections: []\n", text);

        var model = FdgParser.Parse(LineDocument.Parse(text));
        Assert.Equal(FdgModel.CurrentVersion, model.Version);
        Assert.Empty(model.Elements);
        Assert.Empty(model.Connections);
        Assert.Empty(model.Problems);
    }

    /// <summary>
    /// A first element added to an empty document opens the flow-empty section into a block one.
    /// </summary>
    [Fact]
    public void AddingToAnEmptyDocument_OpensTheFlowSection()
    {
        var document = LineDocument.Parse(FdgDocumentFactory.EmptyDocument("\n"));
        var model = FdgParser.Parse(document);

        var added = new FdgElement("first", FdgElementTypes.Function, "First", "", "", 10, 20, 100, null, default);
        Assert.True(FdgWriter.AddElement(document, model, added).WasApplied);

        var reread = FdgParser.Parse(LineDocument.Parse(document.Text));
        Assert.Equal(["first"], reread.Elements.Select(element => element.Id));
        Assert.Empty(reread.Problems);
        Assert.DoesNotContain("elements: []", document.Text, StringComparison.Ordinal);
    }

    /// <summary>Removing an element takes the connections that touch it.</summary>
    [Fact]
    public void RemovingAnElement_TakesEveryConnectionTouchingIt()
    {
        var document = Load("crlf-line-endings.fdg");
        var model = FdgParser.Parse(document);

        var target = model.Elements.Single(element => element.Id == "open-task");
        Assert.True(FdgWriter.RemoveElement(document, model, target).WasApplied);

        var reread = FdgParser.Parse(LineDocument.Parse(document.Text));
        Assert.Equal(["task-list", "note-offline"], reread.Elements.Select(element => element.Id));
        Assert.Empty(reread.Connections);
    }

    /// <summary>A width below the minimum is clamped rather than written as asked.</summary>
    [Fact]
    public void AResizeBelowTheMinimum_IsClamped()
    {
        var document = Load("crlf-line-endings.fdg");
        var model = FdgParser.Parse(document);

        Assert.True(FdgWriter.Resize(document, model.Elements[0], 10, null).WasApplied);

        var reread = FdgParser.Parse(LineDocument.Parse(document.Text));
        Assert.Equal(FdgGeometry.MinimumWidth, reread.Elements[0].Width);
    }

    /// <summary>The two edits a Comment does not take, each refused with a sentence.</summary>
    [Fact]
    public void ACommentRefusesAName_AndTheOthersRefuseAHeight()
    {
        var document = Load("crlf-line-endings.fdg");
        var model = FdgParser.Parse(document);
        var comment = model.Elements.Single(element => element.Id == "note-offline");
        var action = model.Elements.Single(element => element.Id == "open-task");

        var named = FdgWriter.SetName(document, comment, "no");
        var heightened = FdgWriter.Resize(document, action, 200, 300);

        Assert.False(named.WasApplied);
        Assert.Contains("Comment", named.Refusal!, StringComparison.Ordinal);
        Assert.False(heightened.WasApplied);
        Assert.Contains("share", heightened.Refusal!, StringComparison.Ordinal);
    }
}
