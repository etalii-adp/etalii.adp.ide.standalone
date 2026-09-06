using System.Text;
using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
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
    public void Create_WritesExactlyTheLineAndACrlfEnding_WithoutAByteOrderMark()
    {
        var result = AdpFileWriter.Create(_folder, "domain.adp", "freeplane/mindmap");

        var created = Assert.IsType<AdpFileCreated>(result);
        Assert.Equal(IoPath.Combine(_folder, "domain.adp"), created.FullPath);

        // Asserting bytes, not the string: a BOM would be invisible in a string comparison.
        // CRLF, because the repository's house style is CRLF in the working tree and the app
        // must not be the tool CLAUDE.md's line-endings section warns about. It once wrote LF
        // here while the layout writer wrote CRLF, leaving mixed endings inside one file.
        var bytes = File.ReadAllBytes(created.FullPath);
        Assert.Equal(Encoding.UTF8.GetBytes("freeplane/mindmap\r\n"), bytes);
    }

    [Fact]
    public void Create_ThenALayoutBlock_LeavesNoMixedLineEndings()
    {
        // Arrange. The two writers that once disagreed: creation wrote LF, the layout block
        // CRLF, so a registration created and then repositioned in the running app carried
        // both conventions at once.
        var created = Assert.IsType<AdpFileCreated>(AdpFileWriter.Create(_folder, "map.adp", "wardley/map"));

        // Act.
        RegistrationLayout.SetPosition(created.FullPath, "kettle", new RegistrationPosition(120.5, 44));

        // Assert. Every line feed is half of a CRLF - one convention for the whole file.
        var text = File.ReadAllText(created.FullPath);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                Assert.True(
                    index > 0 && text[index - 1] == '\r',
                    $"Bare LF at index {index} in: {text.Replace("\r", "<CR>").Replace("\n", "<LF>")}");
            }
        }
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
        Assert.Equal("x/a\r\n", File.ReadAllText(IoPath.Combine(_folder, "a.adp")));
        Assert.Equal("x/b\r\n", File.ReadAllText(IoPath.Combine(_folder, "b.adp")));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Save_ReplacesWhatIsThere_WhichIsTheWholeDifferenceFromCreate()
    {
        // Arrange.
        // Create refuses a taken name; a save is an overwrite, and every caller this method was
        // written for is a document save.
        var path = IoPath.Combine(_folder, "map.owm");
        File.WriteAllText(path, "the old content");

        // Act.
        AdpFileWriter.Save(path, "the new content");

        // Assert.
        Assert.Equal("the new content", File.ReadAllText(path));
    }

    [Fact]
    public void Save_WritesWithoutAByteOrderMark()
    {
        // Arrange.
        // The stores this replaced either asked for UTF-8-without-BOM explicitly or took
        // File.WriteAllText's default, which is the same encoding. A BOM appearing here would
        // change the bytes of every document those stores save, which their round-trip corpora
        // compare byte for byte.
        var path = IoPath.Combine(_folder, "map.owm");

        // Act.
        AdpFileWriter.Save(path, "content");

        // Assert.
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "a BOM was written");
        Assert.Equal("content", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Save_WritesTheContentVerbatim_AddingNoTerminator()
    {
        // Arrange.
        // Create appends the house CRLF because it is writing a new first line. Save is handed
        // a whole document that already ends how its writer meant it to - appending anything
        // would grow a file by one line per save.
        var path = IoPath.Combine(_folder, "map.owm");

        // Act.
        AdpFileWriter.Save(path, "no trailing newline");

        // Assert.
        Assert.Equal("no trailing newline", File.ReadAllText(path));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        // Arrange.
        var path = IoPath.Combine(_folder, "map.owm");

        // Act.
        AdpFileWriter.Save(path, "content");

        // Assert.
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Save_WhenTheFolderIsGone_ThrowsAndLeavesNoScratchFile()
    {
        // Arrange.
        // Save imposes no error policy: it throws and each caller keeps the policy it had - the
        // mindmap store propagates, the Wardley store and the two sidecars catch and keep the
        // edit in memory. Centralizing the algorithm must not centralize that decision.
        var missing = IoPath.Combine(_folder, "gone", "map.owm");

        // Act and assert.
        Assert.ThrowsAny<IOException>(() => AdpFileWriter.Save(missing, "content"));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Save_WhenTheDestinationIsAFolder_ThrowsAndLeavesTheFolderThere()
    {
        // Arrange.
        var path = IoPath.Combine(_folder, "occupied");
        Directory.CreateDirectory(path);

        // Act and assert.
        // Moving onto a folder raises UnauthorizedAccessException rather than IOException,
        // which is precisely why every caller of Save catches both - and why this asserts the
        // pair rather than the one it first guessed.
        var thrown = Record.Exception(() => AdpFileWriter.Save(path, "content"));
        Assert.True(
            thrown is IOException or UnauthorizedAccessException,
            $"expected an IO or access failure, got {thrown.GetType().Name}");
        Assert.True(Directory.Exists(path), "the existing folder was removed");
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void Save_UsesTheSameScratchPatternTheHierarchyIgnores()
    {
        // Arrange.
        // The scratch name is not cosmetic: HierarchyModel ignores this pattern, so a save must
        // not make a file briefly appear in the user's explorer. Proven by watching the folder
        // during the write rather than by reading the constant back.
        var path = IoPath.Combine(_folder, "map.owm");
        var seen = new List<string>();
        using var watcher = new FileSystemWatcher(_folder);
        watcher.EnableRaisingEvents = true;
        watcher.Created += (_, args) => seen.Add(IoPath.GetFileName(args.FullPath));

        // Act.
        AdpFileWriter.Save(path, "content");
        Thread.Sleep(120);

        // Assert.
        // Whatever the watcher caught, anything that was not the destination must carry the
        // ignored prefix.
        Assert.All(
            seen.Where(name => !string.Equals(name, "map.owm", StringComparison.Ordinal)),
            name => Assert.StartsWith(AdpFileWriter.TempPrefix, name, StringComparison.Ordinal));
    }
}
