using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>Host objects (<see cref="ICelObject"/>): member access, <c>has()</c>, method dispatch and the macros over lists of them.</summary>
public class CelObjectsTests
{
    private static readonly HostObject Trend = new("Trend", new Dictionary<string, object?> { ["name"] = "AI", ["start"] = 24000L, ["peakEnd"] = HostObject.Absent });
    private static readonly HostObject Trigger = new("Trigger", new Dictionary<string, object?> { ["name"] = "Launch", ["date"] = 23990L });
    private static readonly HostObject Note = new("Note", new Dictionary<string, object?> { ["text"] = "" });

    [Theory]
    [InlineData("self.name", "AI")]
    [InlineData("self.start + 1", "24001")]
    [InlineData("self.type", "Trend")]
    [InlineData("has(self.start)", "true")]
    [InlineData("has(self.peakEnd)", "false")]
    [InlineData("has(self.nothing)", "false")]
    [InlineData("self.nothing", "error: No such field: 'nothing'.")]
    [InlineData("self.greet('Ada')", "hello Ada from Trend")]
    [InlineData("self.isA('Trend') && !self.isA('Note')", "true")]
    public void AnObjectIsSelectedFromTestedAndCalled(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression, new Dictionary<string, object?> { ["self"] = Trend }, Environment());

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Fact]
    public void AMethodTheObjectDoesNotImplementFallsBackToTheRegisteredFunction_OrFails()
    {
        // Arrange: size() is a registered function the object does not implement; label() is declared for objects only.
        var environment = Environment().AddFunction(CelFunction.Method("label", 0, 0));

        // Act.
        var size = Evaluate.Expression("'abc'.size()", environment: environment);
        var refused = Evaluate.Expression("self.label()", new Dictionary<string, object?> { ["self"] = Trend }, environment);
        var notAnObject = Evaluate.Expression("'abc'.label()", environment: environment);

        // Assert.
        Assert.Equal(3L, size);
        Assert.Equal("error: 'label()' is not a method of this value.", Evaluate.Text(refused));
        Assert.Equal("error: 'label()' is not a method of this value.", Evaluate.Text(notAnObject));
    }

    [Theory]
    [InlineData("nodes.map(n, n.type)", "[Trend,Trigger,Note]")]
    [InlineData("nodes.filter(n, has(n.name)).map(n, n.name)", "[AI,Launch]")]
    [InlineData("nodes.exists(n, n.isA('Note'))", "true")]
    [InlineData("nodes.all(n, has(n.type))", "true")]
    [InlineData("nodes.exists_one(n, n.isA('Trigger'))", "true")]
    [InlineData("nodes.filter(n, n.isA('Trend'))[0].name", "AI")]
    [InlineData("nodes.size()", "3")]
    [InlineData("nodes[1] in nodes", "true")]
    public void TheMacrosWorkOverListsOfObjects(string expression, string expected)
    {
        // Arrange: a typed list, which is a list of objects only through covariance.
        IReadOnlyList<HostObject> nodes = [Trend, Trigger, Note];

        // Act.
        var value = Evaluate.Expression(expression, new Dictionary<string, object?> { ["nodes"] = nodes }, Environment());

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Fact]
    public void AnObjectEqualsItselfAndNoOther()
    {
        // Act.
        var value = Evaluate.Expression("a == a && a != b", new Dictionary<string, object?> { ["a"] = Trend, ["b"] = Trigger }, Environment());

        // Assert.
        Assert.Equal(true, value);
    }

    private static CelEnvironment Environment() => CelEnvironment.Standard()
        .AddFunction(CelFunction.Method("greet", 1, 1))
        .AddFunction(CelFunction.Method("isA", 1, 1));
}
