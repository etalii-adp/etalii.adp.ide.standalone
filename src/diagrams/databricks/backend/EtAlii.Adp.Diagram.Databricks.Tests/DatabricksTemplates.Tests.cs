using EtAlii.Adp.Diagram.Databricks.Tests.Parity;
using EtAlii.Adp.Specification.Fbl.Routing;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The binding's three new documents, <c>template.byOrigin</c> (FBL 0.10 section 13), are the bytes the module's factory
/// writes for every base name of the transcript's corpus (databricks-disl-fbl Requirement 4.6, finding B20), except for a base name
/// that starts with a digit, which FBL's <c>{key}</c> prefixes with <c>_</c> and the factory does not.
/// </summary>
public class DatabricksTemplatesTests
{
    public static TheoryData<string, string> Cases()
    {
        var cases = new TheoryData<string, string>();
        foreach (var type in new[] { "bundle", "job", "pipeline" })
        {
            foreach (var baseName in DatabricksTranscript.NewBaseNames.Where(name => !char.IsAsciiDigit(name[0]))) cases.Add(type, baseName);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheTemplateOfEachReading_IsTheFactorysDocument(string type, string baseName)
    {
        // Arrange.
        var fileName = baseName + (type == "pipeline" ? ".json" : ".yml");
        var factory = ServiceCollectionAddDatabricksExtension.EmptyDocument(type, baseName);

        // Act.
        var template = TemplateWriter.Produce(DatabricksBody.Binding, $"databricks/{type}", fileName, rule => rule);

        // Assert.
        Assert.Equal(factory, Text(template));
    }

    [Fact]
    public void ABaseNameThatStartsWithADigit_IsTheOneDifference()
    {
        // Arrange: FBL's {key} prefixes '_' before a leading digit (FBL 13); the factory, which still writes every new
        // document, does not (B20), so its bytes are kept and the template is the one that differs.
        const string factory = "resources:\r\n  jobs:\r\n    2024:\r\n      name: 2024\r\n      tasks:\r\n        - task_key: main\r\n          notebook_task:\r\n            notebook_path: notebooks/2024\r\n";
        const string template = "resources:\r\n  jobs:\r\n    _2024:\r\n      name: 2024\r\n      tasks:\r\n        - task_key: main\r\n          notebook_task:\r\n            notebook_path: notebooks/_2024\r\n";

        // Act.
        var written = TemplateWriter.Produce(DatabricksBody.Binding, "databricks/job", "2024.yml", rule => rule);

        // Assert.
        Assert.Equal(factory, ServiceCollectionAddDatabricksExtension.EmptyDocument("job", "2024"));
        Assert.Equal(template, Text(written));
    }

    private static string Text(byte[]? bytes) => System.Text.Encoding.UTF8.GetString(Assert.IsType<byte[]>(bytes));
}
