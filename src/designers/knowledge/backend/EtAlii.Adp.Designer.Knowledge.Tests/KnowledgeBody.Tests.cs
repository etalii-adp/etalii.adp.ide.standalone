using System.Text;
using Xunit;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// A knowledge file read through the bundled bindings (knowledge-designer Requirements 2.3, 2.4,
/// 2.9 and 2.10): each shipped example opens as the expected table in all three formats, a file
/// that is not well-formed says so with its position, one of another version opens read-only,
/// and what the designer does not know is reported and kept.
/// </summary>
public class KnowledgeBodyTests
{
    [Theory]
    [MemberData(nameof(KnowledgeFiles.Extensions), MemberType = typeof(KnowledgeFiles))]
    public void TheExample_OpensAsTheExpectedTable(string extension)
    {
        // Act.
        var body = KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities" + extension), "cities" + extension);
        var table = body.Table;

        // Assert: readable, editable, and nothing to report.
        Assert.Equal("", body.Unreadable);
        Assert.Equal("", body.ReadOnlyReason);
        Assert.Empty(body.Model.Findings);

        // The table and its properties, with what each type has.
        Assert.Equal("Cities", table.Name);
        Assert.Equal("v1", table.ActiveViewId);
        Assert.Equal(
            ["p1 Name text", "p2 Country selection", "p3 Population number", "p4 Tags multipleSelection", "p5 Province relation", "p6 Visited checkbox"],
            table.Properties.Take(6).Select(property => $"{property.Id} {property.Name} {property.ValueType}"));
        Assert.Equal(["p1"], table.Properties.Where(property => property.IsTitle).Select(property => property.Id));
        Assert.Equal(["o1 Netherlands blue", "o2 Belgium yellow"], table.Properties[1].Options.Select(option => $"{option.Id} {option.Name} {option.Colour}"));
        // A colour the file does not give is the default, in every format.
        Assert.Equal(["default", "red"], table.Properties[3].Options.Select(option => option.Colour));
        Assert.Equal(("provinces.yaml", "one"), (table.Properties[4].TargetFile, table.Properties[4].Limit));

        // At least one view, the one the file was left in among them.
        Assert.NotEmpty(table.Views);
        Assert.Contains(table.Views, view => view.Id == table.ActiveViewId);
        Assert.Same(table.Views.Single(view => view.Id == "v1"), table.ViewOrDefault(""));

        // Rows with their cells, by property.
        Assert.NotEmpty(table.Rows);
        Assert.All(table.Rows, row => Assert.NotEqual("", row.Id));
        Assert.All(table.Rows.SelectMany(row => row.Cells), cell => Assert.Contains(table.Properties, property => property.Id == cell.PropertyId));
    }

    [Fact]
    public void TheThreeFormats_AreOneTable()
    {
        // Act.
        var tables = new[] { ".yaml", ".json", ".xml" }
            .Select(extension => Shape(KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities" + extension), "cities" + extension).Table))
            .ToList();

        // Assert: the same properties, views and rows, with the same values in the same written form.
        Assert.NotEmpty(tables[0]);
        Assert.Equal(tables[0], tables[1]);
        Assert.Equal(tables[0], tables[2]);
    }

    [Theory]
    [InlineData(".yaml", "ded: \"0.1\"\ndesigner: etalii/knowledge\nname: [unclosed\n")]
    [InlineData(".json", "{ \"ded\": \"0.1\", \"designer\": \"etalii/knowledge\", \"name\": ")]
    [InlineData(".xml", "<ded version=\"0.1\" designer=\"etalii/knowledge\" name=\"Cities\"><properties>")]
    public void AFileThatIsNotWellFormed_SaysSoWithItsPosition_AndHoldsNoTable(string extension, string text)
    {
        // Act.
        var body = KnowledgeBody.Read(Encoding.UTF8.GetBytes(text), "broken" + extension);

        // Assert: the format's own error, with where; nothing guessed out of the wreck.
        Assert.StartsWith("This file cannot be read: ", body.Unreadable, StringComparison.Ordinal);
        Assert.Matches(@"\(line \d+, column \d+\)$", body.Unreadable);
        Assert.NotEqual(KnowledgeBody.AnotherVersion, body.Unreadable);
        Assert.Equal(body.Unreadable, body.ReadOnlyReason);
        Assert.Same(KnowledgeTable.Empty, body.Table);
    }

    [Fact]
    public void AFileOfAnotherVersion_IsNotRead_AndSaysWhy()
    {
        // Arrange: the example, saying it is of a version this application does not know.
        var text = Encoding.UTF8.GetString(KnowledgeFiles.ExampleBytes("cities.yaml")).Replace("ded: \"0.1\"", "ded: \"9.0\"", StringComparison.Ordinal);

        // Act.
        var body = KnowledgeBody.Read(Encoding.UTF8.GetBytes(text), "cities.yaml");

        // Assert: the bindings require the version's mark, so the file is not read - a later version
        // may hold what this one would misread - and the reason names the version, not a parse error.
        Assert.Equal(KnowledgeBody.AnotherVersion, body.Unreadable);
        Assert.Equal(KnowledgeBody.AnotherVersion, body.ReadOnlyReason);
        Assert.Same(KnowledgeTable.Empty, body.Table);
    }

    [Fact]
    public void AnExtensionNoBindingReads_IsRefusedByName()
    {
        // Act.
        var body = KnowledgeBody.Read("name: Cities\n"u8.ToArray(), "cities.toml");

        // Assert.
        Assert.Equal("A knowledge file is YAML, JSON or XML; '.toml' is none of them.", body.Unreadable);
    }

    [Fact]
    public void ACellWhoseValueDoesNotFitItsType_IsAFinding_AndTheRestIsRead()
    {
        // Arrange: a population that is not a number.
        var text = "ded: \"0.1\"\ndesigner: etalii/knowledge\nname: Cities\nproperties:\n  - id: p1\n    name: Name\n    type: text\n    title: true\n  - id: p3\n    name: Population\n    type: number\nviews:\n  - id: v1\n    name: All\nrows:\n  - id: r1\n    cells:\n      - property: p1\n        text: Antwerp\n      - property: p3\n        number: about half a million\n";

        // Act.
        var body = KnowledgeBody.Read(Encoding.UTF8.GetBytes(text), "cities.yaml");

        // Assert: reported where it is; the row and its good cell are there, the bad cell is not.
        var finding = Assert.Single(body.Model.Findings);
        Assert.Equal(20, finding.Location?.Line);
        Assert.Equal(["p1"], Assert.Single(body.Table.Rows).Cells.Select(cell => cell.PropertyId));
        Assert.Equal("", body.ReadOnlyReason);
    }

    [Fact]
    public void TheBundledDefinition_IsWhatItsProvenanceSays()
    {
        // Arrange.
        using var provenance = System.Text.Json.JsonDocument.Parse(KnowledgeDefinition.Resource("knowledge.provenance.json"));

        // Act and assert: each bundled file hashes to what was recorded when it was copied.
        Assert.Equal("etalii-adp/etalii.adp", provenance.RootElement.GetProperty("repository").GetString());
        Assert.Matches("^[0-9a-f]{40}$", provenance.RootElement.GetProperty("revision").GetString());
        var files = provenance.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.GetProperty("path").GetString()!);
            Assert.Equal(file.GetProperty("sha256").GetString(), KnowledgeDefinition.Sha256Of(name));
        }

        // And the specification was read for its types: without them a mistyped value is no finding.
        Assert.True(KnowledgeDefinition.TypedAttributeCount > 20, $"Only {KnowledgeDefinition.TypedAttributeCount} attribute types were read from knowledge.des.");
        Assert.Equal("number", KnowledgeDefinition.AttributeType("Cell", "number"));
    }

    /// <summary>A table as what a format must not change.</summary>
    private static List<string> Shape(KnowledgeTable table) =>
    [
        $"table {table.Name} active {table.ActiveViewId}",
        .. table.Properties.Select(property =>
            $"property {property.Id} {property.Name} {property.ValueType} title={property.IsTitle} target={property.TargetFile} limit={property.Limit} counterpart={property.Counterpart} computed={property.IsComputed} parent={property.IsParent} " +
            string.Join(",", property.Options.Select(option => $"{option.Id}:{option.Name}:{option.Colour}"))),
        .. table.Views.Select(view =>
            $"view {view.Id} {view.Name} group={view.GroupBy} hideEmpty={view.HideEmptyGroups} any={view.Filter.Any} filters={view.Filter.Items.Count} " +
            $"columns={string.Join(",", view.Columns.Select(column => $"{column.PropertyId}:{column.Visible}:{column.Width}:{column.Wrap}"))} " +
            $"sorts={string.Join(",", view.Sorts.Select(sort => $"{sort.PropertyId}:{sort.Descending}"))} " +
            $"collapsed={string.Join(",", view.Collapsed)} order={string.Join(",", view.GroupOrder)} hidden={string.Join(",", view.HiddenGroups)}"),
        .. table.Rows.Select(row => $"row {row.Id} " + string.Join(" ", row.Cells.Select(cell => $"{cell.PropertyId}=[{string.Join("|", cell.Values)}]"))),
    ];
}
