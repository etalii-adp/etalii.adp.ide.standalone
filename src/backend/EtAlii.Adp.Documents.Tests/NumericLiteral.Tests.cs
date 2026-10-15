using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>Numbers as Turtle and SPARQL write them (code-structure T131, FR-044).</summary>
public class NumericLiteralTests
{
    [Theory]
    [InlineData("42 .", 2, NumericLiteral.Integer)]
    [InlineData("-42 .", 3, NumericLiteral.Integer)]
    [InlineData("5. ", 1, NumericLiteral.Integer)]
    [InlineData("5.25 ", 4, NumericLiteral.Decimal)]
    [InlineData(".5 ", 2, NumericLiteral.Decimal)]
    [InlineData("+1.5e-3 ", 7, NumericLiteral.Double)]
    [InlineData("1E9]", 3, NumericLiteral.Double)]
    [InlineData("5e ", 2, NumericLiteral.Double)]
    public void ANumber_EndsWhereItsLexicalFormDoes_AndHasThatFormsType(string text, int end, string datatype)
    {
        // Act.
        var scanned = NumericLiteral.Scan(text, 0);

        // Assert.
        Assert.Equal(end, scanned);
        Assert.Equal(datatype, NumericLiteral.Datatype(text[..scanned]));
    }
}
