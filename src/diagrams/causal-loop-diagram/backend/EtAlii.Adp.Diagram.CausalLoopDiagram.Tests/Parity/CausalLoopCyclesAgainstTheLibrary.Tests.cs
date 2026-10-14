using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The library's cycles against the tool's, over the parity corpus (causal-loop-disl-fbl Requirement 7.4,
/// design L1): DISL 12.4's <c>diagram.cycles</c> as <c>EtAlii.Adp.Specification.Disl</c> implements it,
/// and the definition's plugin function <c>causalCycles</c>, which is the module's <see cref="CycleFinder"/>
/// (<see cref="CausalLoopCycles"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The two find the same cycles and differ in their order.</b> On every document of the corpus the
/// cycles are the same, and on two of them the order is not: the library sorts the successors of a
/// variable by model order, the tool walks them in the order the links are declared. The order decides
/// the order of the unlabelled-loop findings and which cycle Claim a loop picks, so the module's finder
/// stays as the plugin function and the difference is a finding for etalii.adp (the definition's
/// companion, "Other differences: the order of cycles").
/// </para>
/// <para>
/// <b>Which documents differ is pinned</b>, so that a corpus document that starts or stops differing is
/// seen: the day the two orders agree everywhere, the module's finder can go (Requirement 7.4).
/// </para>
/// </remarks>
public class CausalLoopCyclesAgainstTheLibraryTests
{
    private const int Bound = 1000;

    /// <summary>The documents of the corpus on which the two orders differ.</summary>
    private static readonly string[] OrderDiffers = ["lenient-spellings.cld", "weight-forms.cld"];

    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task TheLibrary_FindsTheToolsCycles_AndTheirOrderDiffersOnlyWhereNamed(string relative)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);
        var diagram = CausalLoopBody.Parse(text).Disl.Diagram;

        // Act.
        (IReadOnlyList<IReadOnlyList<DislElement>> found, bool truncated) = diagram.Cycles("CausalLink", Bound);
        var library = found.Select(Text).ToList();
        var tool = CausalLoopCycles.Find(diagram.NodesOfType("Variable"), diagram.RelationsOfType("CausalLink"), Bound)
            .Select(cycle => Text([.. ((IEnumerable<object?>)cycle!).Cast<DislElement>()]))
            .ToList();

        // Assert.
        Assert.False(truncated, relative);
        Assert.Equal(tool.Order(StringComparer.Ordinal), library.Order(StringComparer.Ordinal));
        Assert.Equal(OrderDiffers.Contains(Path.GetFileName(relative)), !tool.SequenceEqual(library, StringComparer.Ordinal));
    }

    [Fact]
    public async Task OnWeightForms_TheLibrarySortsWhereTheToolReportsAsItFinds()
    {
        // Arrange.
        var relative = CausalLoopTranscript.Corpus().Single(path => path.EndsWith("/weight-forms.cld", StringComparison.Ordinal));
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);
        var diagram = CausalLoopBody.Parse(text).Disl.Diagram;

        // Act.
        var library = diagram.Cycles("CausalLink", Bound).Cycles.Select(Text).ToList();

        // Assert: DISL 12.4's order, by first member and then by member order.
        Assert.Equal(["a → b", "a → b → c", "a → b → c → d", "a → b → d", "a → c", "a → c → d", "a → c → d → b", "b → c → d", "b → d"], library);
    }

    private static string Text(IReadOnlyList<DislElement> cycle) =>
        string.Join(" → ", cycle.Select(variable => variable.Attributes.GetValueOrDefault("name") as string ?? ""));
}
