using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Fbl.Expressions;

/// <summary>FBL's presets of the CEL engine: the variables each <see cref="CelContext"/> offers (FBL §2.4).</summary>
public static class FblCel
{
    private static readonly Dictionary<CelContext, string[]> _variables = new()
    {
        [CelContext.Tree] = ["entry", "parent", "path", "line", "registration"],
        [CelContext.Lines] = ["entry", "parent", "groups", "line", "registration"],
        [CelContext.Insert] = ["attributes"],
    };

    /// <summary>Compiles <paramref name="expression"/> for <paramref name="context"/>; a <see cref="CelException"/> names what it cannot use.</summary>
    public static CelProgram Compile(string expression, CelContext context) => CelCompiler.Compile(expression, _variables[context]);
}
