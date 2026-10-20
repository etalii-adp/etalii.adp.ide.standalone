using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Registers the <c>celFunctions</c> of every declared plugin (DISL §13.1.1) in the environment every
/// expression compiles against, before the user functions, so both they and the specification's
/// expressions may call them by their bare names.
/// </summary>
internal static partial class PluginFunctions
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleIdentifier();

    public static void Register(CelEnvironment environment, JsonElement root, DislPluginFunctions implementations, List<LoadDiagnostic> diagnostics)
    {
        foreach (var plugin in DislJson.Members(root, "plugins"))
        {
            if (!plugin.Value.TryGetProperty("celFunctions", out var functions) || functions.ValueKind != JsonValueKind.Array) continue;
            var index = 0;
            foreach (var declaration in functions.EnumerateArray())
            {
                var pointer = JsonPointer.Append(JsonPointer.Append(JsonPointer.Append("/plugins", plugin.Name), "celFunctions"), index++);
                if (Function(plugin.Name, declaration, pointer, environment, implementations, diagnostics) is { } function) environment.AddFunction(function);
            }
        }
    }

    private static CelFunction? Function(
        string plugin,
        JsonElement declaration,
        string pointer,
        CelEnvironment environment,
        DislPluginFunctions implementations,
        List<LoadDiagnostic> diagnostics)
    {
        var name = JsonProperties.String(declaration, "name") ?? "";
        if (!SimpleIdentifier().IsMatch(name))
        {
            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, "name"), $"'{name}' is not a simple identifier, so CEL cannot call it (DISL §13.1.1)."));
            return null;
        }
        if (environment.TryGetFunction(name, CelCallStyle.Global, out _))
        {
            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, "name"), $"'{name}' is already a function of the DISL library or of another plugin (DISL §13.1.1)."));
            return null;
        }

        // A parameter is a CEL type (the 0.1 form) or {name, type}; only a named one can be read by the fallback.
        var parameters = declaration.TryGetProperty("params", out var declared) && declared.ValueKind == JsonValueKind.Array
            ? declared.EnumerateArray().Select(parameter => parameter.ValueKind == JsonValueKind.Object ? JsonProperties.String(parameter, "name") : null).ToList()
            : [];
        var arity = parameters.Count;
        var cost = declaration.TryGetProperty("cost", out var declaredCost) && declaredCost.TryGetInt64(out var perCall) && perCall > 0 ? perCall : 1;
        var uses = JsonProperties.StringOrStrings(declaration, "uses");

        if (implementations.TryGet(name, out var implementation))
        {
            return new CelFunction(name, CelCallStyle.Global, arity, arity, call => implementation([.. call.Arguments.Select(Unwrapped)]), _ => cost);
        }

        if (JsonProperties.String(declaration, "fallback") is not { } fallback)
        {
            var absent = $"'{name}' is a function of the plugin '{plugin}', which this host does not have, and it declares no fallback (DISL §13.1.1).";
            return new CelFunction(name, CelCallStyle.Global, arity, arity, _ => throw new CelException(absent), _ => cost);
        }

        if (parameters.Any(parameter => parameter is null))
        {
            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, "params"), $"The fallback of '{name}' reads its parameters by name, so each is declared as {{name, type}} (DISL §13.1.1)."));
            return null;
        }

        CelProgram program;
        try
        {
            program = environment.Clone().DeclareVariables(parameters!).DeclareVariables(uses).Compile(fallback);
        }
        catch (CelException e)
        {
            diagnostics.Add(DislLoader.Error(JsonPointer.Append(pointer, "fallback"), e.Message));
            return null;
        }

        return new CelFunction(name, CelCallStyle.Global, arity, arity, call =>
        {
            var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var parameter = 0; parameter < arity; parameter++) variables[parameters[parameter]!] = call[parameter];
            foreach (var use in uses)
            {
                variables[use] = call.Variables.TryGetValue(use, out var value)
                    ? value
                    : throw new CelException($"'{name}' uses '{use}', which the expression calling it has no value for.");
            }
            var result = program.Evaluate(variables, call.Budget);
            return result is CelError error ? throw new CelException(error.Message) : result;
        }, _ => cost);
    }

    /// <summary>An element as the model holds it, also when a finding shows it under its written id.</summary>
    private static object? Unwrapped(object? argument) => argument is DislWrittenElement written ? written.Element : argument;
}
