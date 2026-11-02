using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// An argument a statement does not write yet is written in its own position: with an empty value for each absent
/// argument before it, and before any brace and any comment on the line (c4-disl-fbl task 15, Requirement 5.3).
/// Finding B19 was the comment case: the line surgery appended the argument after the comment, inside it.
/// </summary>
public class C4AppendedArgumentTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("c4-appended-argument");
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IHistoryStack _history;

    private const string Model = """
        workspace "Trailing comments" {
            model {
                s = softwareSystem "System" "A system." {
                    web = container "Web" { # a container with a block and nothing else
                    }
                    api = container "API" "Serves." // no technology yet
                }
                t = softwareSystem "Partner" // no description yet
                s -> t "Calls" // no technology yet
            }
        }
        """;

    public C4AppendedArgumentTests()
    {
        _bodyPath = IoPath.Combine(_testFolder.Path, "model.dsl");
        File.WriteAllText(_bodyPath, Model);
        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_testFolder.Path));
    }

    public void Dispose()
    {
        _services.Dispose();
        _testFolder.Dispose();
    }

    [Fact]
    public async Task ATechnologyForAContainer_IsWrittenBeforeTheCommentOnItsLine()
    {
        // Act.
        var result = await _history.ExecuteAsync(new SetElementTechnologyCommand(_bodyPath, "api", "Go"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("""            api = container "API" "Serves." "Go" // no technology yet""", await ReadAsync());
    }

    [Fact]
    public async Task ATechnologyForARelationship_IsWrittenBeforeTheCommentOnItsLine()
    {
        // Act.
        var relationship = C4Body.Read(Model).Workspace.Relationships.Single();
        var result = await _history.ExecuteAsync(new SetRelationshipTechnologyCommand(_bodyPath, relationship.Id, "HTTPS"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("""        s -> t "Calls" "HTTPS" // no technology yet""", await ReadAsync());
    }

    [Fact]
    public async Task ADescription_IsWrittenBeforeTheComment_AndATechnologyAfterAnEmptyDescription_BeforeTheBrace()
    {
        // Act.
        var described = await _history.ExecuteAsync(new SetElementDescriptionCommand(_bodyPath, "t", "Partners."), TestContext.Current.CancellationToken);
        var technology = await _history.ExecuteAsync(new SetElementTechnologyCommand(_bodyPath, "web", "React"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(described.IsSuccess, described.Error);
        Assert.True(technology.IsSuccess, technology.Error);
        var text = await ReadAsync();
        Assert.Contains("""        t = softwareSystem "Partner" "Partners." // no description yet""", text);
        Assert.Contains("""            web = container "Web" "" "React" { # a container with a block and nothing else""", text);
    }

    private async Task<string> ReadAsync() => (await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
}
