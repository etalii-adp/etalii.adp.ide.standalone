using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The attribute names of a <c>create</c>'s <c>attributes</c> and of a <c>set</c> are names, and their values
/// Expressions (DISL §2.5 d, §9.4), also for an attribute called like an action keyword (<c>form</c>, <c>place</c>,
/// <c>severity</c>, …), and inside an <c>if</c>'s branches.
/// </summary>
public class ActionAttributeNamesTests
{
    private const string Parts = """
        "metamodel": { "types": { "Thing": { "attributes": {
          "name": { "type": "string" }, "form": { "type": "string" }, "severity": { "type": "string" } } } } },
        "behavior": { "operations": {
          "add": { "label": "Add", "for": "diagram", "actions": [
            { "if": "size(diagram.nodes) == 0",
              "then": [ { "create": { "type": "'Thing'", "attributes": { "name": "'first'", "form": "'nested'" } }, "as": "made" } ],
              "else": [ { "create": { "type": "'Thing'", "attributes": { "name": "'later'", "form": "'legacy'" } }, "as": "made" } ] },
            { "set": { "severity": "'high'" }, "target": "made" } ] } } }
        """;

    [Fact]
    public void AnAttributeCalledLikeAnActionKeyword_Loads() =>
        Assert.Empty(DislLoader.Load(Specifications.With(Parts)).Errors);

    [Fact]
    public void ItsValue_IsEvaluated()
    {
        var specification = Specifications.Loaded(Specifications.With(Parts));
        var diagram = new DislDiagram(specification);

        var transaction = OperationInterpreter.Run(specification, "add", diagram, null, DislIds.Fixed("t"));

        Assert.Null(transaction.Refusal);
        var made = diagram.ElementById("t")!;
        Assert.Equal("nested", made.ValueOf("form"));
        Assert.Equal("high", made.ValueOf("severity"));
    }
}
