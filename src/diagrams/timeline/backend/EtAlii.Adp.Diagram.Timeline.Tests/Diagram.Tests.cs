using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The declaration itself. Small assertions, but each pins a fact the rest of the system resolves
/// by: a wrong origin is a toolbox and a validator that silently never run, and a wrongly shared
/// extension is a file that stops routing.
/// </summary>
public class DiagramTests
{
    [Fact]
    public void TheOriginIsGenericTimeline()
    {
        // Assert.
        var definition = Assert.Single(Diagram.Definitions);
        Assert.Equal(new DiagramOrigin("generic", "timeline"), definition.Origin);
    }

    [Fact]
    public void TheExtensionIsTmlAndOwnedOutright()
    {
        // Assert.
        // Requirement 1.2: .tml is Timeline Markup Language, this type's own extension. Owned
        // outright means not shared - a bare .tml routes on sight, with no registration step.
        Assert.Equal(".tml", Diagram.Timeline.Extension);
        Assert.False(Diagram.Timeline.SharedExtension);
        Assert.True(Diagram.Timeline.RoutesBareBody);
    }

    [Fact]
    public void TheSubjectIsADocument()
    {
        // Assert.
        Assert.False(Diagram.Timeline.HasFolderSubject);
        Assert.True(Diagram.Timeline.HasDocumentSibling);
    }

    [Fact]
    public void TheDescriptionSaysWhatTheDiagramAnswers()
    {
        // Assert.
        // The Add dialog shows this beside the choice; an empty one leaves a first-time user
        // guessing at a notation from its title alone.
        Assert.NotEmpty(Diagram.Timeline.Description);
    }

    [Fact]
    public void TheBuildHookRegistersTheModule()
    {
        // Assert.
        // Discovery invokes this at startup; a null hook is a module that is found and then
        // contributes nothing, with no error anybody would see.
        Assert.NotNull(Diagram.Timeline.Build);
    }

    [Theory]
    [InlineData("plan.tml", true)]
    [InlineData("PLAN.TML", true)]
    [InlineData("plan.yml", false)]
    [InlineData("plan.tml.bak", false)]
    [InlineData("", false)]
    public void IsBody_AnswersByExtensionAlone(string path, bool expected)
    {
        // Act & assert.
        Assert.Equal(expected, Diagram.IsBody(path));
    }
}
