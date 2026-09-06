using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class CommandResultTests
{

    [Fact]
    public void Success_WithoutAnInverse_SucceedsAndRecordsNothing()
    {
        // Act.
        var result = CommandResult.Success();

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Null(result.Inverse);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void Success_WithAnInverse_CarriesThatInverse()
    {
        // Arrange.
        var inverse = new CommandResultSampleCommand("back");

        // Act.
        var result = CommandResult.Success(inverse);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Same(inverse, result.Inverse);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void Success_WithANullInverse_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => CommandResult.Success(null!));
    }

    [Fact]
    public void Failure_CarriesTheReasonAndNoInverse()
    {
        // Act.
        var result = CommandResult.Failure("Nope.");

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("Nope.", result.Error);
        Assert.Null(result.Inverse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Failure_WithoutARealReason_Throws(string error)
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentException>(() => CommandResult.Failure(error));
    }

    [Fact]
    public void Failure_WithANullReason_Throws()
    {
        // Arrange, act and assert.
        Assert.Throws<ArgumentNullException>(() => CommandResult.Failure(null!));
    }
}
