using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// SC-003 (spec 029 R18): a document of 100 items and 500 requirements opens, and shows a change another program made,
/// each within two seconds.
/// </summary>
public sealed class LargeDocumentTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    private readonly string _root = Directory.CreateTempSubdirectory("adp-requirement-large-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still be letting go; the temp folder is the system's to clear.
        }
    }

    /// <summary>Ten top items of ten levels each, every item holding five requirements with a description, tags, a category and a state.</summary>
    private static string Generated()
    {
        var text = new StringBuilder("<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\r\n<!-- last: ITEM-0100 REQ-0500 -->\r\n# A large subject\r\n\r\n## Requirements\r\n\r\n");
        var requirement = 0;
        for (var item = 1; item <= 100; item++)
        {
            var depth = (item - 1) % 10;
            var indent = new string(' ', depth * 2);
            text.Append(CultureInfo.InvariantCulture, $"{indent}- ITEM-{item:D4} Part {item} of the subject #part{item % 7}\r\n");
            text.Append(CultureInfo.InvariantCulture, $"{indent}  What part {item} covers, in a sentence.\r\n");
            for (var inItem = 0; inItem < 5; inItem++)
            {
                requirement++;
                text.Append(CultureInfo.InvariantCulture, $"{indent}  - REQ-{requirement:D4} Requirement {requirement} holds. #topic{requirement % 11} category:functional state:proposed\r\n");
                text.Append(CultureInfo.InvariantCulture, $"{indent}    Measured as [REQ-{Math.Max(1, requirement - 1):D4}] is.\r\n");
            }
        }

        return text.Append("\r\n## Assessments\r\n\r\n- [ ] Question REQ-0250: Is this one needed?\r\n").ToString();
    }

    [Fact]
    public async Task ADocumentOf100ItemsAnd500Requirements_Opens_AndShowsAnOutsideChange_EachWithinTwoSeconds()
    {
        // Arrange.
        var body = Path.Combine(_root, "large.md");
        await File.WriteAllTextAsync(body, Generated(), TestContext.Current.CancellationToken);

        // Act: opened.
        var opening = Stopwatch.StartNew();
        await using var page = RequirementPage.Open(body);
        var opened = page.Session.Model();
        opening.Stop();

        // Assert.
        Assert.Equal(100, opened.Nodes.Count(node => node.Role == HierarchySurfaceRoles.Level));
        Assert.Equal(500, opened.Nodes.Count(node => node.Role == HierarchySurfaceRoles.Entry));
        Assert.True(opening.Elapsed < Limit, $"Opening took {opening.Elapsed.TotalMilliseconds:F0} ms.");

        // Act: another program changes one title.
        var showing = Stopwatch.StartNew();
        await File.WriteAllTextAsync(body, Generated().Replace("Requirement 437 holds.", "Requirement 437 holds twice.", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        var shown = await page.ModelAsync(0, model => model.Nodes.Any(node => node.Title == "Requirement 437 holds twice."), Limit);
        showing.Stop();

        // Assert.
        Assert.NotNull(shown);
        Assert.True(showing.Elapsed < Limit, $"Showing the change took {showing.Elapsed.TotalMilliseconds:F0} ms.");
    }
}
