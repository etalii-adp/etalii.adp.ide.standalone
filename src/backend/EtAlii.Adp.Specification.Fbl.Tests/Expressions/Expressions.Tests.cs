using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Expressions;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Expressions;

/// <summary>FBL's contexts of the CEL engine (FBL §2.4): what each refuses. The engine's own tests are in EtAlii.Adp.Specification.Cel.Tests.</summary>
public class ExpressionsTests
{
    [Theory]
    [InlineData("groups.name == 'x'", CelContext.Tree, "'groups' is not a variable here; available: entry, parent, path, line, registration.")]
    [InlineData("path.x == 1", CelContext.Lines, "'path' is not a variable here; available: entry, parent, groups, line, registration.")]
    [InlineData("entry.x == 1", CelContext.Insert, "'entry' is not a variable here; available: attributes.")]
    [InlineData("entry.x.y.z()", CelContext.Tree, "The function 'z' is not supported by this CEL evaluator.")]
    [InlineData("timestamp('2026-01-01')", CelContext.Tree, "The function 'timestamp' is not supported by this CEL evaluator.")]
    public void AnExpressionOutsideTheSubsetFailsToCompile(string expression, CelContext context, string message)
    {
        // Act.
        var refused = Record.Exception(() => FblCel.Compile(expression, context));

        // Assert.
        Assert.IsType<CelException>(refused);
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }
}
