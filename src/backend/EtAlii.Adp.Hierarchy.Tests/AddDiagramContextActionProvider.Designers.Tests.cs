using EtAlii.Adp.Context;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Adding a designer's document (knowledge-designer Requirements 10.4, 2.1, 2.2, 2.4 and 2.5):
/// the designer type is offered in the Add dialog the diagram types are in, with its formats as
/// the choices under it; choosing one creates exactly two files, the body in that format and
/// the registration naming the type and nothing else; and one undo removes both.
/// </summary>
public class AddDiagramContextActionProviderDesignersTests : IDisposable
{
    private static readonly DiagramDefinition ClassDiagram = new(new DiagramOrigin("fixture", "class"), "Class diagram");

    private static readonly DesignerDefinition Sheet = new(
        "fixture/sheet",
        "Sheet",
        "Rows and columns.",
        "mdi-table",
        [new DesignerFormat("YAML", ".yaml"), new DesignerFormat("JSON", ".json"), new DesignerFormat("XML", ".xml")]);

    /// <summary>Declares formats and registers no template: its module is incomplete.</summary>
    private static readonly DesignerDefinition Unfinished = new("fixture/unfinished", "Unfinished", Formats: [new DesignerFormat("YAML", ".yaml")]);

    /// <summary>Declares no format: nothing of it can be added.</summary>
    private static readonly DesignerDefinition Fixed = new("fixture/fixed", "Fixed");

    private readonly string _root;
    private readonly IHistoryStack _history;
    private readonly AddDiagramContextActionProvider _provider;

    public AddDiagramContextActionProviderDesignersTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out var historyStacks, [Sheet, Unfinished, Fixed]);
        _provider = Provider(historyStacks, [ClassDiagram], new SheetTemplate());
    }

    public void Dispose() => TestFolder.TryDelete(_root);

    private static AddDiagramContextActionProvider Provider(
        IHistoryStackStore historyStacks, DiagramDefinition[] diagrams, params IDesignerDocumentTemplate[] templates)
    {
        var catalog = new DiagramDefinitionCatalog { All = diagrams };
        var designers = new DesignerAddOptions(
            new DesignerDefinitionCatalog { All = [Sheet, Unfinished, Fixed] },
            new DesignerDocumentTemplates(templates));
        return new AddDiagramContextActionProvider(historyStacks, new DiagramDocumentFactories([]), catalog, new DiagramFileRouter(catalog), designers);
    }

    private ContextTarget Folder => new(ContextScope.Hierarchy, _root, IsContainer: true, ShortGuid.NewShortGuid(), RootPath: _root);

    private string[] Listing() => [.. Directory.GetFileSystemEntries(_root).Select(path => IoPath.GetFileName(path)).Order(StringComparer.Ordinal)];

    private ValueTask<ContextCommitResult> Add(string optionId, string name) =>
        _provider.CommitAsync(Folder, AddDiagramContextActionProvider.AddActionId, optionId, name, TestContext.Current.CancellationToken);

    private async Task<IReadOnlyList<ContextOptionNode>> Options()
    {
        var result = await _provider.ExecuteAsync(Folder, AddDiagramContextActionProvider.AddActionId, TestContext.Current.CancellationToken);
        return Assert.IsType<ContextExecutionRequiresChoice>(result).Request.Options;
    }

    // ---- the offer ----------------------------------------------------------------------

    [Fact]
    public async Task TheDesignerType_IsOfferedBesideTheDiagramTypesOfItsVendor_WithItsFormatsUnderIt()
    {
        // Act.
        var options = await Options();

        // Assert: one vendor group holding both families, by label.
        var vendor = Assert.Single(options);
        Assert.Equal("fixture", vendor.Id);
        Assert.Equal(["Class diagram", "Sheet"], vendor.Children!.Select(node => node.Label));

        // The type is a heading; what is chosen is the format.
        var sheet = vendor.Children![1];
        Assert.False(sheet.Selectable);
        Assert.Equal("Rows and columns.", sheet.Description);
        Assert.Equal("mdi-table", sheet.Icon);
        Assert.Equal(["YAML", "JSON", "XML"], sheet.Children!.Select(node => node.Label));
        Assert.Equal([true, true, true], sheet.Children!.Select(format => format.Selectable));
        Assert.Equal(["sheet", "sheet", "sheet"], sheet.Children!.Select(format => format.SuggestedValue));
        Assert.Equal(["", "", ""], sheet.Children!.Select(format => format.NameSuppressedReason));
        Assert.Equal(3, sheet.Children!.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ADesignerTypeWithoutAFormatOrWithoutATemplate_IsNotOffered()
    {
        // Act.
        var options = await Options();

        // Assert: offering what the commit would refuse is the thing not to do.
        var labels = options.SelectMany(vendor => vendor.Children!).Select(node => node.Label).ToList();
        Assert.DoesNotContain("Unfinished", labels);
        Assert.DoesNotContain("Fixed", labels);
    }

    [Fact]
    public async Task TheSuggestedName_AvoidsADocumentThatIsAlreadyThere()
    {
        // Arrange.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "sheet.adp"), "fixture/sheet\r\n", TestContext.Current.CancellationToken);

        // Act.
        var options = await Options();

        // Assert.
        var formats = options[0].Children!.Single(node => node.Label == "Sheet").Children!;
        Assert.All(formats, format => Assert.Equal("sheet-2", format.SuggestedValue));
    }

    [Fact]
    public async Task WithOnlyDesignerTypes_AddIsStillAvailable()
    {
        // Arrange: a host with no diagram type at all.
        TestHistory.Create(_root, out var historyStacks, [Sheet]);
        var provider = Provider(historyStacks, [], new SheetTemplate());

        // Act.
        var groups = await provider.DiscoverAsync(Folder, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(Assert.Single(Assert.Single(groups).Actions).Available);
    }

    // ---- the commit ---------------------------------------------------------------------

    [Theory]
    [InlineData(".yaml", "format: yaml of cities.yaml")]
    [InlineData(".json", "format: json of cities.json")]
    [InlineData(".xml", "format: xml of cities.xml")]
    public async Task AddingInAFormat_CreatesExactlyTheBodyInThatFormatAndItsRegistration(string extension, string body)
    {
        // Act.
        var result = await Add("fixture/sheet@" + extension, "cities");

        // Assert: two files and no third.
        Assert.True(result.Completed, result.Error);
        Assert.Equal(IoPath.Combine(_root, "cities.adp"), result.CreatedFullPath);
        Assert.Equal(["cities.adp", "cities" + extension], Listing());

        // The body is the template of the format that was chosen, and of no other.
        Assert.Equal(body, await File.ReadAllTextAsync(IoPath.Combine(_root, "cities" + extension), TestContext.Current.CancellationToken));

        // The registration identifies and does nothing else: the origin and which file is the
        // body, and no layout (Requirement 2.2).
        Assert.Equal($"fixture/sheet\r\nbody: cities{extension}\r\n", await File.ReadAllTextAsync(IoPath.Combine(_root, "cities.adp"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhatWasAdded_IsRoutedBackToTheDesigner_PastAnotherFileOfItsName()
    {
        // Arrange: a YAML file of the same name was there first, and YAML is the first format.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "cities.yaml"), "mine", TestContext.Current.CancellationToken);
        await Add("fixture/sheet@.json", "cities");
        var router = new DesignerFileRouter(new DesignerDefinitionCatalog { All = [Sheet] });

        // Act.
        var routed = router.Route(IoPath.Combine(_root, "cities.adp"), _root);

        // Assert: the registration finds the body in the format it was created in.
        var designer = Assert.IsType<DesignerRouted>(routed);
        Assert.Equal(IoPath.Combine(_root, "cities.json"), designer.BodyPath);
    }

    [Fact]
    public async Task UndoingTheAdd_RemovesBothFiles_AndRedoBringsBothBack()
    {
        // Arrange.
        await Add("fixture/sheet@.yaml", "cities");

        // Act and assert.
        Assert.True((await _history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Empty(Listing());

        Assert.True((await _history.RedoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(["cities.adp", "cities.yaml"], Listing());
    }

    [Fact]
    public async Task ABodyNameThatIsTaken_IsRefused_AndNothingIsCreated()
    {
        // Arrange: no registration of that name, but a file the body would replace.
        await File.WriteAllTextAsync(IoPath.Combine(_root, "cities.yaml"), "mine", TestContext.Current.CancellationToken);

        // Act.
        var result = await Add("fixture/sheet@.yaml", "cities");

        // Assert.
        Assert.False(result.Completed);
        Assert.Contains("'cities.yaml' already exists", result.Error, StringComparison.Ordinal);
        Assert.Equal(["cities.yaml"], Listing());
        Assert.Equal("mine", await File.ReadAllTextAsync(IoPath.Combine(_root, "cities.yaml"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("fixture/sheet@.csv")]
    [InlineData("fixture/sheet")]
    [InlineData("fixture/unfinished@.yaml")]
    [InlineData("fixture/fixed@.yaml")]
    public async Task AFormatOrTypeThatIsNotOnOffer_IsRefused_AndNothingIsCreated(string optionId)
    {
        // Act.
        var result = await Add(optionId, "cities");

        // Assert.
        Assert.False(result.Completed);
        Assert.Empty(Listing());
    }

    [Fact]
    public async Task AnInvalidName_IsRefused_AndNothingIsCreated()
    {
        // Act.
        var result = await Add("fixture/sheet@.yaml", "sub/cities");

        // Assert.
        Assert.False(result.Completed);
        Assert.Empty(Listing());
    }

    // ---- delete, the inverse and the user's own ------------------------------------------

    [Fact]
    public async Task DeletingTheRegistration_TakesTheBodyWithIt()
    {
        // Arrange.
        await Add("fixture/sheet@.xml", "cities");

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(IoPath.Combine(_root, "cities.adp")), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Listing());
    }

    [Fact]
    public async Task DeletingTheBody_TakesTheRegistrationWithIt()
    {
        // Arrange.
        await Add("fixture/sheet@.xml", "cities");

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(IoPath.Combine(_root, "cities.xml")), TestContext.Current.CancellationToken);

        // Assert: a registration naming nothing serves nobody.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Listing());
    }

    [Fact]
    public async Task DeletingAFileThatOnlySharesAName_LeavesTheDocumentAlone()
    {
        // Arrange: the document is JSON; a YAML file of the same name is somebody else's.
        await Add("fixture/sheet@.json", "cities");
        var other = IoPath.Combine(_root, "cities.yaml");
        await File.WriteAllTextAsync(other, "mine", TestContext.Current.CancellationToken);

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(other), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["cities.adp", "cities.json"], Listing());
    }

    /// <summary>A template whose content says which format and file it was asked for.</summary>
    private sealed class SheetTemplate : IDesignerDocumentTemplate
    {
        public string Origin => Sheet.Origin;

        public string? Create(DesignerFormat format, string fileName) =>
            Sheet.Formats.Contains(format) ? $"format: {format.Extension[1..]} of {fileName}" : null;
    }
}
