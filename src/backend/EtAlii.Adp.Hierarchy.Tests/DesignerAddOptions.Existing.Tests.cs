using EtAlii.Adp.Context;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Adding a document of a designer type recognised by its envelope (spec 029 FR-003, FR-005, R5): a file of that name
/// that carries the envelope is accepted and opened as it is; one without it is refused, naming the line it needs; and a
/// new name writes the body alone, the registration waiting for the first setting.
/// </summary>
public class DesignerAddOptionsExistingTests : IDisposable
{
    private const string OptionId = RequirementsDesigner.Origin + "@.md";

    private readonly string _root;
    private readonly IHistoryStack _history;
    private readonly AddDiagramContextActionProvider _provider;

    public DesignerAddOptionsExistingTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out var historyStacks, [RequirementsDesigner.Definition]);
        var catalog = new DiagramDefinitionCatalog { All = [] };
        var designers = new DesignerAddOptions(
            new DesignerDefinitionCatalog { All = [RequirementsDesigner.Definition] },
            new DesignerDocumentTemplates([new RequirementsTemplate()]));
        _provider = new AddDiagramContextActionProvider(historyStacks, new DiagramDocumentFactories([]), catalog, new DiagramFileRouter(catalog), designers);
    }

    public void Dispose() => TestFolder.TryDelete(_root);

    private ContextTarget Folder => new(ContextScope.Hierarchy, _root, IsContainer: true, ShortGuid.NewShortGuid(), Root: ProjectRoot.From(_root));

    private string[] Listing() => [.. Directory.GetFileSystemEntries(_root).Select(path => IoPath.GetFileName(path)).Order(StringComparer.Ordinal)];

    private ValueTask<ContextCommitResult> Add(string name) =>
        _provider.CommitAsync(Folder, AddDiagramContextActionProvider.AddActionId, OptionId, name, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnExistingFileThatCarriesTheEnvelope_IsAccepted_AndNothingIsWritten()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "plan.md");
        var body = RequirementsDesigner.Body("Mine") + "- ITEM-0001 Kept\r\n";
        await File.WriteAllTextAsync(path, body, TestContext.Current.CancellationToken);

        // Act.
        var result = await Add("plan");

        // Assert: it opens; no byte of it and no registration is written.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(path, result.CreatedFullPath);
        Assert.Equal(["plan.md"], Listing());
        Assert.Equal(body, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnExistingFileWithoutTheEnvelope_IsRefused_NamingTheLineItNeeds()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "plan.md");
        await File.WriteAllTextAsync(path, "# Plan\r\n", TestContext.Current.CancellationToken);

        // Act.
        var result = await Add("plan");

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("first line", result.Error, StringComparison.Ordinal);
        Assert.Contains(RequirementsDesigner.Envelope, result.Error, StringComparison.Ordinal);
        Assert.Equal(["plan.md"], Listing());
        Assert.Equal("# Plan\r\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ANewName_WritesTheBodyAlone_WithNoRegistration()
    {
        // Act.
        var result = await Add("plan");

        // Assert.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(IoPath.Combine(_root, "plan.md"), result.CreatedFullPath);
        Assert.Equal(["plan.md"], Listing());
        Assert.Equal(RequirementsDesigner.Body("plan"), await File.ReadAllTextAsync(IoPath.Combine(_root, "plan.md"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UndoingTheAddOfANewName_RemovesTheBody()
    {
        // Arrange.
        await Add("plan");

        // Act.
        var undone = await _history.UndoAsync(TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(undone.IsSuccess, undone.Error);
        Assert.Empty(Listing());
    }
}
