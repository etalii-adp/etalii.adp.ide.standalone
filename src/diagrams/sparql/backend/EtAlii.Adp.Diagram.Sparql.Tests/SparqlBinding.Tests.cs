using System.Text.Json;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The module's binding, <c>sparql.fbl</c> (sparql-disl-fbl, task 9): it loads clean, it is read-only with the sentence
/// the diagram shows, it claims what the module opens, it never creates a registration on a first placement, and its
/// template is the text a new query is created with.
/// </summary>
public class SparqlBindingTests
{
    private static readonly string _file = RepositoryPaths.Under("src", "diagrams", "sparql", "backend", "EtAlii.Adp.Diagram.Sparql", "sparql.fbl");

    [Fact]
    public void TheBinding_LoadsWithNoErrorAndIsTheOneTheModuleEmbeds()
    {
        // Act.
        var problems = FblDocumentLoader.Load(_file, out var document);

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var binding = Assert.Single(document!.Bindings).Value;
        Assert.Equal(("query", "SPARQL 1.1 query"), (binding.Name, binding.Title));
        Assert.Equal(binding.ReadOnly, SparqlBody.Binding.ReadOnly);
        Assert.Equal(binding.Template!.Text, SparqlBody.Binding.Template!.Text);
    }

    [Fact]
    public void TheBinding_IsReadOnly_WithTheSentenceTheDiagramShows()
    {
        // Act & assert: the binding is the one place that sentence is written, and the context actions say it.
        Assert.Equal("A query diagram offers no edits: it reads the .rq file, which is edited in a text editor.", SparqlBody.Binding.ReadOnly);
        Assert.Equal(SparqlBody.Binding.ReadOnly, SparqlContextActionProvider.NoActionsReason);
    }

    [Fact]
    public void TheBinding_ClaimsTheQueryFiles_AndIsReadByThePlugin()
    {
        // Act.
        var binding = SparqlBody.Binding;

        // Assert.
        Assert.Equal([".rq"], binding.Claims.Extensions);
        Assert.Equal([ServiceCollectionAddSparqlExtension.SparqlOrigin.ToString()], binding.Claims.Origins);
        Assert.False(binding.Claims.Shared);
        Assert.Equal(("net.etalii.adp.w3c.sparqlQuery", "^1.0.0"), (binding.Plugin!.Plugin, binding.Plugin.Version));
        Assert.Equal(new SparqlQueryPlugin().Id, binding.Plugin.Plugin);
    }

    [Fact]
    public void AFirstPlacement_CreatesNoRegistration()
    {
        // Arrange: the setting is the loader's own, so it is read as written.
        using var json = JsonDocument.Parse(File.ReadAllBytes(_file));

        // Act.
        var registration = json.RootElement.GetProperty("bindings").GetProperty("query").GetProperty("registration");

        // Assert.
        Assert.False(registration.GetProperty("createOnFirstPlacement").GetBoolean());
    }

    [Fact]
    public void TheTemplate_IsTheTextANewQueryIsCreatedWith()
    {
        // Arrange.
        using var provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddSparql()
            .BuildServiceProvider();
        var factory = Assert.Single(
            provider.GetServices<IDiagramDocumentFactory>(),
            candidate => candidate.Origin == ServiceCollectionAddSparqlExtension.SparqlOrigin);

        // Act.
        var text = factory.CreateEmptyDocument("people");

        // Assert: the starter query, with the house newline, read clean.
        Assert.Equal(SparqlBody.Binding.Template!.Text, text);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.False(SparqlBody.Parse(text).IsUnreadable);
    }
}
