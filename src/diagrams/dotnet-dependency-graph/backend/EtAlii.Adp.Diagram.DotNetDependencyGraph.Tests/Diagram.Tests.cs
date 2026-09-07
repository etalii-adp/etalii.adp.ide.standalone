using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// What this module declares itself to be. Small, and worth having: three halves of this
/// definition carry decisions the design argued - two extensions for one notation, a shared
/// extension so a workspace's solutions are not all claimed on sight, and a document sibling
/// rather than ansible's folder subject - and a definition that drifted from any of them would
/// break the binding quietly rather than loudly.
/// </summary>
public class DiagramTests
{
    [Fact]
    public void TheDefinition_IsTheOriginTheCatalogNames()
    {
        // Arrange, act and assert.
        // docs/diagrams.md carries this exact origin; the .adp's first line is its MIME form.
        Assert.Equal("dotnet/dependency-graph", Diagram.DependencyGraph.Origin.Key);
        Assert.Equal("dotnet/dependency-graph", Diagram.DependencyGraph.Origin.MimeType);
    }

    [Fact]
    public void TheDefinition_DeclaresBothSolutionSerializations()
    {
        // Arrange, act and assert.
        // One notation, two readable forms: .slnx is the newer serialization of the same
        // solution, which is what AlternateExtension exists for.
        Assert.True(Diagram.DependencyGraph.DeclaresExtension(".sln"));
        Assert.True(Diagram.DependencyGraph.DeclaresExtension(".slnx"));
        Assert.True(Diagram.DependencyGraph.HasDocumentSibling);
    }

    [Fact]
    public void TheDefinition_SharesItsExtension_SoASolutionBecomesADiagramOnlyWhenSaidSo()
    {
        // Arrange, act and assert.
        // Requirement 2.3's "never inferred": a workspace is full of solutions nobody asked to
        // see drawn, and a shared extension is never routed from a bare body.
        Assert.True(Diagram.DependencyGraph.SharedExtension);
    }

    [Fact]
    public void TheDefinition_SubjectIsTheDocument_NotTheFolder()
    {
        // Arrange, act and assert.
        // The departure from the ansible template, and the reason for it: a folder subject
        // cannot say which solution when a folder holds two.
        Assert.Equal(DiagramSubject.Document, Diagram.DependencyGraph.Subject);
        Assert.False(Diagram.DependencyGraph.HasFolderSubject);
    }

    [Fact]
    public void TheModule_DeclaresExactlyOneNotation()
    {
        // Arrange, act and assert.
        Assert.Same(Diagram.DependencyGraph, Assert.Single(Diagram.Definitions));
    }

    [Fact]
    public void TheDefinition_SurvivesDiscovery_RatherThanBeingDroppedAsMalformed()
    {
        // A build proving the module compiles is not the same claim as the host finding it.
        // Discovery drops a malformed definition at the cost of one entry and one warning, so
        // a type that declared, say, a folder subject beside an extension would vanish exactly
        // this quietly - green build, absent diagram type. This asks the scan itself.

        // Arrange.
        var assemblies = new[] { typeof(Diagram).Assembly };

        // Act.
        var discovered = DiagramDefinitionDiscovery.Discover(assemblies);

        // Assert.
        var definition = Assert.Single(discovered);
        Assert.Equal("dotnet/dependency-graph", definition.Origin.Key);
    }

    [Fact]
    public void TheDefinition_SaysWhatTheDiagramAnswers_RatherThanRestatingItsTitle()
    {
        // Arrange, act and assert.
        // The description is shown beside the choice in the Add dialog, to a user who may be
        // meeting the notation for the first time.
        Assert.NotEqual("", Diagram.DependencyGraph.Description);
        Assert.DoesNotContain(Diagram.DependencyGraph.Title, Diagram.DependencyGraph.Description, StringComparison.OrdinalIgnoreCase);
    }
}
