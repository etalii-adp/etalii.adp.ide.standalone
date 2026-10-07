using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>The strings library, after cel-spec's string_ext tests; indexes count code points.</summary>
public class CelStringsTests
{
    [Theory]
    [InlineData("'tacocat'.charAt(3)", "o")]
    [InlineData("'tacocat'.charAt(7)", "")]
    [InlineData("'©αT'.charAt(0) == '©' && 'Ω😀x'.charAt(2) == 'x'", "true")]
    [InlineData("'tacocat'.charAt(30)", "error: charAt(30) is out of range for a string of 7.")]
    [InlineData("'tacocat'.indexOf('')", "0")]
    [InlineData("'tacocat'.indexOf('ac')", "1")]
    [InlineData("'tacocat'.indexOf('none') == -1", "true")]
    [InlineData("'tacocat'.indexOf('', 3)", "3")]
    [InlineData("'tacocat'.indexOf('a', 3)", "5")]
    [InlineData("'ta©o©αT'.indexOf('©', 3)", "4")]
    [InlineData("'😀a😀b'.indexOf('b')", "3")]
    [InlineData("'tacocat'.indexOf('a', 30)", "error: indexOf() start 30 is out of range for a string of 7.")]
    [InlineData("'tacocat'.lastIndexOf('')", "7")]
    [InlineData("'tacocat'.lastIndexOf('at')", "5")]
    [InlineData("'tacocat'.lastIndexOf('none') == -1", "true")]
    [InlineData("'tacocat'.lastIndexOf('a', 3)", "1")]
    [InlineData("'TacoCat'.lowerAscii()", "tacocat")]
    [InlineData("'TacoCÆt Xii'.lowerAscii()", "tacocÆt xii")]
    [InlineData("'tacoCat'.upperAscii()", "TACOCAT")]
    [InlineData("'tacoCαt'.upperAscii()", "TACOCαT")]
    [InlineData("lower('TacoCÆt')", "tacocæt")]
    [InlineData("upper('tacoCαt')", "TACOCΑT")]
    [InlineData("'12 days 12 hours'.replace('{0}', '2')", "12 days 12 hours")]
    [InlineData("'{0} days {0} hours'.replace('{0}', '2')", "2 days 2 hours")]
    [InlineData("'{0} days {0} hours'.replace('{0}', '2', 1).replace('{0}', '23')", "2 days 23 hours")]
    [InlineData("'1 ©αT taco'.replace('αT', 'o©α')", "1 ©o©α taco")]
    [InlineData("'hello hello'.replace('he', 'we', 0)", "hello hello")]
    [InlineData("'hello hello'.replace('he', 'we', -1)", "wello wello")]
    [InlineData("'hello hello hello'.split(' ')", "[hello,hello,hello]")]
    [InlineData("'hello hello hello'.split(' ', 0)", "[]")]
    [InlineData("'hello hello hello'.split(' ', 1)", "[hello hello hello]")]
    [InlineData("'hello hello hello'.split(' ', 2)", "[hello,hello hello]")]
    [InlineData("'hello hello hello'.split(' ', -1)", "[hello,hello,hello]")]
    [InlineData("'hello'.split('')", "[h,e,l,l,o]")]
    [InlineData("'tacocat'.substring(4)", "cat")]
    [InlineData("'tacocat'.substring(7)", "")]
    [InlineData("'tacocat'.substring(0, 4)", "taco")]
    [InlineData("'tacocat'.substring(4, 4)", "")]
    [InlineData("'ta©o©αT'.substring(2, 6)", "©o©α")]
    [InlineData("'tacocat'.substring(-1)", "error: substring(-1, 7) is out of range for a string of 7.")]
    [InlineData("'tacocat'.substring(4, 3)", "error: substring(4, 3) is out of range for a string of 7.")]
    [InlineData("' \\ttrim\\n    '.trim()", "trim")]
    [InlineData("['x', 'y'].join()", "xy")]
    [InlineData("['x', 'y'].join('-')", "x-y")]
    [InlineData("[].join()", "")]
    [InlineData("['x', 1].join()", "error: join() needs a list of strings.")]
    [InlineData("'gums'.reverse()", "smug")]
    [InlineData("'a😀b'.reverse()", "b😀a")]
    [InlineData("'hello'.startsWith('he') && 'hello'.endsWith('lo') && 'hello'.contains('ell')", "true")]
    [InlineData("size('a😀b') == 3 && 'a😀b'.size() == 3", "true")]
    // A combining accent is a code point of its own, though it draws as part of one character.
    [InlineData("size('e\u0301') == 2 && 'e\u0301'.charAt(1) == '\u0301'", "true")]
    public void AStringFunctionEvaluates(string expression, string expected)
    {
        // Act.
        var value = Evaluate.Expression(expression);

        // Assert.
        Assert.Equal(expected, Evaluate.Text(value));
    }

    [Fact]
    public void JoinAndIndexOfServeListsToo()
    {
        // Act and assert: DISL §12.4's l.indexOf(v) and the lists extension's join share their names with the string functions.
        Assert.Equal(1L, Evaluate.Expression("['a', 'b', 'a'].indexOf('b')"));
        Assert.Equal(2L, Evaluate.Expression("['a', 'b', 'a'].lastIndexOf('a')"));
        Assert.Equal(-1L, Evaluate.Expression("[1, 2].indexOf(3)"));
    }
}
