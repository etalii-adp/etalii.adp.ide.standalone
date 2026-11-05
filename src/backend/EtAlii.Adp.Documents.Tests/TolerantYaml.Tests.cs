using Xunit;
using YamlDotNet.RepresentationModel;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// One YAML file in, either a parsed document or an account of why not, and never an exception
/// (code-structure FR-037; the folder diagrams' Requirement 1.2).
/// </summary>
public class TolerantYamlTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("tolerant-yaml");
    private readonly string _scratch;

    public TolerantYamlTests()
    {
        _scratch = _testFolder.Path;
    }

    public void Dispose()
    {
        _testFolder.Dispose();
    }

    [Fact]
    public void AValidPlaybook_ParsesToItsRootSequence()
    {
        // Arrange.
        var path = Write("play.yml", "---\n- name: A play\n  hosts: web\n");

        // Act.
        var result = TolerantYaml.Read(path, "play.yml");

        // Assert.
        var document = Assert.IsType<TolerantYamlDocument>(result);
        var root = Assert.IsType<YamlSequenceNode>(document.Root);
        Assert.Single(root.Children);
    }

    [Fact]
    public void AParsedNode_CarriesTheLineItWasDeclaredOn()
    {
        // Arrange.
        // The whole reason for using the representation model: a problem points at a line, and
        // the line has to come from the parser rather than from ADP counting newlines.
        var path = Write("play.yml", "---\n- name: First\n  hosts: web\n- name: Second\n  hosts: db\n");

        // Act.
        var document = Assert.IsType<TolerantYamlDocument>(TolerantYaml.Read(path, "play.yml"));

        // Assert.
        var root = Assert.IsType<YamlSequenceNode>(document.Root);
        Assert.Equal(2, root.Children[0].Start.Line);
        Assert.Equal(4, root.Children[1].Start.Line);
    }

    [Fact]
    public void AMalformedFile_IsAFailureCarryingTheParsersOwnMessageAndLine()
    {
        // Arrange.
        var path = Write("broken.yml", "---\n- name: A play\n  msg: \"never closed\n  loop: [ 1, 2\n");

        // Act.
        var result = TolerantYaml.Read(path, "broken.yml");

        // Assert.
        var unreadable = Assert.IsType<TolerantYamlUnreadable>(result);
        Assert.Equal("broken.yml", unreadable.Failure.RelativePath);
        Assert.NotEqual("", unreadable.Failure.Message);
        Assert.True(unreadable.Failure.Line > 0, "The parser named a line and it should survive.");
        // The position is carried as a number, so it must not also be repeated in the text.
        Assert.DoesNotContain("Line:", unreadable.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyFile_IsAnEmptyDocument_NotAFailure()
    {
        // Arrange.
        // Valid YAML with no document in it. The reader simply finds nothing to recognise.
        var path = Write("empty.yml", "");

        // Act.
        var result = TolerantYaml.Read(path, "empty.yml");

        // Assert.
        Assert.Null(Assert.IsType<TolerantYamlDocument>(result).Root);
    }

    [Fact]
    public void AFileThatIsNotThere_IsAFailureRatherThanAThrow()
    {
        // Act.
        var result = TolerantYaml.Read(IoPath.Combine(_scratch, "absent.yml"), "absent.yml");

        // Assert.
        Assert.IsType<TolerantYamlUnreadable>(result);
    }

    [Fact]
    public void ReadingDoesNotLockTheFile_SoAnEditorCanStillWriteIt()
    {
        // Arrange.
        // A reader that took an exclusive lock would fight the text editor the user is fixing
        // their YAML in - which is the only editor this type ever expects them to use.
        var path = Write("shared.yml", "---\n- name: A play\n");

        // Act.
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var result = TolerantYaml.Read(path, "shared.yml");

        // Assert.
        Assert.IsType<TolerantYamlDocument>(result);
    }

    [Fact]
    public void BinaryContent_DoesNotThrow()
    {
        // Arrange.
        // The never-throws promise holds for content that is not text at all.
        var path = IoPath.Combine(_scratch, "binary.yaml");
        File.WriteAllBytes(path, [0x00, 0xFF, 0x13, 0x37, 0x00, 0x01]);

        // Act.
        var result = TolerantYaml.Read(path, "binary.yaml");

        // Assert.
        // Either outcome is acceptable - some binary happens to parse as a YAML scalar - but
        // an exception never is.
        Assert.True(result is TolerantYamlDocument or TolerantYamlUnreadable);
    }

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_scratch, name);
        File.WriteAllText(path, content);
        return path;
    }
}
