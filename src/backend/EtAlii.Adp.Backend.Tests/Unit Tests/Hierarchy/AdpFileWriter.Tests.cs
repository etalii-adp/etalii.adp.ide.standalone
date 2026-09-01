using System.Text;
using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class AdpFileWriterTests : IDisposable
{
    private readonly string _folder;

    public AdpFileWriterTests()
    {
        _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    private string[] TempFiles() =>
        Directory.GetFiles(_folder, $"{AdpFileWriter.TempPrefix}*{AdpFileWriter.TempExtension}");

    [Fact]
    public void Create_WritesExactlyTheLineAndALineFeed_WithoutAByteOrderMark()
    {
        var result = AdpFileWriter.Create(_folder, "domain.adp", "freeplane/mindmap");

        var created = Assert.IsType<AdpFileCreated>(result);
        Assert.Equal(IoPath.Combine(_folder, "domain.adp"), created.FullPath);

        // Asserting bytes, not the string: a BOM would be invisible in a string comparison.
        var bytes = File.ReadAllBytes(created.FullPath);
        Assert.Equal(Encoding.UTF8.GetBytes("freeplane/mindmap\n"), bytes);
    }

    [Fact]
    public void Create_LeavesNoTemporaryFileBehind()
    {
        // Act.
        AdpFileWriter.Create(_folder, "domain.adp", "freeplane/mindmap");

        // Assert.
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Create_WhenTheNameIsTaken_ReportsItAndLeavesTheExistingFileUntouched()
    {
        // Arrange.
        var existing = IoPath.Combine(_folder, "domain.adp");
        File.WriteAllText(existing, "do not touch me");

        // Act.
        var result = AdpFileWriter.Create(_folder, "domain.adp", "freeplane/mindmap");

        // Assert.
        Assert.IsType<AdpFileNameTaken>(result);
        Assert.Equal("do not touch me", File.ReadAllText(existing));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Create_WhenAFolderHoldsTheName_ReportsItAsTaken()
    {
        // Act.
        Directory.CreateDirectory(IoPath.Combine(_folder, "domain.adp"));

        // Assert.
        Assert.IsType<AdpFileNameTaken>(AdpFileWriter.Create(_folder, "domain.adp", "x/y"));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Create_WhenTheFolderIsGone_FailsWithoutThrowing()
    {
        // Arrange.
        var missing = IoPath.Combine(_folder, "not-there");

        var result = AdpFileWriter.Create(missing, "domain.adp", "freeplane/mindmap");

        // Act and assert, step by step.
        var failed = Assert.IsType<AdpFileWriteFailed>(result);
        Assert.NotEqual("", failed.Message);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Create_TwiceWithDifferentNames_ProducesTwoIndependentFiles()
    {
        // Arrange and act.
        AdpFileWriter.Create(_folder, "a.adp", "x/a");
        AdpFileWriter.Create(_folder, "b.adp", "x/b");

        // Assert.
        Assert.Equal("x/a\n", File.ReadAllText(IoPath.Combine(_folder, "a.adp")));
        Assert.Equal("x/b\n", File.ReadAllText(IoPath.Combine(_folder, "b.adp")));
        Assert.Empty(TempFiles());
    }
}
