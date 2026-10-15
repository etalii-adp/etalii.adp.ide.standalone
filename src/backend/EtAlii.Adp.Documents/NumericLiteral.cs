namespace EtAlii.Adp.Documents;

/// <summary>
/// A number as Turtle and SPARQL write one: where it ends in a text, and which XML Schema type
/// its lexical form has.
/// </summary>
public static class NumericLiteral
{
    /// <summary>The datatype of a number with neither a fraction nor an exponent.</summary>
    public const string Integer = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The datatype of a number with a fraction and no exponent.</summary>
    public const string Decimal = "http://www.w3.org/2001/XMLSchema#decimal";

    /// <summary>The datatype of a number with an exponent.</summary>
    public const string Double = "http://www.w3.org/2001/XMLSchema#double";

    /// <summary>
    /// The offset past a number that starts at <paramref name="start"/>: an optional sign, digits,
    /// a fraction when a digit follows its dot, and an exponent.
    /// </summary>
    /// <remarks>
    /// A trailing dot is not part of the number: <c>5.</c> is the integer five and then whatever
    /// the dot ends. The digits of an exponent are not required, so <c>5e</c> is read as one
    /// number, as the Turtle tokenizer always read it.
    /// </remarks>
    public static int Scan(string text, int start)
    {
        var position = start;
        if (text[position] is '+' or '-')
        {
            position++;
        }

        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            position++;
        }

        if (position + 1 < text.Length && text[position] == '.' && char.IsAsciiDigit(text[position + 1]))
        {
            position++;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        if (position < text.Length && text[position] is 'e' or 'E')
        {
            position++;
            if (position < text.Length && text[position] is '+' or '-')
            {
                position++;
            }

            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        return position;
    }

    /// <summary>The XML Schema type of the number written <paramref name="lexical"/>.</summary>
    public static string Datatype(string lexical) =>
        lexical.Contains('e') || lexical.Contains('E')
            ? Double
            : lexical.Contains('.')
                ? Decimal
                : Integer;
}
