using Xunit;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>The YAML reading helpers every parser shares (code-structure T133, FR-046).</summary>
public class YamlAccessTests
{
    private const string Text = "name: pipeline\r\nenabled: true\r\nstages:\r\n  - build\r\n  - test\r\nsettings:\r\n  retries: 3\r\nempty:\r\n";

    private static YamlMappingNode Root() => YamlAccess.Root(LineDocument.Parse(Text))!;

    [Fact]
    public void AScalar_IsItsValue_AndAnythingElseIsNull()
    {
        // Arrange.
        var root = Root();

        // Act and assert.
        Assert.Equal("pipeline", YamlAccess.Scalar(root, "name"));
        Assert.Equal("", YamlAccess.Scalar(root, "empty"));
        Assert.Null(YamlAccess.Scalar(root, "stages"));
        Assert.Null(YamlAccess.Scalar(root, "missing"));
    }

    [Fact]
    public void AScalarNode_IsTheNodeItself_AndAnythingElseIsNull()
    {
        // Arrange.
        var root = Root();

        // Act and assert.
        Assert.Same(root.Children[new YamlScalarNode("name")], YamlAccess.ScalarNode(root, "name"));
        Assert.Equal("", YamlAccess.ScalarNode(root, "empty")!.Value);
        Assert.Null(YamlAccess.ScalarNode(root, "stages"));
        Assert.Null(YamlAccess.ScalarNode(root, "missing"));
    }

    [Fact]
    public void ASequence_IsItsItems_AndAnythingElseIsEmpty()
    {
        // Arrange.
        var root = Root();

        // Act and assert.
        Assert.Equal(["build", "test"], YamlAccess.Sequence(root, "stages").Select(node => ((YamlScalarNode)node).Value));
        Assert.Empty(YamlAccess.Sequence(root, "name"));
        Assert.Empty(YamlAccess.Sequence(root, "missing"));
    }

    [Fact]
    public void AMappingAndAFlag_AreReadUnderTheirKeys()
    {
        // Arrange.
        var root = Root();

        // Act and assert.
        Assert.Equal("3", YamlAccess.Scalar(YamlAccess.Mapping(root, "settings")!, "retries"));
        Assert.Null(YamlAccess.Mapping(root, "name"));
        Assert.IsType<YamlSequenceNode>(YamlAccess.Node(root, "stages"));
        Assert.Null(YamlAccess.Node(root, "missing"));
        Assert.True(YamlAccess.Flag(root, "enabled"));
        Assert.False(YamlAccess.Flag(root, "name"));
    }

    [Fact]
    public void ANodesLine_IsTheZeroBasedLineItStartsOn()
    {
        // Arrange.
        var document = LineDocument.Parse(Text);
        var root = YamlAccess.Root(document)!;

        // Act and assert.
        Assert.Equal(0, YamlAccess.LineOf(root.Children[new YamlScalarNode("name")], document));
        Assert.Equal(3, YamlAccess.LineOf(YamlAccess.Sequence(root, "stages").First(), document));
    }

    [Theory]
    [InlineData("")]
    [InlineData("- a\r\n- b\r\n")]
    [InlineData("just text\r\n")]
    public void ADocumentWhoseRootIsNoMapping_HasNoRoot(string text)
    {
        // Act and assert.
        Assert.Null(YamlAccess.Root(LineDocument.Parse(text)));
    }
}
