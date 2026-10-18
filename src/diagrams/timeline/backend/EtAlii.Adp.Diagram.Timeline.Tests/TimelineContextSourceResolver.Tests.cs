using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>A selection on the simple timeline, by the contract every element resolver keeps, and what is its own.</summary>
public sealed class TimelineContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly TimelineDocumentStore _store = new(new FileWriter());

    protected override string Example => TimelineDocumentStoreTests.Simple;

    protected override IContextSourceResolver CreateResolver() =>
        new TimelineContextSourceResolver(new DiagramFileRouter(new TimelineOnlyCatalog()), _store, new TimelineElementMapper());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "k7Qv2mXa";

    protected override IReadOnlyList<string> ElementPath => ["Discovery"];

    protected override string ElementType => TimelineElementMapper.PeriodType;

    protected override string Gesture => "rel:k7Qv2mXa->b3Rt9wYz";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a timeline.",
        "That element is no longer on this timeline.",
        "New element",
        "New element");

    /// <summary>A timeline names an element by its <c>label:</c>.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var renamed = Regex.Replace(text, "(?<=label: )(Discovery|Renamed once)", name, RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        return [name];
    }

    [Fact]
    public async Task AConnection_ResolvesToo()
    {
        // Act: both live on the same stream and are selected through the same channel.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("c1Ld4pQn")).Level;

        // Assert.
        Assert.Equal(["gates"], level.RelativePath);
        Assert.Equal(TimelineElementMapper.ConnectionType, level.Detail.Element.ElementType);
    }

    [Fact]
    public async Task ARowPlacement_ResolvesToTheElementAboutToExist()
    {
        // Arrange: a timeline's drop names a time and a whole row.
        var placement = GestureIds.RowPlacement(1_780_000_000, 4);

        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(placement)).Level;

        // Assert.
        Assert.Equal(["New element"], level.RelativePath);
        Assert.Equal(placement, level.Target.ElementId);
    }
}

/// <summary>
/// A catalog carrying only this module's definition, so the router routes the fixture files
/// without the process-wide discovery cache being filled by a test.
/// </summary>
internal sealed class TimelineOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
