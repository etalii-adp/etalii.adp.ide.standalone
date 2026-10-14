using EtAlii.Adp.Documents;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The fixtures through the shared tolerant reader: the valid ones are well-formed YAML, and the
/// broken one fails in exactly the file it is meant to.
/// </summary>
public class AnsibleFixtureYamlTests
{
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
            .Select(path => TolerantYaml.Read(path, IoPath.GetRelativePath(root, path)))
            .OfType<TolerantYamlUnreadable>()
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
            .Select(path => (Path: IoPath.GetRelativePath(root, path), Result: TolerantYaml.Read(path, IoPath.GetRelativePath(root, path))))
            .Where(pair => pair.Result is TolerantYamlUnreadable)
            .Select(pair => pair.Path)
            .ToArray();

        // Assert.
        Assert.Equal(["unparsable.yml"], unreadable);
    }

    private static string[] YamlFilesUnder(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => IoPath.GetExtension(path) is ".yml" or ".yaml")
            .Order(StringComparer.Ordinal)];
}
