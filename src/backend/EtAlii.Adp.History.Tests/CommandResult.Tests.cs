using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.History.Tests;

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
        var inverse = new CommandResultSampleCommand();

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
    public void FromSave_OfASaveThatLanded_SucceedsWithTheInverse_AndNoWarning()
    {
        // Arrange.
        var inverse = new CommandResultSampleCommand();

        // Act.
        var result = CommandResult.FromSave(DocumentSaveResult.Ok, inverse);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Same(inverse, result.Inverse);
        Assert.Equal("", result.Warning);
    }

    [Fact]
    public void FromSave_OfASaveWithAWarning_SucceedsAndCarriesTheWarning()
    {
        // Arrange.
        var inverse = new CommandResultSampleCommand();

        // Act.
        var result = CommandResult.FromSave(DocumentSaveResult.WithWarning("The layout was not kept."), inverse);

        // Assert.
        Assert.True(result.IsSuccess);
        Assert.Same(inverse, result.Inverse);
        Assert.Equal("The layout was not kept.", result.Warning);
    }

    [Fact]
    public void FromSave_OfASaveThatFailed_FailsWithItsReason_AndNoInverse()
    {
        // Act.
        var result = CommandResult.FromSave(DocumentSaveResult.Failure("plan.skv could not be written."), new CommandResultSampleCommand());

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("plan.skv could not be written.", result.Error);
        Assert.Null(result.Inverse);
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
