using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class CommandResultTests
{
    // ReSharper disable once NotAccessedPositionalProperty.Local
    private sealed record SampleCommand(string Value) : ICommand;

    [Fact]
    public void Success_WithoutAnInverse_SucceedsAndRecordsNothing()
    {
        var result = CommandResult.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Inverse);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void Success_WithAnInverse_CarriesThatInverse()
    {
        var inverse = new SampleCommand("back");

        var result = CommandResult.Success(inverse);

        Assert.True(result.IsSuccess);
        Assert.Same(inverse, result.Inverse);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void Success_WithANullInverse_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CommandResult.Success(null!));
    }

    [Fact]
    public void Failure_CarriesTheReasonAndNoInverse()
    {
        var result = CommandResult.Failure("Nope.");

        Assert.False(result.IsSuccess);
        Assert.Equal("Nope.", result.Error);
        Assert.Null(result.Inverse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Failure_WithoutARealReason_Throws(string error)
    {
        Assert.Throws<ArgumentException>(() => CommandResult.Failure(error));
    }

    [Fact]
    public void Failure_WithANullReason_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CommandResult.Failure(null!));
    }
}
