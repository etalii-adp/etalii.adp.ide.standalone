using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// A replace failing with <c>0x80070497</c> is tried again for a moment; nothing else is.
/// </summary>
public class AdpFileWriterReplaceRetryTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("adp-replace-retry");
    private readonly string _path;
    private readonly string _temporary;

    public AdpFileWriterReplaceRetryTests()
    {
        _path = IoPath.Combine(_testFolder.Path, "tea.owm");
        _temporary = IoPath.Combine(_testFolder.Path, "~adp-scratch.tmp");
        File.WriteAllText(_path, "before");
        File.WriteAllText(_temporary, "after");
    }

    public void Dispose()
    {
        _testFolder.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IOException Failure(uint code) => new("Scripted.") { HResult = unchecked((int)code) };

    [Fact]
    public void AReplaceThatFailsTwiceWithTheRaceCode_LandsOnTheThirdAttempt()
    {
        // Arrange.
        var attempts = 0;

        // Act.
        AdpFileWriter.ReplaceAllowingForAnOutsider(_temporary, _path, (temporary, path) =>
        {
            if (++attempts < 3)
            {
                throw Failure(0x80070497);
            }

            File.Move(temporary, path, overwrite: true);
        });

        // Assert.
        Assert.Equal(3, attempts);
        Assert.Equal("after", File.ReadAllText(_path));
    }

    [Fact]
    public void AReplaceThatKeepsFailing_SurfacesTheFailure_AfterFourAttempts()
    {
        // Arrange.
        var attempts = 0;

        // Act.
        var thrown = Assert.Throws<IOException>(() => AdpFileWriter.ReplaceAllowingForAnOutsider(_temporary, _path, (_, _) =>
        {
            attempts++;
            throw Failure(0x80070497);
        }));

        // Assert.
        Assert.Equal(unchecked((int)0x80070497), thrown.HResult);
        Assert.Equal(4, attempts);
        Assert.Equal("before", File.ReadAllText(_path));
    }

    [Fact]
    public void AReaderSharingViolation_FailsAtOnce()
    {
        // Arrange.
        var attempts = 0;

        // Act.
        Assert.Throws<IOException>(() => AdpFileWriter.ReplaceAllowingForAnOutsider(_temporary, _path, (_, _) =>
        {
            attempts++;
            throw Failure(0x80070020);
        }));

        // Assert.
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ADestinationTheFailedReplaceLeftMissing_IsPublishedByAMove()
    {
        // Act.
        AdpFileWriter.ReplaceAllowingForAnOutsider(_temporary, _path, (_, path) =>
        {
            File.Delete(path);
            throw Failure(0x80070497);
        });

        // Assert.
        Assert.Equal("after", File.ReadAllText(_path));
        Assert.False(File.Exists(_temporary));
    }
}
