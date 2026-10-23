using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// Finding B13, repaired on purpose (Q6): a Comment's text reads back as it was typed, and keeps the style it
/// was written in, through the real store on a real file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seen to fail against the commit before task 10</b>, where <c>FdgWriter</c> wrote the line: it rewrote
/// every text as a literal block scalar, a one-line and a quoted one included, and wrote one whose first line
/// begins with a space with no indentation indicator, so the space was read as indentation and lost.
/// </para>
/// <para>
/// <b>The names of finding B12 are not here.</b> B12 is struck (ruled in chat 2026-10-21): a name
/// <c>[draft]</c>, <c>{a}</c> or <c>*x</c> was already written quoted before task 10, so a guard of it passes
/// against the code it would guard and is no guard; the transcript of <c>names-to-quote.fdg</c> holds them.
/// </para>
/// <para>
/// <b>A literal block whose first line begins with a space is gap 3</b>, recorded and not worked around:
/// <see cref="ABlockText_BeginningWithASpace_IsGap3_AndCannotBeRead"/> holds what the library writes today.
/// </para>
/// </remarks>
public sealed class FdgWrittenAsTypedTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.FdgWrittenAsTypedTests");
    private readonly FdgDocumentStore _store = new(new FileWriter());

    public FdgWrittenAsTypedTests()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "comment-text-forms.fdg"), Body);
    }

    private string Body => _folder.Combine("comment-text-forms.fdg");

    public void Dispose()
    {
        _folder.Dispose();
    }

    /// <summary>
    /// Each case: the Comment, the text typed, and the line it must be written on. <c>t1</c> is written plain and
    /// <c>t2</c> quoted, so each keeps its style; a text beginning with a space keeps the space because the
    /// quoted style holds it as written.
    /// </summary>
    public static TheoryData<string, string, string, string> EveryText => new()
    {
        { "a one-line text", "t1", "Still one line", "    text: Still one line" },
        { "a quoted text", "t2", "Still quoted", "    text: \"Still quoted\"" },
        { "a text whose first line begins with a space", "t2", " leading space", "    text: \" leading space\"" },
    };

    /// <summary>The guard of finding B13: the text reads back as typed, written in the style it had.</summary>
    [Theory]
    [MemberData(nameof(EveryText))]
    public async Task AText_ReadsBackAsTyped_InItsOwnStyle(string @case, string comment, string typed, string line)
    {
        // Act.
        var result = await new FdgTestDispatcher(_store).DispatchAsync(new RenameFdgElementCommand(Body, comment, typed), TestContext.Current.CancellationToken);

        // Assert: written, readable, and the text is what was typed.
        Assert.True(result.IsSuccess, $"{@case}: {result.Error}");
        var text = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        var body = FdgBody.Parse(text);
        Assert.False(body.IsUnreadable, $"{@case}: the document no longer reads:\n{text}");
        var element = Assert.IsType<DislElement>(FdgDefinition.ElementOf(body.Disl.Diagram, comment), exactMatch: false);
        Assert.Equal(typed, FdgDefinition.TextOf(element, "text"));

        // Assert: in its own style, on one line.
        Assert.Contains(line, text.Split("\r\n"));
    }

    /// <summary>
    /// Gap 3, recorded: a literal block whose new first line begins with a space is written with no indentation
    /// indicator, so the body no longer reads. FBL 6.3 says a literal is written one step deeper and does not
    /// mention the indicator, and the library does what the text says. When the language gains the indicator
    /// this case changes, and the gap is closed by that change, not here.
    /// </summary>
    [Fact]
    public async Task ABlockText_BeginningWithASpace_IsGap3_AndCannotBeRead()
    {
        // Act: t3 is written as a literal block.
        var result = await new FdgTestDispatcher(_store).DispatchAsync(new RenameFdgElementCommand(Body, "t3", "  indented\nsecond"), TestContext.Current.CancellationToken);

        // Assert: the library writes the block as the language says, and the body no longer reads.
        Assert.True(result.IsSuccess, result.Error);
        var text = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        Assert.Contains("    text: |-\r\n        indented\r\n      second\r\n", text, StringComparison.Ordinal);
        Assert.True(FdgBody.Parse(text).IsUnreadable);
    }
}
