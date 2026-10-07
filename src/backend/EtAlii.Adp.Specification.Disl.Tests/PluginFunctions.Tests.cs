using System.Text;
using EtAlii.Adp.Specification.Cel;
using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// Plugin functions in CEL (DISL §13.1.1): the host's implementation when it has one, the declared
/// fallback when it does not, and an evaluation error when there is neither.
/// </summary>
public class PluginFunctionsTests
{
    private const string Plugin = """
        "plugins": { "org.example.words": { "version": "^1.0.0", "provides": ["celFunctions"], "required": false,
          "celFunctions": [
            { "name": "shout", "params": [ { "name": "text", "type": "string" } ], "returns": "string", "cost": 3,
              "fallback": "text + '!'" },
            { "name": "secret", "params": [ "string" ], "returns": "string" } ] } },
        "functions": { "twiceShouted": { "params": [ { "name": "text", "type": "string" } ], "returns": "string", "cel": "shout(shout(text))" } }
        """;

    private static DislSpecification Loaded(DislPluginFunctions plugins)
    {
        var result = DislLoader.Load(Encoding.UTF8.GetBytes(Specifications.With(Plugin)), plugins);
        return result.Specification ?? throw new InvalidOperationException("The specification does not load: " + string.Join("; ", result.Diagnostics));
    }

    private static object? Evaluate(DislSpecification specification, string expression) =>
        specification.Environment(DislContexts.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

    [Fact]
    public void TheHostsImplementation_IsCalled_ByUserFunctionsToo()
    {
        var specification = Loaded(new DislPluginFunctions().Add("shout", arguments => ((string)arguments[0]!).ToUpperInvariant()));

        Assert.Equal("HEY", Evaluate(specification, "shout('hey')"));
        Assert.Equal("HEY", Evaluate(specification, "twiceShouted('hey')"));
    }

    [Fact]
    public void WithoutAnImplementation_TheFallbackIsEvaluated()
    {
        var specification = Loaded(DislPluginFunctions.None);

        Assert.Equal("hey!", Evaluate(specification, "shout('hey')"));
        Assert.Equal("hey!!", Evaluate(specification, "twiceShouted('hey')"));
    }

    [Fact]
    public void WithoutAnImplementationOrAFallback_ACallIsAnEvaluationError()
    {
        var specification = Loaded(DislPluginFunctions.None);

        var error = Assert.IsType<CelError>(Evaluate(specification, "secret('x')"));
        Assert.Contains("org.example.words", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnElementArgument_ReachesTheHostAsTheModelsElement_AlsoUnderItsWrittenId()
    {
        DislElement? received = null;
        var json = Specifications.With("""
            "plugins": { "org.example.ids": { "provides": ["celFunctions"],
              "celFunctions": [ { "name": "seen", "params": [ { "name": "element", "type": "dyn" } ], "returns": "bool" } ] } },
            "constraints": { "rules": [ { "id": "always", "scope": "Thing", "rule": "seen(self)" } ] }
            """);
        var specification = DislLoader.Load(Encoding.UTF8.GetBytes(json), new DislPluginFunctions().Add("seen", arguments =>
        {
            received = arguments[0] as DislElement;
            return false;
        })).Specification!;
        var diagram = new DislDiagram(specification);
        var thing = diagram.Add(new DislElement(diagram, specification.Metamodel.TypeOf("Thing")!, "ephemeral#1", new Dictionary<string, object?>()));

        var finding = Assert.Single(ConstraintEvaluator.Evaluate(specification, diagram, new DislConstraintOptions(WrittenId: _ => "written")));

        Assert.Same(thing, received);
        Assert.Equal(["written"], finding.ElementIds);
    }

    [Fact]
    public void ANameThatClashes_OrAFallbackOverUnnamedParameters_IsRefused()
    {
        var clash = Specifications.With("""
            "plugins": { "org.example.a": { "celFunctions": [ { "name": "size", "params": [ "string" ], "returns": "int" } ] } }
            """);
        var unnamed = Specifications.With("""
            "plugins": { "org.example.b": { "celFunctions": [ { "name": "echo", "params": [ "string" ], "returns": "string", "fallback": "'x'" } ] } }
            """);

        Assert.Equal(["error at /plugins/org.example.a/celFunctions/0/name: 'size' is already a function of the DISL library or of another plugin (DISL §13.1.1)."], Specifications.Diagnostics(clash));
        Assert.Equal(["error at /plugins/org.example.b/celFunctions/0/params: The fallback of 'echo' reads its parameters by name, so each is declared as {name, type} (DISL §13.1.1)."], Specifications.Diagnostics(unnamed));
    }
}
