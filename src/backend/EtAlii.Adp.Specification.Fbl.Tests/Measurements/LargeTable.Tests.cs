using System.Diagnostics;
using System.Globalization;
using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Measurements;

/// <summary>
/// Measures a table of ten thousand rows and eight properties, written as one entry per cell,
/// through this host's FBL runtime (knowledge-designer task 2, Requirement 9.6): opening it,
/// setting one cell, adding one row and removing one property. The design's language decision
/// L1 chose that shape without knowing its cost, and this is where the cost is taken.
/// </summary>
/// <remarks>
/// The times are written to the test output and are NOT asserted: a bound that holds on one
/// machine is not a bound on another. What is asserted is that each operation happens and that
/// the edited body reads back with the edit, so the numbers are of real work.
/// </remarks>
public class LargeTableTests(ITestOutputHelper output)
{
    private const int Rows = 10_000;
    private const int Properties = 8;

    private static string DraftBinding => Path.Combine(
        Repository.Root, "src", "backend", "EtAlii.Adp.Specification.Fbl.Tests", "Measurements", "knowledge-draft.fbl");

    private static string PropertyId(int index) => string.Create(CultureInfo.InvariantCulture, $"p{index}");

    private static string RowId(int index) => string.Create(CultureInfo.InvariantCulture, $"r{index}");

    /// <summary>The table as YAML: a text title, three more texts, two numbers, a checkbox and a date.</summary>
    private static string Generate(int rows)
    {
        string[] types = ["text", "text", "text", "text", "number", "number", "checkbox", "date"];
        var text = new StringBuilder();
        text.Append("knowledge: \"0.1\"\nname: Large\nactiveView: v1\nproperties:\n");
        for (var property = 0; property < Properties; property++)
        {
            text.Append(CultureInfo.InvariantCulture, $"  - id: {PropertyId(property)}\n    name: Property {property}\n    type: {types[property]}\n");
            if (property == 0) text.Append("    title: true\n");
        }

        text.Append("views:\n  - id: v1\n    name: Table\nrows:\n");
        for (var row = 0; row < rows; row++)
        {
            text.Append(CultureInfo.InvariantCulture, $"  - id: {RowId(row)}\n    cells:\n");
            for (var property = 0; property < Properties; property++)
            {
                text.Append(CultureInfo.InvariantCulture, $"      - property: {PropertyId(property)}\n");
                var line = types[property] switch
                {
                    "text" => string.Create(CultureInfo.InvariantCulture, $"        text: Value {row} of {property}\n"),
                    "number" => string.Create(CultureInfo.InvariantCulture, $"        number: {row * 7 + property}\n"),
                    "checkbox" => row % 2 == 0 ? "        checked: true\n" : "        checked: false\n",
                    _ => "        date: 2026-10-08\n",
                };
                text.Append(line);
            }
        }

        return text.ToString();
    }

    private static FblBinding LoadBinding()
    {
        // The draft is written in FBL 0.2 and this library reads it with a warning, so only an
        // error is a reason not to measure.
        var problems = FblDocumentLoader.Load(DraftBinding, out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == ProblemSeverity.Error);
        return document!.Bindings["yaml"];
    }

    private static FblElement CellOf(FblModel model, string rowId, string propertyId) =>
        model.Elements.Single(element =>
            element.Type == "Cell" &&
            element.ParentId == rowId &&
            Equals(element.Attributes.GetValueOrDefault("property"), propertyId));

    private T Timed<T>(string what, Func<T> work)
    {
        var watch = Stopwatch.StartNew();
        var result = work();
        watch.Stop();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: {watch.Elapsed.TotalMilliseconds:F0} ms"));
        return result;
    }

    [Fact]
    public void ATableOfTenThousandRows_IsOpenedAndEditedThroughTheBinding()
    {
        // Arrange.
        var binding = LoadBinding();
        var text = Generate(Rows);
        var bytes = Encoding.UTF8.GetBytes(text);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"rows: {Rows}, properties: {Properties}, bytes: {bytes.Length}, lines: {text.AsSpan().Count('\n')}"));

        // Act: open.
        var body = Timed("open", () => OpenBody.Open(bytes, binding, new FblOptions { FileName = "large.yaml" }));
        var model = Timed("model after open", () => body.Model);

        // Assert: everything was read, and nothing refused for size.
        Assert.False(model.Unreadable, string.Join("; ", model.Findings.Take(3).Select(finding => finding.ToString())));
        Assert.Equal(Rows, model.Elements.Count(element => element.Type == "Row"));
        Assert.Equal(Rows * Properties, model.Elements.Count(element => element.Type == "Cell"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"elements: {model.Elements.Count}, findings: {model.Findings.Count}"));
        foreach (var kind in model.Findings.GroupBy(finding => finding.Code).OrderByDescending(group => group.Count()))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"findings of {kind.Key}: {kind.Count()}, first: {kind.First()}"));
        }

        // Act: set one cell in the middle of the table.
        var middle = RowId(Rows / 2);
        var cell = Timed("find one cell", () => CellOf(model, middle, PropertyId(1)));
        var set = Timed("set one cell", () => body.Change(new ModelChange.Set(cell.Id, new Dictionary<string, object?> { ["text"] = "edited" })));

        // Assert: one small splice, and the value reads back in that row and no other.
        var planned = Assert.IsType<PlanResult.Planned>(set);
        Assert.Single(planned.Edit.Splices);
        var afterSet = Timed("model after set", () => body.Model);
        Assert.Equal("edited", CellOf(afterSet, middle, PropertyId(1)).Attributes["text"]);
        Assert.Equal(1, afterSet.Elements.Count(element => element.Type == "Cell" && Equals(element.Attributes.GetValueOrDefault("text"), "edited")));

        // Act: add one row.
        var add = Timed("add one row", () => body.Change(new ModelChange.Add("Row", "rnew", new Dictionary<string, object?>())));

        // Assert.
        Assert.IsType<PlanResult.Planned>(add);
        var afterAdd = Timed("model after add", () => body.Model);
        Assert.Equal(Rows + 1, afterAdd.Elements.Count(element => element.Type == "Row"));
        Assert.Equal("rnew", afterAdd.Elements.Last(element => element.Type == "Row").Id);

        // Act: remove one property, which cascades to its ten thousand cells.
        var remove = Timed("remove one property", () => body.Change(new ModelChange.Remove(PropertyId(7))));

        // Assert.
        var removed = Assert.IsType<PlanResult.Planned>(remove);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"splices to remove one property: {removed.Edit.Splices.Count}"));
        var afterRemove = Timed("model after remove", () => body.Model);
        Assert.DoesNotContain(afterRemove.Elements, element => element.Type == "Property" && element.Id == PropertyId(7));
        Assert.Equal(Rows * (Properties - 1), afterRemove.Elements.Count(element => element.Type == "Cell"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bytes after the edits: {body.Bytes.Length}"));
    }
}
