using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A relation type inherits <c>directed</c>, <c>allowSelfLoops</c> and <c>allowParallel</c> along its C3 linearisation,
/// else the DISL §4.9 default, and may only narrow them; supertypes that differ in a flag the type does not state are a
/// specification error (DISL 0.6, §4.7). Checked against <c>std.endpoints</c> (§8.7) both as a finding and as the refusal
/// of a connect gesture (§8.4), and against etalii.adp's example and refused specifications, vendored unchanged.
/// </summary>
public class RelationFlagInheritanceTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "metamodel": {
          "types": { "Box": {} },
          "relations": {
            "Once": { "abstract": true, "source": "Box", "target": "Box", "allowParallel": false },
            "OnceChild": { "extends": "Once" },
            "Loops": { "abstract": true, "source": "Box", "target": "Box", "allowSelfLoops": true },
            "LoopsChild": { "extends": "Loops" },
            "Mutual": { "abstract": true, "source": "Box", "target": "Box", "directed": false, "allowParallel": false },
            "MutualChild": { "extends": "Mutual" }
          }
        },
        "constraints": { "builtIn": { "std.endpoints": { "message": { "cel": "detail.violation" } } } }
        """));

    private static string Violations(string relationType, bool loop, bool opposite = false)
    {
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Box", "a");
        var b = loop ? a : diagram.AddNode("Box", "b");
        diagram.AddRelation(relationType, "first", a, b);
        if (!loop) diagram.AddRelation(relationType, "second", opposite ? b : a, opposite ? a : b);
        return string.Join(",", ConstraintEvaluator.Evaluate(Specification, diagram)
            .Where(finding => finding.ConstraintId == "std.endpoints")
            .Select(finding => finding.Message + "@" + string.Join("|", finding.ElementIds)));
    }

    private static string Refusal(string relationType, bool loop)
    {
        var diagram = new DislDiagram(Specification);
        var a = diagram.AddNode("Box", "a");
        var b = loop ? a : diagram.AddNode("Box", "b");
        if (!loop) diagram.AddRelation(relationType, "first", a, b);
        return GestureConstraintEvaluator.Connect(Specification, diagram, relationType, a, b)?.Message ?? "";
    }

    [Fact]
    public void ASecondRelationOnOnePair_OfASubtypeThatInheritsAllowParallelFalse_IsReportedAsParallel() =>
        Assert.Equal("parallel@second", Violations("OnceChild", loop: false));

    [Fact]
    public void ConnectingASecondRelationOnOnePair_OfASubtypeThatInheritsAllowParallelFalse_IsRefused() =>
        Assert.Equal("A OnceChild already connects these two elements.", Refusal("OnceChild", loop: false));

    [Fact]
    public void ALoopOfASubtypeThatInheritsAllowSelfLoopsTrue_IsNotReported() =>
        Assert.Equal("", Violations("LoopsChild", loop: true));

    [Fact]
    public void ConnectingAnElementToItself_WithASubtypeThatInheritsAllowSelfLoopsTrue_IsNotRefused() =>
        Assert.Equal("", Refusal("LoopsChild", loop: true));

    [Fact]
    public void AnOppositePair_OfASubtypeThatInheritsDirectedFalseAndAllowParallelFalse_IsReportedAsParallel() =>
        Assert.Equal("parallel@second", Violations("MutualChild", loop: false, opposite: true));

    /// <summary>
    /// The definition of etalii.adp's <c>service-calls.did</c>, built on the vendored <c>service-calls.dis</c>, gives
    /// exactly the two findings of <c>service-calls.findings.json</c>, in its order.
    /// </summary>
    [Fact]
    public void TheServiceCallsExample_GivesTheTwoFindingsOfItsFindingsFile()
    {
        // Arrange.
        var specification = Specifications.Loaded(Resources.Text(Resources.ServiceCalls));
        var diagram = new DislDiagram(specification);
        var checkout = diagram.AddNode("Service", "checkout", new Dictionary<string, object?> { ["name"] = "Checkout" });
        var payments = diagram.AddNode("Service", "payments", new Dictionary<string, object?> { ["name"] = "Payments" });
        var ledger = diagram.AddNode("Service", "ledger", new Dictionary<string, object?> { ["name"] = "Ledger" });
        diagram.AddRelation("SyncCall", "call-1", checkout, payments);
        diagram.AddRelation("SyncCall", "call-2", checkout, payments);
        diagram.AddRelation("AsyncCall", "call-3", checkout, payments);
        diagram.AddRelation("SyncCall", "call-4", ledger, ledger);
        diagram.AddRelation("TracedCall", "call-5", payments, payments);

        // Act.
        var findings = ConstraintEvaluator.Evaluate(specification, diagram)
            .Select(finding => $"{finding.ConstraintId} {finding.Severity} {finding.ElementIds[0]}: {finding.Message}");

        // Assert.
        Assert.Equal(
        [
            "std.endpoints error call-2: A SyncCall already connects these two elements.",
            "std.endpoints error call-5: A TracedCall cannot connect an element to itself.",
        ], findings);
    }

    /// <summary>Each specification etalii.adp keeps in <c>specifications/disl/refused/</c> fails to load, with one error naming its relation type and flag.</summary>
    [Theory]
    [InlineData(Resources.RelationFlagWidened, "/metamodel/relations/Strong/allowParallel", "'Strong'", "allowParallel")]
    [InlineData(Resources.RelationFlagDirected, "/metamodel/relations/Mutual/directed", "'Mutual'", "directed")]
    [InlineData(Resources.RelationFlagConflict, "/metamodel/relations/Both", "'Both'", "allowSelfLoops")]
    public void ARefusedSpecification_FailsToLoadWithOneErrorNamingItsTypeAndFlag(string resource, string pointer, string type, string flag)
    {
        // Act.
        var result = DislLoader.Load(Resources.Text(resource));

        // Assert.
        Assert.Null(result.Specification);
        var error = Assert.Single(result.Errors);
        Assert.Equal(pointer, error.Pointer);
        Assert.Contains(type, error.Message, StringComparison.Ordinal);
        Assert.Contains(flag, error.Message, StringComparison.Ordinal);
        Assert.Contains("DISL §4.7", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The vendored <c>service-calls.dis</c> loads, and each relation type has the effective flags of spec 034's data model.</summary>
    [Theory]
    [InlineData("Call", true, true, false)]
    [InlineData("SyncCall", true, true, false)]
    [InlineData("AsyncCall", true, true, false)]
    [InlineData("Observation", true, false, true)]
    [InlineData("TracedCall", true, false, false)]
    public void TheServiceCallsExample_HasTheEffectiveFlagsOfTheDataModel(string relationType, bool directed, bool allowSelfLoops, bool allowParallel)
    {
        // Act.
        var type = Specifications.Loaded(Resources.Text(Resources.ServiceCalls)).Metamodel.Relations[relationType];

        // Assert.
        Assert.Equal((directed, allowSelfLoops, allowParallel), (type.Directed, type.AllowSelfLoops, type.AllowParallel));
    }
}
