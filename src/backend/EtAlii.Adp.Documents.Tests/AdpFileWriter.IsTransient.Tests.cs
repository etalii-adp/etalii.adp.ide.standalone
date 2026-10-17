using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The one test for a transient file name, which the hierarchy, the problems, the save diagnostics
/// and the reload bridge share (code-structure T146, FR-050).
/// </summary>
public class AdpFileWriterIsTransientTests
{
    [Theory]
    [InlineData("~adp-0123456789abcdef.tmp", true)]
    [InlineData("~adp-0123456789abcdef.TMP", true)]
    [InlineData("model.dsl~RF1a2b3c.TMP", true)]
    [InlineData("model.dsl~rf1A2B3C.tmp", true)]
    [InlineData("folder/model.dsl~RF1a2b3c.TMP", true)]
    [InlineData("model.dsl", false)]
    [InlineData("~adp-notes.txt", false)]
    [InlineData("adp-0123.tmp", false)]
    [InlineData("model.dsl~RF.TMP", false)]
    [InlineData("model.dsl~RFxyz.TMP", false)]
    [InlineData("model.dsl~RF1a2b3c.txt", false)]
    [InlineData("notes.tmp", false)]
    public void AName_IsTransient_OnlyWhenAWriteMadeIt(string name, bool transient)
    {
        Assert.Equal(transient, AdpFileWriter.IsTransient(name));
    }

    [Theory]
    [InlineData("model.dsl~RF1a2b3c.TMP", "model.dsl", true)]
    [InlineData("model.dsl~RF1a2b3c.TMP", "MODEL.DSL", true)]
    [InlineData("model.dsl~RF1a2b3c.TMP", "folder/model.dsl", true)]
    [InlineData("model.dsl~RF1a2b3c.TMP", "other.dsl", false)]
    [InlineData("model.dsl.bak~RF1a2b3c.TMP", "model.dsl", false)]
    public void ABackup_IsOfTheDestinationItNames(string name, string destination, bool backup)
    {
        Assert.Equal(backup, AdpFileWriter.IsReplaceBackup(name, of: destination));
    }
}
