using Xunit;

namespace EtAlii.Adp.Editor.Yaml.Tests;

public class EditorTests
{
    /// <summary>
    /// This module declares exactly one editor, so Single() is the assertion as well as the
    /// accessor: if it ever grows a second, these tests fail rather than silently checking
    /// whichever one happened to be first.
    /// </summary>
    private static EditorDefinition Definition => Assert.Single(Editor.Definitions);

    [Fact]
    public void Definition_ClaimsTheYamlExtensions_AndIsNoFallback()
    {
        // Arrange, act and assert (yaml-editor Requirement 2.1).
        Assert.Equal("yaml", Definition.Id);
        Assert.Equal("YAML editor", Definition.Title);
        Assert.Equal(new[] { ".yaml", ".yml" }, Definition.Extensions);
        Assert.Empty(Definition.FileNames);
        Assert.False(Definition.IsFallback);
        Assert.False(Definition.IsDefaultForSharedExtension);
        Assert.NotNull(Definition.Build);
    }

    [Fact]
    public void TheModule_ReadsNoYamlOnTheBackend()
    {
        // Arrange and act: what YAML is lives in the client (the design's non-functional
        // requirement); the backend is the family's session and nothing else.
        var references = typeof(Editor).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);

        // Assert.
        Assert.DoesNotContain("YamlDotNet", references);
    }
}
