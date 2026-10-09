using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// A specification's own functions (DISL §3.4), compiled into its CEL environment in declaration
/// order, so each may call only those declared before it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order is the termination guarantee.</b> A function body is compiled in an environment that holds
/// the functions declared before it and, when it declares <c>recursion</c>, itself. A call into a
/// later function, or into itself without <c>recursion</c>, therefore fails to compile, and indirect
/// recursion cannot be written at all. The diagnostic names which of the two it is.
/// </para>
/// <para>
/// <b>A function sees its parameters</b>, and <c>diagram</c> and <c>env</c> only when it names them in
/// <c>uses</c>, taken from the evaluation that calls it. Reading either without declaring it fails to
/// compile, as reading any other variable does.
/// </para>
/// <para>
/// <b>Bounded recursion</b> counts the nested calls of one function within one evaluation, the
/// top-level call being depth 1; a call that would go deeper than <c>maxDepth</c> evaluates
/// <c>atMaxDepth</c> over its own parameters instead of the body.
/// </para>
/// </remarks>
internal static partial class UserFunctions
{
    /// <summary>The depth of each recursive function within one evaluation, keyed by the evaluation's budget.</summary>
    private static readonly ConditionalWeakTable<CelBudget, Dictionary<string, int>> Depths = [];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleIdentifier();

    /// <summary>
    /// Adds every function of <paramref name="declarations"/> to <paramref name="environment"/>, in
    /// order, and reports each one that does not compile; a function that does not compile is not added.
    /// </summary>
    public static void Compile(CelEnvironment environment, IReadOnlyList<DislFunctionDeclaration> declarations, List<DislDiagnostic> diagnostics)
    {
        var declared = declarations.Select(declaration => declaration.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            var pointer = DislJson.Pointer("/functions", declaration.Name);
            if (!Valid(declaration, pointer, environment, diagnostics)) continue;

            var function = new Compiled(declaration);
            var scope = environment.Clone()
                .DeclareVariables(declaration.Parameters.Select(parameter => parameter.Name))
                .DeclareVariables(declaration.Uses);
            if (declaration.Recursion is not null) scope.AddFunction(function.Function);

            try
            {
                function.Body = scope.Compile(declaration.Cel);
                if (declaration.Recursion is { } recursion) function.AtMaxDepth = scope.Compile(recursion.AtMaxDepth);
            }
            catch (CelException e)
            {
                diagnostics.Add(new DislDiagnostic(DislJson.Pointer(pointer, "cel"), DislSeverity.Error, Explain(declaration, e.Message, declared)));
                continue;
            }

            environment.AddFunction(function.Function);
        }
    }

    private static bool Valid(DislFunctionDeclaration declaration, string pointer, CelEnvironment environment, List<DislDiagnostic> diagnostics)
    {
        var valid = true;
        if (!SimpleIdentifier().IsMatch(declaration.Name))
        {
            diagnostics.Add(new DislDiagnostic(pointer, DislSeverity.Error, $"'{declaration.Name}' is not a simple identifier, so CEL cannot call it."));
            valid = false;
        }
        if (environment.TryGetFunction(declaration.Name, CelCallStyle.Global, out _))
        {
            diagnostics.Add(new DislDiagnostic(pointer, DislSeverity.Error, $"'{declaration.Name}' is already a function of the DISL library or of this specification."));
            valid = false;
        }
        foreach (var parameter in declaration.Parameters.Where(parameter => !SimpleIdentifier().IsMatch(parameter.Name)))
        {
            diagnostics.Add(new DislDiagnostic(DislJson.Pointer(pointer, "params"), DislSeverity.Error, $"The parameter '{parameter.Name}' is not a simple identifier."));
            valid = false;
        }
        foreach (var use in declaration.Uses.Where(use => use is not ("diagram" or "env")))
        {
            diagnostics.Add(new DislDiagnostic(DislJson.Pointer(pointer, "uses"), DislSeverity.Error, $"A function may use 'diagram' and 'env', not '{use}'."));
            valid = false;
        }
        if (declaration.Recursion is { MaxDepth: < 1 })
        {
            diagnostics.Add(new DislDiagnostic(DislJson.Pointer(pointer, "recursion"), DislSeverity.Error, "recursion.maxDepth must be at least 1."));
            valid = false;
        }
        return valid;
    }

    /// <summary>The compile error, with why a call to a function of this specification is refused when that is the cause.</summary>
    private static string Explain(DislFunctionDeclaration declaration, string message, HashSet<string> declared)
    {
        var refused = Regex.Match(message, "^The function '([^']+)' is not supported", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!refused.Success) return message;
        var name = refused.Groups[1].Value;
        if (name == declaration.Name) return $"'{name}' calls itself, which a function may do only when it declares recursion (DISL §3.4).";
        return declared.Contains(name)
            ? $"'{declaration.Name}' calls '{name}', which is declared after it or does not compile; a function may call only the functions declared before it (DISL §3.4)."
            : message;
    }

    /// <summary>One user function: the CEL function the environment offers, and the programs it evaluates.</summary>
    private sealed class Compiled
    {
        private readonly DislFunctionDeclaration _declaration;

        public Compiled(DislFunctionDeclaration declaration)
        {
            _declaration = declaration;
            var arity = declaration.Parameters.Count;
            Function = new CelFunction(declaration.Name, CelCallStyle.Global, arity, arity, Call);
        }

        public CelFunction Function { get; }

        public CelProgram? Body { get; set; }

        public CelProgram? AtMaxDepth { get; set; }

        private object? Call(CelCall call)
        {
            var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < _declaration.Parameters.Count; index++)
            {
                variables[_declaration.Parameters[index].Name] = call[index];
            }
            foreach (var use in _declaration.Uses)
            {
                variables[use] = call.Variables.TryGetValue(use, out var value)
                    ? value
                    : throw new CelException($"'{_declaration.Name}' uses '{use}', which the expression calling it has no value for.");
            }

            if (_declaration.Recursion is not { } recursion) return Unwrap(Body!.Evaluate(variables, call.Budget));

            var depths = Depths.GetOrCreateValue(call.Budget);
            var depth = depths.GetValueOrDefault(_declaration.Name) + 1;
            if (depth > recursion.MaxDepth) return Unwrap(AtMaxDepth!.Evaluate(variables, call.Budget));
            depths[_declaration.Name] = depth;
            try
            {
                return Unwrap(Body!.Evaluate(variables, call.Budget));
            }
            finally
            {
                depths[_declaration.Name] = depth - 1;
            }
        }

        /// <summary>A failing body fails the call, so the error reaches the expression that called it.</summary>
        private static object? Unwrap(object? result) => result is CelError error ? throw new CelException(error.Message) : result;
    }
}
