using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// The host's implementations of the CEL functions a specification's plugins declare (DISL §13.1.1),
/// keyed by function name, handed to <see cref="DislLoader"/> when the specification is loaded.
/// </summary>
/// <remarks>
/// <para>
/// <b>A host implements the functions of a plugin it has</b>, and a function it does not implement is
/// a function of a plugin that is absent: its <c>fallback</c> is evaluated in its place, and without
/// one a call is an evaluation error, so a constraint that calls it reports that it could not be
/// evaluated rather than passing.
/// </para>
/// <para>
/// <b>An implementation receives the call's arguments as CEL values</b>: an element is the model's
/// <see cref="DislElement"/>, also where a finding's view shows it with the id it is written with. It
/// <b>MUST NOT</b> change the model or anything CEL can see (§13.1.1); one that throws a
/// <see cref="CelException"/> makes the call an evaluation error with that message.
/// </para>
/// </remarks>
public sealed class DislPluginFunctions
{
    private readonly Dictionary<string, Func<IReadOnlyList<object?>, object?>> _implementations = new(StringComparer.Ordinal);

    /// <summary>No implementations: every plugin is absent.</summary>
    public static DislPluginFunctions None { get; } = new();

    /// <summary>Implements the plugin function <paramref name="name"/>; answers this, for chaining.</summary>
    public DislPluginFunctions Add(string name, Func<IReadOnlyList<object?>, object?> implementation)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(implementation);
        if (ReferenceEquals(this, None)) throw new InvalidOperationException("DislPluginFunctions.None holds no implementations; create a new instance.");
        _implementations[name] = implementation;
        return this;
    }

    internal bool TryGet(string name, out Func<IReadOnlyList<object?>, object?> implementation) =>
        _implementations.TryGetValue(name, out implementation!);
}

/// <summary>
/// Registers the <c>celFunctions</c> of every declared plugin (DISL §13.1.1) in the environment every
/// expression compiles against, before the user functions, so both they and the specification's
/// expressions may call them by their bare names.
/// </summary>
internal static partial class PluginFunctions
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleIdentifier();

    public static void Register(CelEnvironment environment, JsonElement root, DislPluginFunctions implementations, List<DislDiagnostic> diagnostics)
    {
        foreach (var plugin in DislJson.Members(root, "plugins"))
        {
            if (!plugin.Value.TryGetProperty("celFunctions", out var functions) || functions.ValueKind != JsonValueKind.Array) continue;
            var index = 0;
            foreach (var declaration in functions.EnumerateArray())
            {
                var pointer = DislJson.Pointer(DislJson.Pointer(DislJson.Pointer("/plugins", plugin.Name), "celFunctions"), index++);
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
        List<DislDiagnostic> diagnostics)
    {
        var name = DislJson.String(declaration, "name") ?? "";
        if (!SimpleIdentifier().IsMatch(name))
        {
            diagnostics.Add(DislLoader.Error(DislJson.Pointer(pointer, "name"), $"'{name}' is not a simple identifier, so CEL cannot call it (DISL §13.1.1)."));
            return null;
        }
        if (environment.TryGetFunction(name, CelCallStyle.Global, out _))
        {
            diagnostics.Add(DislLoader.Error(DislJson.Pointer(pointer, "name"), $"'{name}' is already a function of the DISL library or of another plugin (DISL §13.1.1)."));
            return null;
        }

        // A parameter is a CEL type (the 0.1 form) or {name, type}; only a named one can be read by the fallback.
        var parameters = declaration.TryGetProperty("params", out var declared) && declared.ValueKind == JsonValueKind.Array
            ? declared.EnumerateArray().Select(parameter => parameter.ValueKind == JsonValueKind.Object ? DislJson.String(parameter, "name") : null).ToList()
            : [];
        var arity = parameters.Count;
        var cost = declaration.TryGetProperty("cost", out var declaredCost) && declaredCost.TryGetInt64(out var perCall) && perCall > 0 ? perCall : 1;
        var uses = DislJson.Strings(declaration, "uses");

        if (implementations.TryGet(name, out var implementation))
        {
            return new CelFunction(name, CelCallStyle.Global, arity, arity, call => implementation([.. call.Arguments.Select(Unwrapped)]), _ => cost);
        }

        if (DislJson.String(declaration, "fallback") is not { } fallback)
        {
            var absent = $"'{name}' is a function of the plugin '{plugin}', which this host does not have, and it declares no fallback (DISL §13.1.1).";
            return new CelFunction(name, CelCallStyle.Global, arity, arity, _ => throw new CelException(absent), _ => cost);
        }

        if (parameters.Any(parameter => parameter is null))
        {
            diagnostics.Add(DislLoader.Error(DislJson.Pointer(pointer, "params"), $"The fallback of '{name}' reads its parameters by name, so each is declared as {{name, type}} (DISL §13.1.1)."));
            return null;
        }

        CelProgram program;
        try
        {
            program = environment.Clone().DeclareVariables(parameters!).DeclareVariables(uses).Compile(fallback);
        }
        catch (CelException e)
        {
            diagnostics.Add(DislLoader.Error(DislJson.Pointer(pointer, "fallback"), e.Message));
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
