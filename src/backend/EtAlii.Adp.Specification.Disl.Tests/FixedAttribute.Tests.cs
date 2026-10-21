using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A <c>fixed</c> attribute (DISL §4.3): it always has its value, which CEL reads while <c>has()</c> is
/// false; it is read-only, a reader ignores a stored value and a writer never writes it; and a form
/// shows it only where it asks for it.
/// </summary>
public class FixedAttributeTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "types": {
            "Part": { "abstract": true, "attributes": {
              "name": { "type": "string" },
              "height": { "type": "number", "min": 1 },
              "sort": { "type": "string", "readOnly": true } } },
            "Box": { "extends": "Part", "attributes": {
              "height": { "type": "number", "fixed": 48, "readOnlyReasons": ["Every box is as high as the next."] },
              "sort": { "type": "string", "fixed": "box" } } },
            "Note": { "extends": "Part" }
          }
        },
        "forms": {
          "box": { "for": "Box", "items": [
            { "attribute": "name" },
            { "attribute": "height", "label": "Height" },
            { "attribute": "sort", "label": "Sort", "readOnlyReasons": ["The sort is the box's type."] } ] },
          "note": { "for": "Note", "items": [ { "attribute": "height", "label": "Height" } ] }
        },
        "persistence": { "typeMap": { "box": { "as": "Box" }, "note": { "as": "Note" } } }
        """));

    private static FblElement Element(string id, string type, IReadOnlyDictionary<string, object?> attributes) =>
        new(id, true, type, type, false, attributes, null, null, null, null, new Span(0, 0), 1);

    [Fact]
    public void AFixedAttribute_ReadsAsItsValue_AndHasIsFalse()
    {
        var diagram = new DislDiagram(Specification);
        var box = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });

        Assert.Equal(48.0, box.ValueOf("height"));
        Assert.Equal("box", box.ValueOf("sort"));
        Assert.Equal(true, Cel("self.height == 48.0 && self.sort == 'box' && !has(self.height) && !has(self.sort)", box));
    }

    [Fact]
    public void AStoredValue_IsIgnoredByTheReader()
    {
        var reading = new FblModel([Element("a", "box", new Dictionary<string, object?> { ["name"] = "A", ["height"] = 12.0, ["sort"] = "bag" })], [], false);

        var box = DislModelBuilder.From(reading, Specification).Diagram.Nodes.Single();

        Assert.Equal(["name"], box.Attributes.Keys);
        Assert.Empty(box.HostAttributes);
        Assert.Equal(48.0, box.ValueOf("height"));
    }

    [Fact]
    public void AFixedAttribute_IsNeverWritten()
    {
        var created = Assert.IsType<ModelChange.Add>(DislWrite.ToFbl(Specification, new DislChange.Create("Box", "a", new Dictionary<string, object?> { ["name"] = "A", ["height"] = 48.0, ["sort"] = "box" }, null, null)));
        var set = Assert.IsType<ModelChange.Set>(DislWrite.ToFbl(Specification, new DislChange.Set("a", "Box", new Dictionary<string, object?> { ["height"] = 60.0, ["name"] = "B" })));

        Assert.Equal(["name"], created.Attributes.Keys);
        Assert.Equal(["name"], set.Attributes.Keys);
    }

    [Fact]
    public void AFormThatAsks_ShowsTheValue_ReadOnly_ForTheFieldsReasonElseTheAttributes()
    {
        var diagram = new DislDiagram(Specification);
        var box = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });

        var rows = FormDerivation.Derive(Specification, box, new DislEnv(), WireIdMap.None).Select(row => $"{row.Id} | {row.Value} | {row.ReadOnlyReason}");

        Assert.Equal(["name | A | ", "height | 48 | Every box is as high as the next.", "sort | box | The sort is the box's type."], rows);
    }

    [Fact]
    public void ACommitToAFixedRow_IsRefused_WithItsReason()
    {
        var diagram = new DislDiagram(Specification);
        var box = diagram.AddNode("Box", "a", new Dictionary<string, object?> { ["name"] = "A" });

        var committed = FormCommit.Row(Specification, box, "height", "60", new DislEnv());

        Assert.Equal("Every box is as high as the next.", committed.Refusal);
    }

    private static object? Cel(string expression, DislElement self) =>
        Specification.Environment(DislContext.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = self.Diagram, ["env"] = new CelMap() });
}
