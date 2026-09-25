using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The declaration itself. Small assertions, but each pins a fact the rest of the system resolves
/// by: a wrong origin is a toolbox and a validator that silently never run, and a wrongly shared
/// extension is a file that stops routing.
/// </summary>
public class DiagramTests
{
    [Fact]
    public void TheOriginIsGenericDependencies()
    {
        // Assert.
        var definition = Assert.Single(Diagram.Definitions);
        Assert.Equal(new DiagramOrigin("generic", "dependencies"), definition.Origin);
    }

    [Fact]
    public void TheExtensionIsDgrAndOwnedOutright()
    {
        // Assert.
        // Owned outright means not shared - a bare .dgr routes on sight, with no registration
        // step.
        Assert.Equal(".dgr", Diagram.DependencyGraph.Extension);
        Assert.False(Diagram.DependencyGraph.SharedExtension);
        Assert.True(Diagram.DependencyGraph.RoutesBareBody);
    }

    [Fact]
    public void TheSubjectIsADocument()
    {
        // Assert.
        Assert.False(Diagram.DependencyGraph.HasFolderSubject);
        Assert.True(Diagram.DependencyGraph.HasDocumentSibling);
    }

    [Fact]
    public void TheDisplayTitleIsDependencyGraph()
    {
        // Assert.
        Assert.Equal("Dependency Graph", Diagram.DependencyGraph.Title);
    }

    [Fact]
    public void TheDescriptionSaysWhatTheDiagramAnswers_AndMentionsNoTime()
    {
        // Assert.
        // The Add dialog shows this beside the choice; an empty one leaves a first-time user
        // guessing at a notation from its title alone. And this type is the timeline with time
        // removed, so a description that promised one would be describing the wrong diagram.
        Assert.NotEmpty(Diagram.DependencyGraph.Description);
        foreach (var word in new[] { "time", "date", "when", "duration", "moment" })
        {
            Assert.DoesNotContain(word, Diagram.DependencyGraph.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheBuildHookRegistersTheModule()
    {
        // Assert.
        // Discovery invokes this at startup; a null hook is a module that is found and then
        // contributes nothing, with no error anybody would see.
        Assert.NotNull(Diagram.DependencyGraph.Build);
    }

    [Theory]
    [InlineData("services.dgr", true)]
    [InlineData("SERVICES.DGR", true)]
    [InlineData("services.yml", false)]
    [InlineData("services.tml", false)]
    [InlineData("services.dgr.bak", false)]
    [InlineData("", false)]
    public void IsBody_AnswersByExtensionAlone(string path, bool expected)
    {
        // Act & assert.
        Assert.Equal(expected, Diagram.IsBody(path));
    }
}
