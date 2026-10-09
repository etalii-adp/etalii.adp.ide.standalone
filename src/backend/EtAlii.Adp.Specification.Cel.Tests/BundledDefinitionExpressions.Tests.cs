using System.Text.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>
/// Every CEL expression in the two bundled DISL definitions compiles with this engine - outside
/// <c>x-</c> extensions and <c>doc</c>, which are not CEL a runtime evaluates. The DISL library
/// functions the DISL runtime will register (§12.4: <c>isA</c>, <c>outgoingOf</c>, <c>yearMonth</c>,
/// <c>min</c> and the rest) are stubs here and nowhere else, and the definitions' own functions are
/// declared from their <c>functions</c> block with their parameters, so a function, macro or
/// syntax the engine lacks fails this test by name.
/// </summary>
public class BundledDefinitionExpressionsTests
{
    /// <summary>The variables of DISL §12.3's contexts, all of them: which one an expression is in is the DISL loader's to decide.</summary>
    private static readonly string[] ContextVariables =
    [
        "self", "diagram", "env", "item", "index", "group", "value", "yValue", "position", "p", "w", "h", "px", "py", "sw",
        "old", "event", "detail", "attribute", "newValue", "gesture", "newBounds", "violation", "count", "match",
        "source", "target", "sourceAnchor", "parent", "axis", "elementType", "dropTarget", "tool", "selection", "operationId",
    ];

    /// <summary>A GeomExpr string that is not CEL: a percentage of the dimension (DISL §2.5 c).</summary>
    private static readonly System.Text.RegularExpressions.Regex Percentage = new(@"^-?\d+(\.\d+)?%$");

    [Theory]
    [InlineData("gartner-hype-cycle-graph.dis", 250)]
    [InlineData("agent-behavior-modelling.dis", 80)]
    public void EveryExpressionInTheDefinitionCompiles(string resource, int atLeast)
    {
        // Arrange.
        using var document = JsonDocument.Parse(Read(resource));
        var root = document.RootElement;
        (CelEnvironment functionsEnvironment, List<(string Name, string[] Parameters, string[] Uses, string Body)> functions) = FunctionsOf(root);
        var environment = functionsEnvironment.Clone().DeclareVariables(ContextVariables).DeclareVariables(BindingsOf(root));
        var expressions = new List<(string Pointer, string Source)>();
        Collect(root, [], expressions);
        var failures = new List<string>();

        // Act.
        foreach ((string pointer, string source) in expressions)
        {
            try
            {
                environment.Compile(source);
            }
            catch (CelException e)
            {
                failures.Add($"{pointer}: {e.Message}");
            }
        }
        foreach ((string name, string[] parameters, string[] uses, string body) in functions)
        {
            try
            {
                functionsEnvironment.Clone().DeclareVariables(parameters).DeclareVariables(uses).Compile(body);
            }
            catch (CelException e)
            {
                failures.Add($"/functions/{name}/cel: {e.Message}");
            }
        }

        // Assert.
        Assert.Empty(failures);
        Assert.True(expressions.Count + functions.Count >= atLeast, $"Only {expressions.Count + functions.Count} expressions were found in {resource}; the walk has lost some.");
    }

    [Fact]
    public void AnExpressionUsingAFunctionNoOneOffersFails()
    {
        // Arrange: the same environment the definitions compile in, given a function nobody declared.
        using var document = JsonDocument.Parse(Read("gartner-hype-cycle-graph.dis"));
        (CelEnvironment environment, _) = FunctionsOf(document.RootElement);

        // Act.
        var refused = Assert.Throws<CelException>(() => environment.Clone().DeclareVariables(ContextVariables).Compile("self.name.soundex() == ''"));

        // Assert.
        Assert.Equal("The function 'soundex' is not supported by this CEL evaluator.", refused.Message);
    }

    /// <summary>
    /// The standard environment, the DISL library as stubs, and the definition's functions, each
    /// declared with its parameter count; and each function's parameters, <c>uses</c> and body.
    /// </summary>
    private static (CelEnvironment Environment, List<(string Name, string[] Parameters, string[] Uses, string Body)> Functions) FunctionsOf(JsonElement root)
    {
        var environment = CelEnvironment.Standard();
        foreach (var stub in DislLibraryStubs()) environment.AddFunction(stub);
        var functions = new List<(string, string[], string[], string)>();
        if (root.TryGetProperty("functions", out var declared))
        {
            foreach (var function in declared.EnumerateObject())
            {
                var parameters = function.Value.GetProperty("params").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray();
                var uses = function.Value.TryGetProperty("uses", out var u) ? u.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
                environment.AddFunction(CelFunction.Global(function.Name, parameters.Length, _ => throw new CelException("A stub.")));
                functions.Add((function.Name, parameters, uses, function.Value.GetProperty("cel").GetString()!));
            }
        }
        return (environment, functions);
    }

    /// <summary>The DISL §12.4 functions and §12.2 methods the two definitions call, which the DISL runtime will register.</summary>
    private static IEnumerable<CelFunction> DislLibraryStubs()
    {
        yield return CelFunction.Method("isA", 1, 1);
        yield return CelFunction.Method("outgoingOf", 1, 1);
        yield return CelFunction.Method("incomingOf", 1, 1);
        yield return CelFunction.Method("nodesOfType", 1, 2);
        yield return CelFunction.Method("positionIn", 1, 1);
        yield return CelFunction.Method("descendants", 0, 0);
        yield return CelFunction.Method("year", 0, 0);
        yield return CelFunction.Method("month", 0, 0);
        yield return new CelFunction("enumLabel", CelCallStyle.Global, 2, 2, Stub);
        yield return new CelFunction("yearMonth", CelCallStyle.Global, 2, 2, Stub);
        yield return new CelFunction("formatYearMonth", CelCallStyle.Global, 2, 2, Stub);
        yield return new CelFunction("parseYearMonth", CelCallStyle.Global, 1, 1, Stub);
        yield return new CelFunction("min", CelCallStyle.Global, 2, 4, Stub);
        yield return new CelFunction("max", CelCallStyle.Global, 2, 4, Stub);
        yield return new CelFunction("clamp", CelCallStyle.Global, 3, 3, Stub);
        yield return new CelFunction("textWidth", CelCallStyle.Global, 2, 2, Stub);
        yield break;
        static object Stub(CelCall _) => throw new CelException("A stub.");
    }

    /// <summary>The names actions bind: an action's <c>as</c>, a context tool's <c>as</c>, and the keys of a <c>let</c>.</summary>
    private static IEnumerable<string> BindingsOf(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.StartsWith("x-", StringComparison.Ordinal) || property.Name == "doc") continue;
                    if (property is { Name: "as", Value.ValueKind: JsonValueKind.String }) yield return property.Value.GetString()!;
                    if (property is { Name: "let", Value.ValueKind: JsonValueKind.Object })
                    {
                        foreach (var bound in property.Value.EnumerateObject()) yield return bound.Name;
                    }
                    foreach (var name in BindingsOf(property.Value)) yield return name;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var name in BindingsOf(item)) yield return name;
                }
                break;
        }
    }

    /// <summary>
    /// Every string the DISL schema makes CEL, outside <c>x-</c> and <c>doc</c>: a <c>cel</c> member
    /// (an Expression object or a Bindable), the Expression-typed members, GeomExprs in shapes, every
    /// value position of an action (DISL §2.5 d) but its keywords and names, and the members of
    /// derived relations, retyping, ids and context-tool arguments that are Expressions, and a form
    /// item's <c>optionLabel</c> and <c>accepts</c> (DISL 0.3 §7.5). The <c>functions</c> block is
    /// compiled separately, over each function's parameters.
    /// </summary>
    private static void Collect(JsonElement element, List<string> path, List<(string, string)> expressions)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.StartsWith("x-", StringComparison.Ordinal) || property.Name == "doc") continue;
                    if (path.Count == 0 && property.Name == "functions") continue;
                    path.Add(property.Name);
                    Collect(property.Value, path, expressions);
                    path.RemoveAt(path.Count - 1);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    path.Add((index++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Collect(item, path, expressions);
                    path.RemoveAt(path.Count - 1);
                }
                break;
            case JsonValueKind.String:
                if (IsCel(path, element.GetString()!)) expressions.Add(("/" + string.Join("/", path), element.GetString()!));
                break;
        }
    }

    private static bool IsCel(List<string> path, string value)
    {
        var names = path.Where(segment => !segment.All(char.IsAsciiDigit)).ToList();
        var key = names[^1];
        var parentKey = names.Count > 1 ? names[^2] : "";
        if (key == "cel") return true;
        if (key is "when" or "visible" or "rule" or "keep" or "forEach" or "enabled" or "initial" or "atMaxDepth") return true;
        if (key == "count" && parentKey == "confirm") return true;
        if (names[0] == "constraints" && key == "target") return true;
        if (names is ["persistence", "ids", "expression"]) return true;
        if (names is ["metamodel", "relations", _, "derived", "from" or "source" or "target" or "sources"]) return true;
        if (names is ["behavior", "retype", _, "attributeMapping", _]) return true;
        if (parentKey == "args") return true;
        if (names[0] == "forms" && key is "optionLabel" or "accepts") return true;
        if (names.Contains("actions") || names.Contains("write"))
        {
            // Every value position of an action is CEL, except its keywords and names (DISL §2.5 d) and a layout action's refusals, which are Messages.
            if (parentKey == "refusals" && names.Count > 2 && names[^3] == "layout") return false;
            return !(key is "as" or "algorithm" or "unset" or "call" or "plugin" or "severity" or "form" or "place"
                || (key == "label" && parentKey == "editLabel"));
        }
        if (names is ["notation", "shapes", ..] && key is "x" or "y" or "w" or "h" or "rx" or "ry" or "x1" or "y1" or "x2" or "y2" or "cx" or "cy" or "r")
        {
            return !Percentage.IsMatch(value);
        }
        return false;
    }

    private static string Read(string resource)
    {
        using var stream = typeof(BundledDefinitionExpressionsTests).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The test assembly does not embed {resource}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
