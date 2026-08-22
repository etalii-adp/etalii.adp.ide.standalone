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
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string[] TempFiles() =>
        Directory.GetFiles(_folder, $"{AdpFileWriter.TempPrefix}*{AdpFileWriter.TempExtension}");

    [Fact]
    public void Create_WritesExactlyTheLineAndALineFeed_WithoutAByteOrderMark()
    {
        var result = AdpFileWriter.Create(_folder, "domain.adp", "freeplane/mindmap");

        var created = Assert.IsType<AdpFileWriteResult.Created>(result);
        Assert.Equal(IoPath.Combine(_folder, "domain.adp"), created.FullPath);

        // Asserting bytes, not the string: a BOM would be invisible in a string comparison.
        var bytes = File.ReadAllBytes(created.FullPath);
        Assert.Equal(Encoding.UTF8.GetBytes("freeplane/mindmap\n"), bytes);
    }

    [Fact]
    public void Create_LeavesNoTemporaryFileBehind()
    {
        AdpFileWriter.Create(_folder, "domain.adp", "freeplane/mindmap");

        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Create_WhenTheNameIsTaken_ReportsItAndLeavesTheExistingFileUntouched()
    {
        var existing = IoPath.Combine(_folder, "domain.adp");
        File.WriteAllText(existing, "do not touch me");

        var result = AdpFileWriter.Create(_folder, "domain.adp", "freeplane/mindmap");

        Assert.IsType<AdpFileWriteResult.NameTaken>(result);
        Assert.Equal("do not touch me", File.ReadAllText(existing));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Create_WhenAFolderHoldsTheName_ReportsItAsTaken()
    {
        Directory.CreateDirectory(IoPath.Combine(_folder, "domain.adp"));

        Assert.IsType<AdpFileWriteResult.NameTaken>(AdpFileWriter.Create(_folder, "domain.adp", "x/y"));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Create_WhenTheFolderIsGone_FailsWithoutThrowing()
    {
        var missing = IoPath.Combine(_folder, "not-there");

        var result = AdpFileWriter.Create(missing, "domain.adp", "freeplane/mindmap");

        var failed = Assert.IsType<AdpFileWriteResult.Failed>(result);
        Assert.NotEqual("", failed.Message);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Create_TwiceWithDifferentNames_ProducesTwoIndependentFiles()
    {
        AdpFileWriter.Create(_folder, "a.adp", "x/a");
        AdpFileWriter.Create(_folder, "b.adp", "x/b");

        Assert.Equal("x/a\n", File.ReadAllText(IoPath.Combine(_folder, "a.adp")));
        Assert.Equal("x/b\n", File.ReadAllText(IoPath.Combine(_folder, "b.adp")));
        Assert.Empty(TempFiles());
    }
}
