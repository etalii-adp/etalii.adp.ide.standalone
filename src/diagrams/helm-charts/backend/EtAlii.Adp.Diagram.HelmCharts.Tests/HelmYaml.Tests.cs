using Xunit;

using YamlDotNet.RepresentationModel;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The narrowest point of Requirement 3.1: one file in, either a parsed document or an account
/// of why not, and never an exception.
/// </summary>
public class HelmYamlTests : IDisposable
{
    private readonly string _scratch;

    public HelmYamlTests()
    {
        _scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_scratch);
    }

    [Fact]
    public void AValidChartYaml_ParsesToItsRootMapping()
    {
        // Arrange.
        var path = Write("Chart.yaml", "apiVersion: v2\nname: sample\nversion: 1.2.3\n");

        // Act.
        var result = HelmYaml.Read(path, "Chart.yaml");

        // Assert.
        var document = Assert.IsType<HelmYamlDocument>(result);
        var root = Assert.IsType<YamlMappingNode>(document.Root);
        Assert.Equal(3, root.Children.Count);
    }

    [Fact]
    public void AParsedNode_CarriesTheLineItWasDeclaredOn()
    {
        // Arrange.
        // The whole reason for using the representation model: a problem points at a line, and
        // the line has to come from the parser rather than from ADP counting newlines.
        var path = Write("values.yaml", "replicaCount: 1\nservice:\n  type: ClusterIP\n");

        // Act.
        var document = Assert.IsType<HelmYamlDocument>(HelmYaml.Read(path, "values.yaml"));

        // Assert.
        var root = Assert.IsType<YamlMappingNode>(document.Root);
        // Indexing by key yields the VALUE node: the nested mapping starting on line 3 (the
        // key scalar itself sits on line 2 - an easy off-by-one to re-learn).
        var service = root.Children[new YamlScalarNode("service")];
        Assert.Equal(3, service.Start.Line);
    }

    [Fact]
    public void AnEmptyFile_IsADocumentWithNoRoot()
    {
        // Arrange.
        var path = Write("values.yaml", "");

        // Act.
        var result = HelmYaml.Read(path, "values.yaml");

        // Assert.
        // Valid YAML, no document in it - an empty values.yaml is an ordinary chart state,
        // not a failure.
        var document = Assert.IsType<HelmYamlDocument>(result);
        Assert.Null(document.Root);
    }

    [Fact]
    public void BrokenYaml_BecomesAFailureInTheParsersOwnWords()
    {
        // Arrange.
        // A mapping value at the wrong indentation - the kind of mistake a hand edit makes.
        var path = Write("Chart.yaml", "apiVersion: v2\nname: sample\n  version: oops\n");

        // Act.
        var result = HelmYaml.Read(path, "Chart.yaml");

        // Assert.
        var unreadable = Assert.IsType<HelmYamlUnreadable>(result);
        Assert.Equal("Chart.yaml", unreadable.Failure.RelativePath);
        Assert.True(unreadable.Failure.Line > 0);
        // The position prefix is stripped: the line travels as a number, and the message must
        // not repeat it in front of the parser's words.
        Assert.False(unreadable.Failure.Message.StartsWith('('));
        Assert.NotEqual(string.Empty, unreadable.Failure.Message);
    }

    [Fact]
    public void AMissingFile_BecomesAFailureRatherThanAnException()
    {
        // Arrange.
        var path = IoPath.Combine(_scratch, "not-there.yaml");

        // Act.
        var result = HelmYaml.Read(path, "not-there.yaml");

        // Assert.
        var unreadable = Assert.IsType<HelmYamlUnreadable>(result);
        Assert.Equal(0u, unreadable.Failure.Line);
    }

    [Fact]
    public void BinaryContent_DoesNotThrow()
    {
        // Arrange.
        // Requirement 3.5's never-throws promise holds for content that is not text at all.
        var path = IoPath.Combine(_scratch, "binary.yaml");
        File.WriteAllBytes(path, [0x00, 0xFF, 0x13, 0x37, 0x00, 0x01]);

        // Act.
        var result = HelmYaml.Read(path, "binary.yaml");

        // Assert.
        // Either outcome is acceptable - some binary happens to parse as a YAML scalar - but
        // an exception never is.
        Assert.True(result is HelmYamlDocument or HelmYamlUnreadable);
    }

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_scratch, name);
        File.WriteAllText(path, content);
        return path;
    }
}
