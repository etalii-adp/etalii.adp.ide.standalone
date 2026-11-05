using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Modules.Tests;

/// <summary>
/// One YAML file a persistence plugin is handed, read tolerantly once for every folder plugin (code-structure
/// T294): a parsed document with its lines, or the parser's own account of why not, and never an exception.
/// The cases are those ansible-structure's and helm-chart's copies each handled.
/// </summary>
public class PluginFileYamlTests
{
    [Fact]
    public void AWellFormedFile_ParsesWithTheLinesItWasDeclaredOn()
    {
        // Arrange.
        var file = File("roles/web/meta/main.yml", "---\ndependencies:\n  - role: common\n  - role: db\n");

        // Act.
        var result = PluginFileYaml.Read(file, file.RelativePath);

        // Assert.
        var root = Assert.IsType<YamlMappingNode>(Assert.IsType<TolerantYamlDocument>(result).Root);
        var dependencies = Assert.IsType<YamlSequenceNode>(root.Children[new YamlScalarNode("dependencies")]);
        Assert.Equal([3L, 4L], dependencies.Children.Select(child => child.Start.Line));
    }

    [Fact]
    public void AnEmptyFile_IsADocumentWithNothingInIt()
    {
        // Act.
        var result = PluginFileYaml.Read(File("values.yaml", ""), "values.yaml");

        // Assert: valid YAML with nothing to recognise, not a failure.
        Assert.Null(Assert.IsType<TolerantYamlDocument>(result).Root);
    }

    [Fact]
    public void AMalformedFile_IsAFailureWithTheParsersWordsAndLine_UnderTheNameItWasGiven()
    {
        // Arrange.
        var file = File("chart/values.yaml", "a: 1\nb: \"never closed\n");

        // Act.
        var result = PluginFileYaml.Read(file, "values.yaml");

        // Assert: the parser's own message, without the position prefix YamlDotNet puts in front of it, since
        // the line travels as a number.
        var failure = Assert.IsType<TolerantYamlUnreadable>(result).Failure;
        Assert.Equal("values.yaml", failure.RelativePath);
        Assert.Equal(2u, failure.Line);
        Assert.Equal("While scanning a quoted scalar, found unexpected end of stream.", failure.Message);
    }

    [Fact]
    public void AnOversizedFile_IsNotRead_AndSaysSo()
    {
        // Arrange: handed by its path with no bytes - and here with bytes that would parse, which must not be.
        var file = new PluginFile("Chart.yaml", "apiVersion: v2\n"u8.ToArray()) { IsOversized = true };

        // Act.
        var result = PluginFileYaml.Read(file, "Chart.yaml");

        // Assert.
        Assert.Equal(
            new TolerantYamlFailure("Chart.yaml", "The file is larger than this host reads, so it is not read.", 0),
            Assert.IsType<TolerantYamlUnreadable>(result).Failure);
    }

    [Fact]
    public void TheBytes_AreDecodedAsTheSharedReaderDecodesAFile()
    {
        // Arrange: UTF-8 unless a byte order mark says otherwise.
        var utf16 = new PluginFile("values.yaml", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("name: ü\n")]);
        var utf8 = new PluginFile("values.yaml", "name: ü\n"u8.ToArray());

        // Act.
        var fromUtf16 = Assert.IsType<YamlMappingNode>(Assert.IsType<TolerantYamlDocument>(PluginFileYaml.Read(utf16, "values.yaml")).Root);
        using var text = PluginFileYaml.Text(utf8);

        // Assert.
        Assert.Equal("ü", ((YamlScalarNode)fromUtf16.Children[new YamlScalarNode("name")]).Value);
        Assert.Equal("name: ü\n", text.ReadToEnd());
    }

    [Fact]
    public void NoFileOrNoName_IsRefused()
    {
        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => PluginFileYaml.Read(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => PluginFileYaml.Read(File("x", ""), null!));
        Assert.Throws<ArgumentNullException>(() => PluginFileYaml.Text(null!));
    }

    private static PluginFile File(string relativePath, string text) => new(relativePath, Encoding.UTF8.GetBytes(text));
}
