using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// A value written bare, without quotes, as Structurizr allows (finding B6), can be set to one that needs quotes: the
/// binding's <c>escape.requote</c> writes the whole argument again between two quotes (FBL 0.15 section 6.3, <i>A
/// string</i>), where the library once wrote inside the bare word and refused the value that would not read back from it.
/// </summary>
public sealed class C4BareValueTests : IDisposable
{
    private const string Model = """
        workspace "Lenient spellings" {
            model {
                User = Person User Bare
                sys = SoftwareSystem "System"
            }
        }
        """;

    private readonly TestFolder _testFolder = TestFolder.Create("c4-bare-value");
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly IHistoryStack _history;

    public C4BareValueTests()
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
    public async Task ABareName_RenamedToTwoWords_IsWrittenQuoted_AndUndoPutsTheBareNameBack()
    {
        // Act.
        var result = await _history.ExecuteAsync(new SetElementNameCommand(_bodyPath, "User", "The \"user\""), TestContext.Current.CancellationToken);
        var renamed = await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken);
        await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Model.Replace("Person User Bare", "Person \"The \\\"user\\\"\" Bare", StringComparison.Ordinal), renamed);
        Assert.Equal(Model, await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABareDescription_IsWrittenQuoted()
    {
        // Act.
        var result = await _history.ExecuteAsync(new SetElementDescriptionCommand(_bodyPath, "User", "Uses it."), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Model.Replace("Person User Bare", "Person User \"Uses it.\"", StringComparison.Ordinal), await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
    }
}
