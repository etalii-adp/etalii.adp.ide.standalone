using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// An operation whose body is a simulation (DISL §9.3, §9.6) is loaded like any other: its <c>initial</c>, <c>next</c>
/// and <c>until</c> are Expressions of the <c>simulation</c> context (§12.3), compiled at load, so a mistake in one is
/// refused there rather than passing unchecked until a run plays.
/// </summary>
public class SimulationLoadingTests
{
    private static string Simulating(string next) => Specifications.With($$"""
        "metamodel": { "types": { "Step": { "attributes": {
          "name": { "type": "string" },
          "played": { "type": "string", "transient": "viewer" } } } } },
        "behavior": { "operations": { "play": {
          "label": "Play (simulated)", "for": ["Step"],
          "simulate": {
            "for": ["Step"], "state": "played",
            "initial": "'pending'",
            "next": "{{next}}",
            "until": "step >= 2 * diagram.nodesOfType('Step').size()",
            "notice": { "running": "Playing", "finished": "Played" } } } } }
        """);

    [Theory]
    [InlineData("initial")]
    [InlineData("next")]
    [InlineData("until")]
    public void ASimulationsExpressions_AreCompiledInTheSimulationContext(string property)
    {
        var specification = Specifications.Loaded(Simulating("state[self.id] == 'pending' ? 'done' : state[self.id]"));

        Assert.Equal(DislContext.Simulation, specification.ExpressionAt($"/behavior/operations/play/simulate/{property}")?.Context);
    }

    [Fact]
    public void ASimulationsExpressions_SeeStateAndStep()
    {
        Assert.Empty(Specifications.Diagnostics(Simulating("step == 1 && state[self.id] == 'pending' ? 'done' : state[self.id]")));
    }

    [Fact]
    public void AMistakeInASimulation_IsRefusedAtLoad()
    {
        var result = DislLoader.Load(Simulating("state[self.id] == 'pending' ? notAFunction(self) : state[self.id]"));

        Assert.Null(result.Specification);
        Assert.Equal("/behavior/operations/play/simulate/next", Assert.Single(result.Errors).Pointer);
    }
}
