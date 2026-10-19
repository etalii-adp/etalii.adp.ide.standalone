using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What the property grid shows for a C4 selection, and what changing one of those values does.
/// </summary>
/// <remarks>
/// The point of these: a property edit is the same edit as the equivalent context action, on the
/// same history, so a user who prefers the grid and one who prefers the dialog are not using two
/// features that can drift apart.
/// </remarks>
public class C4ContextPropertyProviderTests : IDisposable
{
    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking." {
                    web = container "Web" "Serves pages." "React"
                    batch = container "Batch" "Runs overnight."
                }
                u -> web "Uses" "HTTPS"
            }
            views {
                container s "containers" {
                    include *
                }
            }
        }
        """;

    private readonly string _root;
    private readonly string _bodyPath;
    private readonly ServiceProvider _services;
    private readonly C4ContextPropertyProvider _provider;
    private readonly IHistoryStack _history;

    public C4ContextPropertyProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(_bodyPath, Model);

        _services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        _provider = _services.GetServices<IContextPropertyProvider>().OfType<C4ContextPropertyProvider>().Single();
        _history = _services.GetRequiredService<IHistoryStackStore>().Get(_root);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _services.Dispose();
        TestFolder.TryDelete(_root);
    }

    private ContextTarget Target(string elementId) => new(
        ContextScope.DiagramElement,
        _bodyPath,
        IsContainer: false,
        SourceId: ShortGuid.NewShortGuid(),
        RootPath: _root,
        ElementId: elementId);

    private async Task<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(string elementId) =>
        await _provider.DescribeAsync(Target(elementId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task AContainer_OffersItsName_DescriptionAndTechnology()
    {
        // Act.
        var properties = await DescribeAsync("web");

        // Assert.
        Assert.Equal("Web", properties.Row(C4ContextPropertyProvider.NamePropertyId).Value);
        Assert.Equal("Serves pages.", properties.Row(C4ContextPropertyProvider.DescriptionPropertyId).Value);
        Assert.Equal("React", properties.Row(C4ContextPropertyProvider.TechnologyPropertyId).Value);
    }

    [Fact]
    public async Task ADescription_IsAMultiLineEditor_AndANameIsNot()
    {
        // Act.
        var properties = await DescribeAsync("web");

        // Assert.
        Assert.Equal(ContextPropertyEditor.Text, properties.Row(C4ContextPropertyProvider.DescriptionPropertyId).Editor);
        Assert.Equal(ContextPropertyEditor.Line, properties.Row(C4ContextPropertyProvider.NamePropertyId).Editor);
    }

    [Fact]
    public async Task AContainerWithNoTechnologyWritten_StillOffersTheRow_Empty()
    {
        // Act.
        // The other half of the rule the person test pins: absent from the *kind* means no row
        // at all, but present-and-empty means a row with an empty value. A reader must be able
        // to tell "this kind has no technology" from "nobody has said what this one is", and a
        // sentinel value for either would collapse the two.
        var properties = await DescribeAsync("batch");

        // Assert.
        Assert.Equal("", properties.Row(C4ContextPropertyProvider.TechnologyPropertyId).Value);
    }

    [Fact]
    public async Task APerson_IsOfferedNoTechnology_BecauseC4GivesItNone()
    {
        // Act.
        // Not read-only: absent. Offering an empty box would suggest the model is missing
        // something it is not.
        var properties = await DescribeAsync("u");

        // Assert.
        Assert.DoesNotContain(properties, property => property.Id == C4ContextPropertyProvider.TechnologyPropertyId);
        Assert.Equal("Customer", properties.Row(C4ContextPropertyProvider.NamePropertyId).Value);
    }

    [Fact]
    public async Task TheKindAndIdentifier_AreShownWithAReason_RatherThanHidden()
    {
        // Act.
        var properties = await DescribeAsync("web");

        // Assert.
        var kind = properties.Row(C4ContextPropertyProvider.KindPropertyId);
        Assert.Equal("Container", kind.Value);
        Assert.False(kind.IsEditable);
        Assert.NotEmpty(kind.ReadOnlyReason);
        Assert.Equal("Model", kind.Group);
        Assert.False(properties.Row(C4ContextPropertyProvider.IdentifierPropertyId).IsEditable);
    }

    [Fact]
    public async Task ARelationship_OffersItsDescriptionAndTechnology_AndNamesItsEnds()
    {
        // Arrange.
        var workspace = (await _services.GetRequiredService<IC4DocumentStore>().GetOrLoadAsync(_bodyPath, TestContext.Current.CancellationToken)).Model;
        var relationship = Assert.Single(workspace.Relationships);

        // Act.
        var properties = await DescribeAsync(relationship.Id);

        // Assert.
        Assert.Equal("Uses", properties.Row(C4ContextPropertyProvider.RelationshipDescriptionPropertyId).Value);
        Assert.Equal("HTTPS", properties.Row(C4ContextPropertyProvider.RelationshipTechnologyPropertyId).Value);
        // Named, not identified: "Customer" reads where "u" does not.
        Assert.Equal("Customer", properties.Row(C4ContextPropertyProvider.RelationshipSourcePropertyId).Value);
        Assert.False(properties.Row(C4ContextPropertyProvider.RelationshipDestinationPropertyId).IsEditable);
    }

    [Fact]
    public async Task SettingAName_ChangesTheDocument_AndIsOneUndoAway()
    {
        // Arrange.
        var before = await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken);

        // Act.
        var result = await _provider.SetAsync(
            Target("web"), C4ContextPropertyProvider.NamePropertyId, "Web Application", TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains("container \"Web Application\"", await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // The whole reason properties go through commands rather than writing directly.
        await _history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SettingATechnology_ChangesOnlyTheLineItTouches()
    {
        // Arrange.
        var before = (await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken)).Split('\n');

        // Act.
        await _provider.SetAsync(
            Target("web"), C4ContextPropertyProvider.TechnologyPropertyId, "TypeScript and React", TestContext.Current.CancellationToken);

        // Assert.
        // The `.dsl` belongs to another ecosystem, and a property edit is line surgery on it
        // like every other edit ADP makes.
        var after = (await File.ReadAllTextAsync(_bodyPath, TestContext.Current.CancellationToken)).Split('\n');
        Assert.Equal(before.Length, after.Length);
        Assert.Single(before.Zip(after), pair => pair.First != pair.Second);
    }

    [Fact]
    public async Task SettingAPropertyOfSomethingGone_IsRefusedRatherThanThrown()
    {
        // Act.
        var result = await _provider.SetAsync(
            Target("ghost"), C4ContextPropertyProvider.NamePropertyId, "Anything", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task SettingAPropertyThatIsNotOffered_IsRefused()
    {
        // Act.
        var result = await _provider.SetAsync(
            Target("web"), "c4.invented", "Anything", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("c4.invented", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingIsDescribed_ForASelectionThatIsNotADiagramElement()
    {
        // Arrange.
        var fileTarget = new ContextTarget(
            ContextScope.Hierarchy, _bodyPath, IsContainer: false, SourceId: ShortGuid.NewShortGuid(), RootPath: _root);

        // Act.
        var properties = await _provider.DescribeAsync(fileTarget, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(properties);
    }
}
