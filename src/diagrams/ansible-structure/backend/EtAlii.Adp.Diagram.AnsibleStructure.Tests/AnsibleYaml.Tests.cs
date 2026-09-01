using Xunit;

using YamlDotNet.RepresentationModel;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The narrowest point of Requirement 1.2: one file in, either a parsed document or an account
/// of why not, and never an exception.
/// </summary>
public class AnsibleYamlTests : IDisposable
{
    private readonly string _scratch;

    public AnsibleYamlTests()
    {
        _scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_scratch);
    }

    [Fact]
    public void AValidPlaybook_ParsesToItsRootSequence()
    {
        // Arrange.
        var path = Write("play.yml", "---\n- name: A play\n  hosts: web\n");

        // Act.
        var result = AnsibleYaml.Read(path, "play.yml");

        // Assert.
        var document = Assert.IsType<AnsibleYamlDocument>(result);
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
        var document = Assert.IsType<AnsibleYamlDocument>(AnsibleYaml.Read(path, "play.yml"));

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
        var result = AnsibleYaml.Read(path, "broken.yml");

        // Assert.
        var unreadable = Assert.IsType<AnsibleYamlUnreadable>(result);
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
        var result = AnsibleYaml.Read(path, "empty.yml");

        // Assert.
        Assert.Null(Assert.IsType<AnsibleYamlDocument>(result).Root);
    }

    [Fact]
    public void AFileThatIsNotThere_IsAFailureRatherThanAThrow()
    {
        // Act.
        var result = AnsibleYaml.Read(IoPath.Combine(_scratch, "absent.yml"), "absent.yml");

        // Assert.
        Assert.IsType<AnsibleYamlUnreadable>(result);
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
        var result = AnsibleYaml.Read(path, "shared.yml");

        // Assert.
        Assert.IsType<AnsibleYamlDocument>(result);
    }

    [Theory]
    [InlineData("infrastructure")]
    [InlineData("unconventional")]
    public void EveryYamlFileInAValidFixture_Parses(string fixture)
    {
        // Arrange.
        // The fixtures' only machine check: they are hand-written, and this at least proves
        // they are well-formed YAML. It says nothing about whether they are good Ansible.
        var root = IoPath.Combine("Fixtures", fixture);

        // Act.
        var failures = YamlFilesUnder(root)
            .Select(path => AnsibleYaml.Read(path, IoPath.GetRelativePath(root, path)))
            .OfType<AnsibleYamlUnreadable>()
            .Select(unreadable => $"{unreadable.Failure.RelativePath}:{unreadable.Failure.Line} {unreadable.Failure.Message}")
            .ToArray();

        // Assert.
        Assert.Empty(failures);
    }

    [Fact]
    public void TheDeliberatelyBrokenFixture_IsTheOnlyOneThatDoesNotParse()
    {
        // Arrange.
        var root = IoPath.Combine("Fixtures", "broken");

        // Act.
        var unreadable = YamlFilesUnder(root)
            .Select(path => (Path: IoPath.GetRelativePath(root, path), Result: AnsibleYaml.Read(path, IoPath.GetRelativePath(root, path))))
            .Where(pair => pair.Result is AnsibleYamlUnreadable)
            .Select(pair => pair.Path)
            .ToArray();

        // Assert.
        Assert.Equal(["unparsable.yml"], unreadable);
    }

    private static string[] YamlFilesUnder(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => IoPath.GetExtension(path) is ".yml" or ".yaml")
            .Order(StringComparer.Ordinal)];

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_scratch, name);
        File.WriteAllText(path, content);
        return path;
    }
}
