using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// An instance has no description in C4, as it has no name: it takes what it is an instance of, its deployment groups
/// and its tags. The line surgery wrote a description into the deployment groups' place; the library holds no such
/// attribute, and the command refuses it in C4's words (c4-disl-fbl task 15, the instance-name defect of task 14 found
/// again for the description).
/// </summary>
public sealed class C4InstanceDescriptionTests : IDisposable
{
    private const string Model = """
        workspace "Deployed" {
            model {
                s = softwareSystem "System" "A system." {
                    web = container "Web" "Serves." "React"
                }
                deploymentEnvironment "Live" {
                    node = deploymentNode "Server" "A box." "Linux" {
                        instance = containerInstance web
                        systemInstance = softwareSystemInstance s
                    }
                }
            }
        }
        """;

    private readonly TestFolder _testFolder = TestFolder.Create("c4-instance-description");
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IHistoryStack _history;

    public C4InstanceDescriptionTests()
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

    [Theory]
    [InlineData("instance", "containerinstance")]
    [InlineData("systemInstance", "softwaresysteminstance")]
    public async Task ADescriptionForAnInstance_IsRefusedInC4sWords_AndWritesNothing(string elementId, string kind)
    {
        // Act.
        var result = await _history.ExecuteAsync(new SetElementDescriptionCommand(_bodyPath, elementId, "Described."), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal($"A {kind} has no description in C4; it stands for what it is an instance of.", result.Error);
        Assert.Equal(Model, await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
    }
}
