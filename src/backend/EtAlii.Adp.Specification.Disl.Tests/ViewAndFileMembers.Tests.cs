using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The two members a host binds for one reading rather than storing (DISL §12.2, §8.2): an element's
/// <c>view</c>, the viewer's state of it, and the diagram's <c>file</c>, the file a finding names.
/// </summary>
public class ViewAndFileMembersTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": { "types": { "Topic": { "attributes": { "text": { "type": "string" } }, "children": { "allowed": ["Topic"] } } } }
        """));

    private static readonly DislSpecification Shadowed = Specifications.Loaded(Specifications.With("""
        "metamodel": { "diagram": { "attributes": { "file": { "type": "string", "default": "stored" } } }, "types": { "Topic": {} } }
        """));

    private static object? Evaluate(DislSpecification specification, string expression, object self, DislDiagram diagram) =>
        specification.Environment(DislContexts.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = diagram, ["env"] = new CelMap() });

    [Theory]
    [InlineData("self.view.collapsed", true)]
    [InlineData("has(self.view)", true)]
    [InlineData("self.view.collapsed && self.children.size() > 0", true)]
    public void AnElementsView_IsWhatTheHostBound(string expression, bool expected)
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var topic = diagram.AddNode("Topic", "t");
        diagram.AddNode("Topic", "c", parent: topic);
        topic.View = new CelMap { ["collapsed"] = true };

        // Act.
        var value = Evaluate(Specification, expression, topic, diagram);

        // Assert.
        Assert.Equal(expected, value);
    }

    [Fact]
    public void AnElementWithoutAView_HasNoViewMember()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);
        var topic = diagram.AddNode("Topic", "t");

        // Act.
        var has = Evaluate(Specification, "has(self.view)", topic, diagram);
        var read = Evaluate(Specification, "self.view.collapsed", topic, diagram);

        // Assert.
        Assert.Equal(false, has);
        Assert.IsType<CelError>(read);
    }

    [Fact]
    public void TheDiagramsFile_IsWhatTheHostBound()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification) { File = "ideas.adp" };

        // Act.
        var value = Evaluate(Specification, "diagram.file + '!'", diagram, diagram);

        // Assert.
        Assert.Equal("ideas.adp!", value);
    }

    [Fact]
    public void ADiagramWithoutAFile_HasNoFileMember()
    {
        // Arrange.
        var diagram = new DislDiagram(Specification);

        // Act.
        var has = Evaluate(Specification, "has(diagram.file)", diagram, diagram);

        // Assert.
        Assert.Equal(false, has);
    }

    [Fact]
    public void ADiagramAttributeNamedFile_ShadowsTheBoundFile()
    {
        // Arrange.
        var diagram = new DislDiagram(Shadowed) { File = "ideas.adp" };

        // Act.
        var value = Evaluate(Shadowed, "diagram.file", diagram, diagram);

        // Assert.
        Assert.Equal("stored", value);
    }
}
