using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The pipeline splices, in both syntaxes: block YAML inside a bundle and flow JSON as a
/// settings file - each keeping its own conventions, comma discipline included
/// (databricks-diagrams Requirements 2.2 and 6.4).
/// </summary>
public class PipelineWriterTests
{
    private const string Yaml =
        "resources:\r\n"
        + "  pipelines:\r\n"
        + "    bronze:\r\n"
        + "      name: bronze\r\n"
        + "      libraries:\r\n"
        + "        - notebook:\r\n"
        + "            path: transformations/bronze\r\n";

    private static LineDocument LoadJson() =>
        LineDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "pipeline.json")));

    private static PipelineModel Pipeline(LineDocument document) =>
        Assert.Single(PipelineParser.Parse(DatabricksYaml.Root(document), document));

    [Fact]
    public void InsertLibrary_InYaml_AppendsADashEntry()
    {
        // Arrange.
        var document = LineDocument.Parse(Yaml);

        // Act.
        var refusal = PipelineWriter.InsertLibrary(document, Pipeline(document), "file", "transformations/silver.py");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(
            new[] { ("notebook", "transformations/bronze"), ("file", "transformations/silver.py") },
            Pipeline(document).Libraries.Select(library => (library.Kind, library.Path)));
    }

    [Fact]
    public void InsertLibrary_InJson_KeepsTheCommaDiscipline()
    {
        // Arrange.
        var document = LoadJson();

        // Act.
        var refusal = PipelineWriter.InsertLibrary(document, Pipeline(document), "notebook", "transformations/platinum");

        // Assert.
        // The document still parses as JSON-through-YAML - which it would not with a missing or
        // trailing comma - and carries the new entry last.
        Assert.Equal("", refusal);
        var reparsed = Pipeline(document);
        Assert.Equal(4, reparsed.Libraries.Count);
        Assert.Equal(("notebook", "transformations/platinum"), (reparsed.Libraries[^1].Kind, reparsed.Libraries[^1].Path));
    }

    [Fact]
    public void RemoveLibrary_InJson_OfTheLastEntry_StripsTheNewLastsComma()
    {
        // Arrange.
        var document = LoadJson();

        // Act.
        var refusal = PipelineWriter.RemoveLibrary(document, Pipeline(document), "glob", "transformations/gold/**");

        // Assert.
        Assert.Equal("", refusal);
        var reparsed = Pipeline(document);
        Assert.Equal(2, reparsed.Libraries.Count);
        // JSON forbids a trailing comma on the new last item; a reparse failing here would mean
        // the splice left one behind.
        Assert.Equal("file", reparsed.Libraries[^1].Kind);
    }

    [Fact]
    public void RemoveLibrary_OfTheOnlyEntry_IsRefused()
    {
        // Arrange.
        var document = LineDocument.Parse(Yaml);
        var original = document.Text;

        // Act & assert.
        Assert.Contains(
            "at least one library",
            PipelineWriter.RemoveLibrary(document, Pipeline(document), "notebook", "transformations/bronze"),
            StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void SetScalar_InJson_KeepsQuotingAndTheTrailingComma()
    {
        // Arrange.
        var document = LoadJson();
        var before = document.Lines.Count;

        // Act.
        var refusal = PipelineWriter.SetScalar(document, Pipeline(document), "catalog", "lakehouse_prod");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(before, document.Lines.Count);
        Assert.Equal("lakehouse_prod", Pipeline(document).Catalog);
        Assert.Contains("\"catalog\": \"lakehouse_prod\",", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SetScalar_InYaml_AddsAMissingKey()
    {
        // Arrange.
        var document = LineDocument.Parse(Yaml);

        // Act.
        var refusal = PipelineWriter.SetScalar(document, Pipeline(document), "catalog", "lakehouse_dev");

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal("lakehouse_dev", Pipeline(document).Catalog);
    }

    [Fact]
    public void SetScalar_ClearingTheName_IsRefused()
    {
        // Arrange.
        var document = LoadJson();
        var original = document.Text;

        // Act & assert.
        Assert.Contains("needs a name", PipelineWriter.SetScalar(document, Pipeline(document), "name", ""), StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }

    [Fact]
    public void InsertLibrary_Refusals_CoverKindPathAndDuplicates()
    {
        // Arrange.
        var document = LoadJson();
        var original = document.Text;

        // Act & assert.
        Assert.Contains("no 'wheel' library kind", PipelineWriter.InsertLibrary(document, Pipeline(document), "wheel", "x"), StringComparison.Ordinal);
        Assert.Contains("needs a path", PipelineWriter.InsertLibrary(document, Pipeline(document), "file", ""), StringComparison.Ordinal);
        Assert.Contains("already there", PipelineWriter.InsertLibrary(document, Pipeline(document), "file", "transformations/silver.py"), StringComparison.Ordinal);
        Assert.Equal(original, document.Text);
    }
}
