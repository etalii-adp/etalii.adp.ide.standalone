using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Spec 089's worked workspace (<c>Fixtures/links-example/</c>) opens through the served binding (etalii.adp spec 092,
/// T011, SC-001): its document id and every linked element are read, each named by its link, and nothing is reported.
/// </summary>
public class LinksExampleTests
{
    [Fact]
    public async Task TheRoadmap_ReadsItsDocumentIdAndEveryLinkedElement_WithNoFinding()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "roadmap.tml"), TestContext.Current.CancellationToken);

        // Act.
        var body = TimelineBody.Parse(text);
        var model = TimelineReading.Of(text);

        // Assert.
        Assert.Equal("r6d3w8ne", body.Model.DocumentId);
        Assert.Empty(body.Model.Findings);
        Assert.Equal(
            ["Sign-in", "Checkout", "Beta", "Ask the support team", "Payment gateway"],
            model.Elements.Select(element => element.Label));
        Assert.Equal(4, model.Elements.Count(element => element.Link is not null));
        Assert.Equal(2, model.Connections.Count);
        Assert.Equal(text, body.Text);
    }
}
