using Xunit;

namespace EtAlii.Adp.Editor.Json.Tests;

public class EditorTests
{
    /// <summary>
    /// This module declares exactly one editor, so Single() is the assertion as well as the
    /// accessor: if it ever grows a second, these tests fail rather than silently checking
    /// whichever one happened to be first.
    /// </summary>
    private static EditorDefinition Definition => Assert.Single(Editor.Definitions);

    [Fact]
    public void Definition_ClaimsJsonAlone_AndIsNoFallback()
    {
        // Arrange, act and assert (json-editor Requirement 2.1: the id, the title and the claim,
        // .json only - ruling Q1).
        Assert.Equal("json", Definition.Id);
        Assert.Equal("JSON editor", Definition.Title);
        Assert.Equal("mdi-code-json", Definition.Icon);
        Assert.Equal(new[] { ".json" }, Definition.Extensions);
        Assert.Empty(Definition.FileNames);
        Assert.False(Definition.IsFallback);
        Assert.NotNull(Definition.Build);
    }
}
