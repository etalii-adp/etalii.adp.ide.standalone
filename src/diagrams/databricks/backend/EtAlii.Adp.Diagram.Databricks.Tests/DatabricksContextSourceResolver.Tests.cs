using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>A selection on the lakehouse bundle, by the contract every element resolver keeps, and what is its own.</summary>
public sealed class DatabricksContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly DatabricksDocumentStore _store = new();

    private static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    protected override string Example => Fixture("bundle.yml");

    protected override IContextSourceResolver CreateResolver() => Resolver(ServiceCollectionAddDatabricksExtension.BundleOrigin);

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "bundle";

    protected override IReadOnlyList<string> ElementPath => ["lakehouse-nightly"];

    protected override string ElementType => DatabricksElementMapper.BundleType;

    protected override string Gesture => "rel:bundle->resource:jobs/nightly_ingest";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a Databricks configuration.",
        "That element is no longer in this file.",
        "New element",
        "New element");

    /// <summary>A <c>.yml</c> is routed to the family only through the registration beside it.</summary>
    protected override string SelectedIn(string body) => Registration(body, "databricks/bundle");

    /// <summary>The bundle is named by its <c>bundle: name:</c>, and keeps its id through a rename.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var renamed = Regex.Replace(text, "(?<=^  name: )(lakehouse-nightly|Renamed once)", name, RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        return [name];
    }

    [Fact]
    public async Task EachTypeOfTheFamily_AnswersForItsOwnFiles_WithItsOwnReading()
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-resolver");
        var body = folder.Combine("job.yml");
        File.Copy(Fixture("job.yml"), body);
        var registration = Registration(body, "databricks/job");

        // Act.
        var job = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(Resolver(ServiceCollectionAddDatabricksExtension.JobOrigin), folder.Path, registration, "task:ingest")).Level;
        var bundle = Assert.IsType<RejectedContextLevel>(await ResolveAsync(Resolver(ServiceCollectionAddDatabricksExtension.BundleOrigin), folder.Path, registration, "task:ingest"));

        // Assert: the job's resolver draws the task as the job canvas does, and the bundle's leaves the file to it.
        Assert.Equal(["ingest"], job.RelativePath);
        Assert.Equal(DatabricksElementMapper.TaskType, job.Detail.Element.ElementType);
        Assert.Equal(Texts.NotThisType, bundle.Reason);
    }

    [Theory]
    [InlineData("databricks/bundle", "bundle.yml", "bundle")]
    [InlineData("databricks/job", "job.yml", "task:ingest")]
    [InlineData("databricks/pipeline", "pipeline.json", "pipeline")]
    public async Task TheModule_RegistersAResolver_ForEveryTypeOfTheFamily(string mime, string fixture, string elementId)
    {
        // Arrange.
        await using var provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddSingleton<IDiagramDefinitionCatalog, DatabricksOnlyCatalog>()
            .AddSingleton<DiagramFileRouter>()
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        using var folder = TestFolder.Create("databricks-resolver");
        var body = folder.Combine(fixture);
        File.Copy(Fixture(fixture), body);
        var registration = Registration(body, mime);

        // Act.
        var resolutions = new List<ContextLevelResolution>();
        foreach (var resolver in provider.GetServices<IContextSourceResolver>().OfType<DatabricksContextSourceResolver>())
        {
            resolutions.Add(await ResolveAsync(resolver, folder.Path, registration, elementId));
        }

        // Assert: exactly one of the family's resolvers answers for a file of this type.
        Assert.Single(resolutions.OfType<ResolvedContextLevel>());
    }

    private DatabricksContextSourceResolver Resolver(DiagramOrigin origin) =>
        new(origin, new DiagramFileRouter(new DatabricksOnlyCatalog()), _store, new DatabricksElementMapper());

    private static string Registration(string body, string mime)
    {
        var registration = IoPath.ChangeExtension(body, ".adp");
        File.WriteAllText(registration, $"{mime}\r\nbody: {IoPath.GetFileName(body)}\r\n");
        return registration;
    }

    private static Task<ContextLevelResolution> ResolveAsync(IContextSourceResolver resolver, string root, string file, string elementId)
    {
        var parent = new ContextResolvedLevel(
            new ContextSource(),
            [IoPath.GetFileName(file)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, file, IsContainer: false, SourceId: default, root, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

        return resolver.ResolveAsync(
            ShortGuid.NewShortGuid(),
            root,
            new ContextSource { ElementId = new ElementId { Value = elementId } },
            [],
            parent,
            TestContext.Current.CancellationToken).AsTask();
    }
}

/// <summary>The family's definitions and nothing else, so a router built for a test routes only to it.</summary>
internal sealed class DatabricksOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
