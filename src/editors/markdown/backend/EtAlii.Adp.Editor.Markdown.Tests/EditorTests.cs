using Xunit;

namespace EtAlii.Adp.Editor.Markdown.Tests;

public class EditorTests
{
    /// <summary>
    /// This module declares exactly one editor, so Single() is the assertion as well as the
    /// accessor: if it ever grows a second, these tests fail rather than silently checking
    /// whichever one happened to be first.
    /// </summary>
    private static EditorDefinition Definition => Assert.Single(Editor.Definitions);

    [Fact]
    public void Definition_ClaimsTheMarkdownExtensions_AndIsNoFallback()
    {
        // Arrange, act and assert.
        Assert.Equal("markdown", Definition.Id);
        Assert.Equal(new[] { ".md", ".markdown" }, Definition.Extensions);
        Assert.False(Definition.IsFallback);
        Assert.NotNull(Definition.Build);
    }
}
