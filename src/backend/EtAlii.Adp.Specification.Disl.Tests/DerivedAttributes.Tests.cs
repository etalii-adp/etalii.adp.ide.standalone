using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// An attribute with a <c>derived</c> expression (DISL §4.3) is computed, never stored: evaluated in
/// the <c>element</c> context with <c>self</c> the element, whoever reads it - CEL, a form, a menu.
/// </summary>
public class DerivedAttributesTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "functions": {
          "twice": { "params": [{ "name": "n", "type": "int" }], "returns": "int", "cel": "n * 2" }
        },
        "metamodel": {
          "types": {
            "Crate": { "attributes": {
              "name": { "type": "string" },
              "shout": { "type": "string", "derived": "self.name.upperAscii()" },
              "echo": { "type": "string", "derived": "self.shout + '!'" },
              "arrivals": { "type": "int", "derived": "twice(self.incoming.size())" },
              "broken": { "type": "int", "derived": "1 / 0" },
              "itself": { "type": "string", "derived": "self.itself" } } },
            "Pallet": { "extends": "Crate" } },
          "relations": {
            "Belt": { "source": "Crate", "target": "Crate", "attributes": {
              "route": { "type": "string", "derived": "self.source.name + '>' + self.target.name" } } } } },
        "forms": {
          "crate": { "for": "Crate", "items": [ { "attribute": "shout" }, { "kind": "computed", "label": "Echo", "value": { "cel": "self.echo" } } ] }
        }
        """));

    private readonly DislDiagram _diagram = new(Specification);
    private readonly DislElement _first;
    private readonly DislElement _second;
    private readonly DislElement _belt;

    public DerivedAttributesTests()
    {
        _first = _diagram.AddNode("Crate", "first", new Dictionary<string, object?> { ["name"] = "apples" });
        _second = _diagram.AddNode("Pallet", "second", new Dictionary<string, object?> { ["name"] = "pears" });
        _belt = _diagram.AddRelation("Belt", "belt", _first, _second);
    }

    private object? Evaluate(string expression, DislElement self) =>
        Specification.Environment(DislContexts.Element).Compile(expression)
            .Evaluate(new Dictionary<string, object?> { ["self"] = self, ["diagram"] = _diagram, ["env"] = new DislEnv().ToCel() });

    [Fact]
    public void ADerivedAttribute_ReadsAsItsExpression() =>
        Assert.Equal("APPLES", _first.ValueOf("shout"));

    [Fact]
    public void ADerivedAttribute_IsComputedForTheElementReadingIt_InheritedOnes_Included() =>
        Assert.Equal("PEARS", _second.ValueOf("shout"));

    [Fact]
    public void ADerivedAttribute_MayReadAnother_AndUserFunctions_AndTheModel()
    {
        Assert.Equal("APPLES!", _first.ValueOf("echo"));
        Assert.Equal(0L, _first.ValueOf("arrivals"));
        Assert.Equal(2L, _second.ValueOf("arrivals"));
    }

    [Fact]
    public void ARelationsDerivedAttribute_IsComputedToo() =>
        Assert.Equal("apples>pears", _belt.ValueOf("route"));

    [Fact]
    public void CelReadsADerivedAttribute_AndHasHoldsForIt()
    {
        Assert.Equal("APPLES!", Evaluate("self.echo", _first));
        Assert.Equal(true, Evaluate("has(self.shout)", _first));
    }

    [Fact]
    public void ADerivedAttribute_IsNotStored_EvenOnceRead()
    {
        // Act.
        _ = _first.ValueOf("shout");

        // Assert.
        Assert.DoesNotContain("shout", _first.Attributes.Keys);
    }

    [Fact]
    public void ADerivedAttributeThatFails_ReadsAsItsTypesZeroValue()
    {
        Assert.Equal(0L, _first.ValueOf("broken"));
        Assert.Equal("", _first.ValueOf("itself"));
    }

    [Fact]
    public void AFormRow_ShowsTheDerivedValue()
    {
        // Act.
        var rows = FormDerivation.Derive(Specification, _first, new DislEnv(), WireIdMap.None);

        // Assert.
        Assert.Equal(["APPLES", "APPLES!"], rows.Select(row => row.Value));
    }

    [Fact]
    public void AnExpressionReadingAFailingDerivedAttribute_StillEvaluates() =>
        Assert.IsNotType<CelError>(Evaluate("self.broken + 1", _first));
}
