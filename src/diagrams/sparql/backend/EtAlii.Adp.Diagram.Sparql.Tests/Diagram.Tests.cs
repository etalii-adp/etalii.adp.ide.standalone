using EtAlii.Adp.Documents;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The definition and its routing: a bare <c>.rq</c> routes to <c>w3c/sparql</c> on sight,
/// because <c>.rq</c> means SPARQL everywhere and nothing else claims it (Requirement 2.1).
/// </summary>
public class DiagramTests : IDisposable
{
    private sealed class Catalog : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
    }

    private readonly string _root;
    private readonly DiagramFileRouter _router = new(new Catalog());

    public DiagramTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void TheDefinition_ClaimsOnlyTheQueryExtension()
    {
        // Arrange & act & assert.
        Assert.True(Diagram.Sparql.DeclaresExtension(".rq"));
        Assert.False(Diagram.Sparql.DeclaresExtension(".ru"));
        Assert.False(Diagram.Sparql.DeclaresExtension(".ttl"));

        // No alternate and no shared stance: one extension, claimed on sight.
        Assert.Equal("", Diagram.Sparql.AlternateExtension);
        Assert.False(Diagram.Sparql.SharedExtension);
        Assert.True(Diagram.Sparql.RoutesBareBody);
        Assert.False(Diagram.Sparql.HasFolderSubject);
    }

    [Fact]
    public void ABareQueryFile_RoutesOnSight()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "people.rq");
        File.WriteAllText(path, "SELECT * WHERE { ?s ?p ?o }");

        // Act.
        var routing = _router.Route(path);

        // Assert.
        var routed = Assert.IsType<DiagramRouted>(routing);
        Assert.Equal("w3c/sparql", routed.Definition.Origin.Key);
        Assert.True(_router.ClaimsExtensionOf(path));
    }

    [Fact]
    public void AnUpdateDocumentsExtension_IsNotClaimed()
    {
        // Arrange: SPARQL Update is out of scope, and .ru is its conventional extension.
        var path = IoPath.Combine(_root, "insert.ru");
        File.WriteAllText(path, "INSERT DATA { <a> <b> <c> }");

        // Act & assert.
        Assert.IsType<NotADiagram>(_router.Route(path));
        Assert.False(_router.ClaimsExtensionOf(path));
    }

    [Fact]
    public void TheModule_RegistersNoToolboxAndNoCommands()
    {
        // Arrange: the third layer of the no-writer proof - what the module offers core is what
        // the client can offer a user, so a read-only diagram registers no mutating seam at all.
        using var provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddSparql()
            .BuildServiceProvider();

        // Act & assert.
        Assert.DoesNotContain(
            provider.GetServices<IDiagramToolboxProvider>(),
            toolbox => toolbox.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);

        // The action provider is registered and offers nothing, which is stronger than being
        // absent: it says in code that this diagram has no edits rather than leaving it open.
        Assert.Single(
            provider.GetServices<IContextActionProvider>().OfType<SparqlContextActionProvider>());

        // The seams this module does register.
        Assert.Single(
            provider.GetServices<IDiagramSessionFactory>(),
            factory => factory.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);
        Assert.Single(
            provider.GetServices<IDiagramDocumentReloader>(),
            reloader => reloader.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);
    }

    [Fact]
    public void TheDocumentFactory_ExistsBecauseCoreRequiresIt_AndItsStarterQueryValidatesClean()
    {
        // Arrange: core refuses to start a host whose type declares an extension and registers
        // no factory, so this module has one; what it produces has to open as a diagram rather
        // than as a finding. It supplies a new file's text and never rewrites an existing one.
        using var provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddSparql()
            .BuildServiceProvider();

        // Act.
        var factory = Assert.Single(
            provider.GetServices<IDiagramDocumentFactory>(),
            candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);
        var text = factory.CreateEmptyDocument("people");

        // Assert: the deployment invariant core checks at startup holds for this module.
        Assert.Empty(new DiagramDocumentFactories([factory]).Verify(Diagram.Definitions));

        // And the starter query parses, draws, and reports nothing at all.
        var model = SparqlParser.Parse(text);
        Assert.Equal(SparqlQueryForm.Select, model.Form);
        Assert.NotEmpty(SparqlProjection.Project(model).Nodes);
        Assert.Empty(SparqlValidator.Judge(model, text));
        Assert.EndsWith("\r\n", text);
    }
}
