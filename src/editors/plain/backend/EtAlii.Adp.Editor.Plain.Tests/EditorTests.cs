using Xunit;

namespace EtAlii.Adp.Editor.Plain.Tests;

public class EditorTests
{
    /// <summary>
    /// This module declares exactly one editor, so Single() is the assertion as well as the
    /// accessor: if it ever grows a second, these tests fail rather than silently checking
    /// whichever one happened to be first.
    /// </summary>
    private static EditorDefinition Definition => Assert.Single(Editor.Definitions);

    [Fact]
    public void Definition_IsTheFallback_AndClaimsNothing()
    {
        // Arrange, act and assert: claiming nothing is WHAT MAKES IT the fallback - not a
        // registration-order accident (Requirement 3.2).
        Assert.True(Definition.IsFallback);
        Assert.Empty(Definition.Extensions);
        Assert.Empty(Definition.FileNames);
        Assert.Equal("plain", Definition.Id);
    }
}
