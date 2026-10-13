using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>
/// Which selections the providers answer: a behavior model shares <c>.md</c> with every Markdown
/// file, so they tell their own elements by the target's origin, not by the file's extension.
/// </summary>
public sealed class AbmProvidersTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("abm-providers");
    private readonly AbmDocumentStore _store = new();

    public void Dispose() => _folder.Dispose();

    private string Body()
    {
        var body = _folder.Combine("bug-fixer.md");
        File.Copy(AbmExamples.BodyOf("bug-fixer"), body, overwrite: true);
        return body;
    }

    private ContextTarget Target(string body, DiagramOrigin? origin) =>
        ContextTarget.ForElement(body, "1.1", _folder.Path, origin: origin);

    private AbmContextActionProvider Actions => new(new HistoryStackStore(new AbmTestDispatcher(_store)), _store);

    private AbmContextPropertyProvider Properties => new(new HistoryStackStore(new AbmTestDispatcher(_store)), _store);

    [Fact]
    public async Task ANodeOfABehaviorModel_HasItsMenuAndItsRows()
    {
        // Arrange.
        var target = Target(Body(), Diagram.AgentBehaviorModelling.Origin);
        var token = TestContext.Current.CancellationToken;

        // Act.
        var menu = await Actions.DiscoverAsync(target, token);
        var rows = await Properties.DescribeAsync(target, token);

        // Assert.
        Assert.NotEmpty(menu);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task TheSameMarkdownSelectedAsAnotherType_IsAnsweredWithNothing()
    {
        // Arrange: the same file and id, selected inside a diagram of another type.
        var target = Target(Body(), new DiagramOrigin("etalii", "another-type"));
        var token = TestContext.Current.CancellationToken;

        // Act.
        var menu = await Actions.DiscoverAsync(target, token);
        var rows = await Properties.DescribeAsync(target, token);

        // Assert.
        Assert.Empty(menu);
        Assert.Empty(rows);
    }
}
