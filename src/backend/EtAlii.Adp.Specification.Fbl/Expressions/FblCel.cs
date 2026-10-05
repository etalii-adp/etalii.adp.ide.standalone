using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Fbl.Expressions;

/// <summary>
/// FBL's presets of the CEL engine (FBL §2.4): the standard environment, which FBL shares with DISL,
/// with the variables each <see cref="CelContext"/> offers and nothing else.
/// </summary>
public static class FblCel
{
    private static readonly Dictionary<CelContext, CelEnvironment> _environments = new()
    {
        [CelContext.Tree] = CelEnvironment.Standard().DeclareVariables("entry", "parent", "path", "line", "registration"),
        [CelContext.Lines] = CelEnvironment.Standard().DeclareVariables("entry", "parent", "groups", "line", "registration"),
        [CelContext.Insert] = CelEnvironment.Standard().DeclareVariables("attributes"),
    };

    /// <summary>Compiles <paramref name="expression"/> for <paramref name="context"/>; a <see cref="CelException"/> names what it cannot use.</summary>
    public static CelProgram Compile(string expression, CelContext context) => _environments[context].Compile(expression);
}
