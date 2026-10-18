using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// What makes a C4 element or relationship selectable. What every element resolver must do is the
/// shared contract's (<see cref="DiagramElementSourceResolverContract"/>), on the courier example and
/// through a registration of the context type; the facts below are this module's own.
/// </summary>
public sealed class C4ContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly C4DocumentStore _store = new(new FileWriter());
    private readonly C4ElementMapper _mapper = new(C4Metrics.Default, new C4LayoutSidecar());

    protected override string Example => C4DocumentStoreTests.Courier;

    protected override IContextSourceResolver CreateResolver() => Resolver();

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "tracking";

    protected override IReadOnlyList<string> ElementPath => ["Courier Tracking"];

    protected override string ElementType => C4ElementMapper.NodeType;

    protected override string Gesture => GestureIds.Relation("sender", "tracking");

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a C4 diagram.",
        "That element is no longer in this diagram.",
        "New element",
        "New relationship");

    /// <summary>A selection is made in a registration of the context type, and the target carries the body beside it.</summary>
    protected override string SelectedIn(string body)
    {
        var registration = IoPath.ChangeExtension(body, ".adp");
        File.WriteAllText(registration, $"c4/context\r\nbody: {IoPath.GetFileName(body)}\r\n");
        return registration;
    }

    /// <summary>A software system is named by the first quoted text on its line, and keeps its id through a rename.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var renamed = Regex.Replace(text, "(?<=^\\s*tracking = softwareSystem \")[^\"]*", name, RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        return [name];
    }

    private C4ContextSourceResolver Resolver() =>
        new(new ToolOrigin("c4", "context"), new DiagramFileRouter(new C4ExamplesStubCatalog()), _store, _mapper);

    [Fact]
    public async Task ALineOnlyAViewElevates_IsSelectable_AsTheCanvasDrewIt()
    {
        // Arrange: a line a view draws to the nearest shown ancestor of an end it does not show, so
        // its id is in no relationship of the model. The first such line of the first view that has
        // one, which is the view the resolver takes it from.
        using var folder = TestFolder.Create("c4-resolver");
        var body = folder.Combine("courier.dsl");
        File.Copy(Example, body);
        var registration = SelectedIn(body);
        var workspace = _store.GetOrLoad(body).Model;
        var modelIds = workspace.Relationships.Select(relationship => relationship.Id).ToHashSet(StringComparer.Ordinal);
        var elevated = workspace.Views
            .SelectMany(view => _mapper.Visible(workspace, view, DiagramViewport.Unbounded))
            .First(element => element.Type == C4ElementMapper.RelationshipType && !modelIds.Contains(element.Id));

        // Act.
        var resolution = await Resolver().ResolveAsync(
            ShortGuid.NewShortGuid(), folder.Path, new ContextSource { ElementId = new ElementId { Value = elevated.Id } }, [], FileLevel(registration),
            TestContext.Current.CancellationToken);

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(C4ElementMapper.RelationshipType, level.Detail.Element.ElementType);
        Assert.Equal(elevated.Payload.ToArray(), level.Detail.Element.Payload.Value.ToByteArray());
        Assert.Contains(" -> ", level.Detail.Element.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnElementThatContainsOthers_SaysSo()
    {
        // Arrange.
        using var folder = TestFolder.Create("c4-resolver");
        var body = folder.Combine("courier.dsl");
        File.Copy(Example, body);
        var registration = SelectedIn(body);

        // Act.
        var system = await ResolveAsync(folder, registration, "tracking");
        var person = await ResolveAsync(folder, registration, "courier");

        // Assert.
        Assert.True(system.Detail.Element.HasChildren);
        Assert.False(person.Detail.Element.HasChildren);
    }

    private async Task<ContextResolvedLevel> ResolveAsync(TestFolder folder, string registration, string elementId) =>
        Assert.IsType<ResolvedContextLevel>(await Resolver().ResolveAsync(
            ShortGuid.NewShortGuid(), folder.Path, new ContextSource { ElementId = new ElementId { Value = elementId } }, [], FileLevel(registration),
            TestContext.Current.CancellationToken)).Level;

    private static ContextResolvedLevel FileLevel(string path) =>
        new(
            new ContextSource(),
            [IoPath.GetFileName(path)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, path, IsContainer: false, SourceId: default, IoPath.GetDirectoryName(path)!, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);
}
