using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The rules themselves, judged directly. The rename provider's own tests prove the same
/// rules still reach a rename; these prove what they are.
/// </summary>
public class EntryNameRulesTests : IDisposable
{
    private readonly string _folder;

    public EntryNameRulesTests()
    {
        _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_AnEmptyName_IsRejected(string name)
    {
        // Act.
        var result = EntryNameRules.Validate(name, _folder);

        // Assert.
        Assert.False(result.Valid);
        Assert.Equal("Enter a name.", result.Reason);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("sub/name.txt")]
    [InlineData("sub\\name.txt")]
    [InlineData("C:name.txt")]
    [InlineData("na*me.txt")]
    [InlineData("na?me.txt")]
    public void Validate_ANameThatIsOrContainsAPath_OrAnInvalidCharacter_IsRejected(string name)
    {
        // Act.
        var result = EntryNameRules.Validate(name, _folder);

        // Assert.
        Assert.False(result.Valid);
        Assert.Contains("cannot contain a path", result.Reason);
    }

    [Fact]
    public void Validate_AFreeName_IsAccepted()
    {
        // Arrange, act and assert.
        Assert.True(EntryNameRules.Validate("diagram.adp", _folder).Valid);
    }

    [Fact]
    public void Validate_ANameTakenByAFile_IsRejectedNamingIt()
    {
        // Arrange.
        File.WriteAllText(IoPath.Combine(_folder, "taken.adp"), "");

        // Act.
        var result = EntryNameRules.Validate("taken.adp", _folder);

        // Assert.
        Assert.False(result.Valid);
        Assert.Equal("An item named 'taken.adp' already exists in this folder.", result.Reason);
    }

    [Fact]
    public void Validate_ANameTakenByAFolder_IsRejected()
    {
        // Act.
        Directory.CreateDirectory(IoPath.Combine(_folder, "docs"));

        // Assert.
        Assert.False(EntryNameRules.Validate("docs", _folder).Valid);
    }

    [Fact]
    public void Validate_ASurroundingWhitespaceOnlyDifference_IsTrimmedAway()
    {
        // Act.
        File.WriteAllText(IoPath.Combine(_folder, "taken.adp"), "");

        // Assert.
        Assert.False(EntryNameRules.Validate("  taken.adp  ", _folder).Valid);
    }

    [Fact]
    public void Validate_ForARename_TheCurrentNameItself_IsRejected()
    {
        // Arrange.
        File.WriteAllText(IoPath.Combine(_folder, "current.txt"), "");

        // Act.
        var result = EntryNameRules.Validate("current.txt", _folder, currentName: "current.txt");

        // Assert.
        Assert.False(result.Valid);
        Assert.Equal("Enter a name that differs from the current one.", result.Reason);
    }

    [Fact]
    public void Validate_ForARename_ACasingOnlyChange_IsAccepted()
    {
        // Act.
        // The entry collides with itself on a case-insensitive filesystem, which is exactly
        // what renaming its casing means - the move handles it.
        File.WriteAllText(IoPath.Combine(_folder, "current.txt"), "");

        // Assert.
        Assert.True(EntryNameRules.Validate("Current.txt", _folder, currentName: "current.txt").Valid);
    }

    [Fact]
    public void Validate_ForACreation_ANameTakenByTheEntryBeingRenamedElsewhere_IsStillRejected()
    {
        // Act.
        // Without a currentName there is no same-entry exemption: every existing name is taken.
        File.WriteAllText(IoPath.Combine(_folder, "current.txt"), "");

        // Assert.
        Assert.False(EntryNameRules.Validate("current.txt", _folder).Valid);
    }
}
