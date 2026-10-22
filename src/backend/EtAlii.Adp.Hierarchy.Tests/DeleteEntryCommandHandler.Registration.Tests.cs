using EtAlii.Adp.Designer;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Deleting either file of a designer's document (spec 029 FR-004, R4): the registration of a type that creates it on
/// first placement goes alone and the body stays; a registration of any other designer type takes its body with it; and
/// a body always takes its registration, so that no registration names a file that is gone.
/// </summary>
public class DeleteEntryCommandHandlerRegistrationTests : IDisposable
{
    /// <summary>A designer type recognised by its registration alone, as the Knowledge designer is.</summary>
    private static readonly DesignerDefinition Sheet = new("fixture/sheet", "Sheet", Formats: [new DesignerFormat("YAML", ".yaml")]);

    private readonly string _root;
    private readonly IHistoryStack _history;

    public DeleteEntryCommandHandlerRegistrationTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out _, [RequirementsDesigner.Definition, Sheet]);
    }

    public void Dispose() => TestFolder.TryDelete(_root);

    private string[] Listing() => [.. Directory.GetFileSystemEntries(_root).Select(path => IoPath.GetFileName(path)).Order(StringComparer.Ordinal)];

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<CommandResult> Delete(string path) => _history.ExecuteAsync(new DeleteEntryCommand(path), TestContext.Current.CancellationToken);

    [Fact]
    public async Task DeletingTheRegistrationOfATypeThatCreatesItOnFirstPlacement_KeepsTheBody()
    {
        // Arrange.
        Write("plan.md", RequirementsDesigner.Body());
        var registration = Write("plan.adp", RequirementsDesigner.Origin + "\r\n");

        // Act.
        var result = await Delete(registration);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["plan.md"], Listing());
        Assert.Equal(RequirementsDesigner.Body(), await File.ReadAllTextAsync(IoPath.Combine(_root, "plan.md"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingTheRegistrationOfAnotherDesignerType_TakesItsBodyWithIt()
    {
        // Arrange.
        Write("cities.yaml", "rows: []\n");
        var registration = Write("cities.adp", "fixture/sheet\r\nbody: cities.yaml\r\n");

        // Act.
        var result = await Delete(registration);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Listing());
    }

    [Fact]
    public async Task DeletingAnEnvelopedBody_TakesItsRegistrationWithIt()
    {
        // Arrange.
        var body = Write("plan.md", RequirementsDesigner.Body());
        Write("plan.adp", RequirementsDesigner.Origin + "\r\n");

        // Act.
        var result = await Delete(body);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Listing());
    }
}
