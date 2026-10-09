using System.Globalization;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Routing;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Conformance;

/// <summary>
/// What a new knowledge file starts as (knowledge-designer task 9, Requirement 10.4): each
/// binding's template, written through <see cref="TemplateWriter"/>, is read back through that
/// same binding with no finding, as one title property, one view and no rows, and is the same
/// table in the three formats.
/// </summary>
public class KnowledgeTemplatesTests
{
    private static readonly string BindingFile = Path.Combine(Repository.Conformance, "etalii.adp", "definitions", "designers", "knowledge.fbl");

    public static TheoryData<string, string> Formats => new()
    {
        { "yaml", ".yaml" },
        { "json", ".json" },
        { "xml", ".xml" },
    };

    private static FblBinding Binding(string name)
    {
        var binding = FblDocumentLoader.ResolveReference("knowledge.fbl#" + name, BindingFile, out var problems);
        Assert.DoesNotContain(problems, problem => problem.Severity == ProblemSeverity.Error);
        return binding;
    }

    private static OpenBody New(string template, string readBy, string extension)
    {
        var made = 0;
        var bytes = TemplateWriter.Produce(Binding(template), "etalii/knowledge", "Cities" + extension, rule => $"{rule}{++made}");
        Assert.NotNull(bytes);
        return OpenBody.Open(bytes, Binding(readBy), new FblOptions { FileName = "Cities" + extension, DeriveId = KnowledgeIds.Derive, AttributeType = KnowledgeTypes.Of });
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void ANewFile_ReadsThroughItsBinding_AsOneTitlePropertyOneViewAndNoRows(string name, string extension)
    {
        // Act.
        var model = New(name, name, extension).Model;

        // Assert: nothing to report, and exactly the table an author starts from.
        Assert.False(model.Unreadable);
        Assert.Empty(model.Findings);
        Assert.Equal(["Property", "Table", "View"], model.Elements.Select(element => element.Type).Order(StringComparer.Ordinal));

        var table = model.Elements.Single(element => element.Type == "Table");
        Assert.Equal("Cities", table.Attributes["name"]);

        var property = model.Elements.Single(element => element.Type == "Property");
        Assert.Equal("property1", property.Id);
        Assert.Equal("Name", property.Attributes["name"]);
        var written = property.Attributes.Values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)?.ToLowerInvariant()).ToList();
        Assert.Contains("text", written);
        Assert.Contains("true", written);

        var view = model.Elements.Single(element => element.Type == "View");
        Assert.Equal("view2", view.Id);
    }

    [Theory]
    [InlineData("yaml", "json", ".json")]
    [InlineData("json", "xml", ".xml")]
    [InlineData("xml", "yaml", ".yaml")]
    public void ATemplateOfAnotherFormat_IsNotATableOfThisOne(string template, string readBy, string extension)
    {
        // Act: what a host would create if it took the template of the wrong binding.
        var model = New(template, readBy, extension).Model;

        // Assert: it is caught on reading, by the file being unreadable or holding no table.
        Assert.True(
            model.Unreadable || model.Findings.Count > 0 || model.Elements.All(element => element.Type != "Property"),
            $"A {template} template read as {readBy} gave a table.");
    }
}
