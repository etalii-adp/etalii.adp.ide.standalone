using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A typed value past an attribute's <c>min</c> or <c>max</c> (DISL 0.3 §4.3, <c>outOfRange</c>): committed through
/// a form row, it is refused with <c>message</c> under <c>typed: "refuse"</c>, the default, or with the runtime's
/// sentence when there is no message, and stopped at the bound under <c>typed: "clamp"</c>; the bound is the
/// attribute's own or its data type's.
/// </summary>
public class TypedOutOfRangeTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": {
          "dataTypes": { "Fraction": { "base": "number", "min": 0, "max": 1 } },
          "types": { "Dot": { "attributes": {
            "across": { "type": "Fraction", "outOfRange": { "drag": "clamp", "typed": "refuse",
              "message": { "cel": "string(newValue) + ' is off the map; ' + attribute + ' runs from ' + string(min) + ' to ' + string(max) + '.'" } } },
            "up": { "type": "number", "min": 0, "max": 10, "outOfRange": { "typed": "clamp" } },
            "deep": { "type": "number", "min": 0 } } } }
        },
        "forms": { "dot": { "for": "Dot", "items": [
          { "attribute": "across", "label": "Across", "parse": { "write": [ { "set": { "across": "double(value)" } } ] } },
          { "attribute": "up", "label": "Up", "parse": { "write": [ { "set": { "up": "double(value)" } } ] } },
          { "attribute": "deep", "label": "Deep", "parse": { "write": [ { "set": { "deep": "double(value)" } } ] } } ] } }
        """));

    private static DislElement Dot()
    {
        var diagram = new DislDiagram(Specification);
        return diagram.AddNode("Dot", "d", new Dictionary<string, object?> { ["across"] = 0.5, ["up"] = 5.0, ["deep"] = 1.0 });
    }

    private static string Commit(string row, string value)
    {
        var transaction = FormCommit.Row(Specification, Dot(), row, value, new DislEnv());
        return transaction.Refusal ?? string.Join(" ", transaction.Changes.OfType<DislChange.Set>().SelectMany(set => set.Attributes.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    [Theory]
    [InlineData("1.5", "1.5 is off the map; across runs from 0 to 1.")]
    [InlineData("-0.25", "-0.25 is off the map; across runs from 0 to 1.")]
    public void PastADataTypesBound_ATypedValueIsRefused_WithTheMessage(string value, string refusal) =>
        Assert.Equal(refusal, Commit("across", value));

    [Theory]
    [InlineData("1", "across=1")]
    [InlineData("0", "across=0")]
    [InlineData("0.75", "across=0.75")]
    public void WithinTheBounds_ATypedValueIsWritten(string value, string written) =>
        Assert.Equal(written, Commit("across", value));

    [Theory]
    [InlineData("12", "up=10")]
    [InlineData("-3", "up=0")]
    public void UnderClamp_ATypedValueStopsAtTheBound(string value, string written) =>
        Assert.Equal(written, Commit("up", value));

    [Fact]
    public void WithoutOutOfRange_ATypedValueIsRefused_WithTheRuntimesSentence() =>
        Assert.Equal("'d' cannot have deep -1; the minimum is 0.", Commit("deep", "-1"));
}
