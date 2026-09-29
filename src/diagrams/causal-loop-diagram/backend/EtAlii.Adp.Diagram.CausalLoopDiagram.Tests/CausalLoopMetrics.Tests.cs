using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// Causal-loop's text sizing measured against the shared text metric's golden fixture
/// (backend-centralization R10.1, R10.2).
/// </summary>
/// <remarks>
/// The metric is shared and read from <c>src/fixtures/cross-tier/text-metric.json</c>, which the client
/// reads too; the padding and minimum around it are this module's own. So each case's expected
/// width is the fixture's text width with this module's own sizing applied around it, and a module
/// that measured its text any other way than the client does would fail here.
/// </remarks>
public class CausalLoopMetricsTests
{
    [Fact]
    public void ATextsWidth_IsTheSharedMetricsWidth_WithThisModulesOwnSizingAroundIt()
    {
        var metrics = CausalLoopMetrics.Default;
        var cases = LoadCasesAt(metrics.FontSize);
        Assert.NotEmpty(cases);

        var failures = new List<string>();
        foreach (var (text, textWidth) in cases)
        {
            var expected = Math.Max(metrics.MinimumWidth, textWidth + (2 * metrics.HorizontalPadding));
            var actual = metrics.WidthOf(text);
            if (Math.Abs(actual - expected) > 1e-9)
            {
                failures.Add($"\"{text}\": sized {actual}, expected {expected}");
            }
        }

        Assert.Empty(failures);
    }

    private static List<(string Text, double Width)> LoadCasesAt(double fontSize)
    {
        var path = IoPath.Combine(LocateRepositoryRoot(), "src", "fixtures", "cross-tier", "text-metric.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(path));
        return fixture.RootElement.GetProperty("cases").EnumerateArray()
            .Where(@case => Math.Abs(@case.GetProperty("fontSize").GetDouble() - fontSize) < double.Tolerance)
            .Select(@case => (@case.GetProperty("text").GetString()!, @case.GetProperty("width").GetDouble()))
            .ToList();
    }

    private static string LocateRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test assembly.");
    }
}
