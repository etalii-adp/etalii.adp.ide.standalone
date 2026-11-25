using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// Spec 089's worked workspace (<c>Fixtures/links-example/</c>) opens through the served binding (etalii.adp spec 092,
/// T011, SC-001): its document id and every linked node are read, and the definition's rules report nothing.
/// </summary>
public class LinksExampleTests
{
    [Fact]
    public async Task TheServices_ReadItsDocumentIdAndEveryLinkedNode_WithNoFinding()
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "links-example", "services.dgr"), TestContext.Current.CancellationToken);

        // Act.
        var body = DependencyGraphBody.Parse(text);

        // Assert.
        Assert.Equal("s2v5b9hu", body.Model.DocumentId);
        Assert.Empty(body.Model.Findings);
        Assert.Empty(DependencyGraphFindings.Judge(body));
        Assert.Equal(3, body.Disl.Diagram.Elements.Count(element => element.Type.Name == "LinkedNode"));
        Assert.Equal(2, body.Disl.Diagram.Relations.Count);
    }
}
