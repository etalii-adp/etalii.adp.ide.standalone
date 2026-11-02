using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// One value changed is only its bytes changed, so a statement holding a comment is never written again whole (spec 071,
/// T012, Requirement 5.2): every value of every element on such a line of the corpus, changed, is planned without a
/// <c>re-emit-line</c>.
/// </summary>
public sealed class WardleyCommentedStatementsTests
{
    public static TheoryData<string> Names() => WardleyCorpus.Names();

    [Theory]
    [MemberData(nameof(Names))]
    public async Task NoEditOfAStatementHoldingAComment_IsPlannedAsReEmitLine(string name)
    {
        // Act.
        var written = await WrittenAsync(name);

        // Assert.
        Assert.DoesNotContain(written, splice => splice.Operation == SpliceOperation.ReEmitLine);
    }

    [Fact]
    public async Task TheCorpusHasCommentedStatements_SoTheTheoryWritesSomething()
    {
        // Act.
        var written = await WrittenAsync(WardleyCorpus.Names().Select(row => row.Data).First(name => name.EndsWith("comments-everywhere.owm", StringComparison.Ordinal)));

        // Assert.
        Assert.True(written.Count >= 5, $"{written.Count} splices were written on lines with a comment.");
    }

    /// <summary>The splices every changed value of every element on a commented line of <paramref name="name"/> is planned as, each change on its own.</summary>
    private static async Task<List<Splice>> WrittenAsync(string name)
    {
        var document = new WardleyTranscript().Corpus().Single(candidate => candidate.Name == name);
        var lines = LineDocument.Parse(Encoding.UTF8.GetString(await document.Read(TestContext.Current.CancellationToken)));
        var written = new List<Splice>();
        foreach (var element in WardleyBody.Edit(lines, []).Reading.Elements.Where(element => HoldsAComment(lines, element.Line)))
        {
            foreach (var (attribute, value) in element.Attributes)
            {
                if (Changed(value) is not { } changed) continue;
                var edit = WardleyBody.Edit(lines, []);
                if (edit.Change(new ModelChange.Set(element.Id, new Dictionary<string, object?>(StringComparer.Ordinal) { [attribute] = changed })).WasApplied)
                {
                    written.AddRange(edit.Splices);
                }
            }
        }

        return written;
    }

    /// <summary>Whether line <paramref name="number"/> holds a <c>//</c> comment, rather than the <c>//</c> of an address.</summary>
    private static bool HoldsAComment(LineDocument lines, int number)
    {
        if (number < 1 || number > lines.Lines.Count) return false;
        var line = lines.Lines[number - 1].Text;
        for (var at = line.IndexOf("//", StringComparison.Ordinal); at >= 0; at = line.IndexOf("//", at + 2, StringComparison.Ordinal))
        {
            if (at == 0 || line[at - 1] != ':') return true;
        }

        return false;
    }

    /// <summary>Another value of the same kind, or null for a value this guard does not change.</summary>
    private static object? Changed(object? value) => value switch
    {
        double number => number < 0.5d ? number + 0.1d : number - 0.1d,
        long number => number + 1,
        string { Length: > 0 } text when !text.Contains("//", StringComparison.Ordinal) => text + " Changed",
        _ => null,
    };
}
