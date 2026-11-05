using System.Globalization;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// The two sentences of finding A15 write their number the same on every machine
/// (functional-decomposition-graph-disl-fbl Requirement 3.2): a width of 12.5 reads "12.5" where the
/// decimal separator is a comma too.
/// </summary>
public sealed class FdgCultureFreeSentencesTests
{
    [Fact]
    public async Task ASizeBelowItsMinimum_IsWrittenWithAPoint_UnderACommaCulture()
    {
        // Arrange.
        var path = Path.Combine(RepositoryPaths.Under("src", "diagrams", "functional-decomposition-graph"), "backend", "EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests", "Fixtures", "width-fraction.fdg");
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var request = new DiagramValidationRequest(text, "width-fraction", ProjectRoot.From(Path.GetDirectoryName(path)!), path, null);
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
        IReadOnlyList<DiagramProblem> problems;
        try
        {
            // Act.
            problems = await new FdgValidator().ValidateAsync(request, TestContext.Current.CancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        // Assert.
        Assert.Equal(
            ["`w` is 12.5 wide; the minimum is 80.", "`c` is 30.5 tall; a Comment's minimum is 48."],
            problems.Select(problem => problem.Message));
    }
}
