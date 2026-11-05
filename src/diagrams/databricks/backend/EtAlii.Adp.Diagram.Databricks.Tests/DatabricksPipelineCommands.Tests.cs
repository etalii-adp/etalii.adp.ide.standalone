using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The pipeline commands as the binding writes them, in both syntaxes: block YAML inside a bundle and JSON as a settings
/// file, each keeping its own conventions, its commas included (databricks-diagrams Requirements 2.2 and 6.4;
/// databricks-disl-fbl Requirement 6).
/// </summary>
public sealed class DatabricksPipelineCommandsTests
{
    private const string Yaml =
        "resources:\r\n"
        + "  pipelines:\r\n"
        + "    bronze:\r\n"
        + "      name: bronze\r\n"
        + "      libraries:\r\n"
        + "        - notebook:\r\n"
        + "            path: transformations/bronze\r\n";

    [Fact]
    public async Task InsertLibrary_InYaml_AppendsADashEntry()
    {
        // Arrange.
        using var editing = DatabricksEditing.Holding("databricks.yml", Yaml);

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksLibraryCommand(body, "", "file", "transformations/silver.py"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            [("notebook", "transformations/bronze"), ("file", "transformations/silver.py")],
            (await editing.PipelineAsync()).Libraries.Select(library => (library.Kind, library.Path)));
    }

    [Fact]
    public async Task InsertLibrary_InJson_KeepsTheCommas()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("pipeline.json");

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksLibraryCommand(body, "", "notebook", "transformations/platinum"));

        // Assert.
        // The document still reads, which it would not with a missing or a trailing comma, and carries the new entry last.
        Assert.True(result.IsSuccess, result.Error);
        var pipeline = await editing.PipelineAsync();
        Assert.Equal(4, pipeline.Libraries.Count);
        Assert.Equal(("notebook", "transformations/platinum"), (pipeline.Libraries[^1].Kind, pipeline.Libraries[^1].Path));
    }

    [Fact]
    public async Task RemoveLibrary_InJson_OfTheLastEntry_StripsTheNewLastsComma()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("pipeline.json");

        // Act.
        var result = await editing.RunAsync(body => new RemoveDatabricksLibraryCommand(body, "", "glob", "transformations/gold/**"));

        // Assert.
        // JSON forbids a trailing comma on the new last item; a reading failing here would mean one was left behind.
        Assert.True(result.IsSuccess, result.Error);
        var pipeline = await editing.PipelineAsync();
        Assert.Equal(2, pipeline.Libraries.Count);
        Assert.Equal("file", pipeline.Libraries[^1].Kind);
    }

    [Fact]
    public async Task RemoveLibrary_OfTheOnlyEntry_IsRefused()
    {
        // Arrange.
        using var editing = DatabricksEditing.Holding("databricks.yml", Yaml);
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new RemoveDatabricksLibraryCommand(body, "", "notebook", "transformations/bronze"));

        // Assert.
        Assert.Contains("at least one library", result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Fact]
    public async Task SetScalar_InJson_KeepsQuotingAndTheTrailingComma()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("pipeline.json");
        var before = (await editing.TextAsync()).Split('\n').Length;

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksPipelineScalarCommand(body, "", "catalog", "lakehouse_prod"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, (await editing.TextAsync()).Split('\n').Length);
        Assert.Equal("lakehouse_prod", (await editing.PipelineAsync()).Catalog);
        Assert.Contains("\"catalog\": \"lakehouse_prod\",", await editing.TextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetScalar_InYaml_AddsAMissingKey()
    {
        // Arrange.
        using var editing = DatabricksEditing.Holding("databricks.yml", Yaml);

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksPipelineScalarCommand(body, "", "catalog", "lakehouse_dev"));

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("lakehouse_dev", (await editing.PipelineAsync()).Catalog);
    }

    [Fact]
    public async Task SetScalar_ClearingTheName_IsRefused()
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("pipeline.json");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksPipelineScalarCommand(body, "", "name", ""));

        // Assert.
        Assert.Contains("needs a name", result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Theory]
    [InlineData("wheel", "x", "There is no 'wheel' library kind to add.")]
    [InlineData("file", "", "needs a path")]
    [InlineData("file", "transformations/silver.py", "already there")]
    public async Task InsertLibrary_Refusals_CoverKindPathAndDuplicates(string kind, string path, string sentence)
    {
        // Arrange.
        using var editing = DatabricksEditing.Of("pipeline.json");
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new InsertDatabricksLibraryCommand(body, "", kind, path));

        // Assert.
        Assert.Contains(sentence, result.Error, StringComparison.Ordinal);
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Theory]
    [InlineData("serverless", "true")]
    [InlineData("edition", "ADVANCED")]
    public async Task SetScalar_OfAValueThePipelineReadingDoesNotWrite_IsRefused(string key, string value)
    {
        // Arrange.
        // serverless is read from the file and read-only in the pipeline's definition, and edition is not in it: neither
        // is offered by the property grid, and the command refuses both as the binding does (fork 2, taken as A on
        // 2026-10-30: the definitions' answer).
        using var editing = DatabricksEditing.Holding("databricks.yml", Yaml);
        var original = await editing.BytesAsync();

        // Act.
        var result = await editing.RunAsync(body => new SetDatabricksPipelineScalarCommand(body, "", key, value));

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal(original, await editing.BytesAsync());
    }
}
