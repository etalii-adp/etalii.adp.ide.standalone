using System.Globalization;
using System.Text;

namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// Parses CEL against an environment: a call resolves to one of its functions and a comprehension to
/// one of its macros while parsing, so anything the environment does not offer is refused here,
/// naming it.
/// </summary>
internal sealed class CelParser(string source, CelEnvironment environment)
{
    private readonly List<(string Kind, string Text)> _tokens = Tokenize(source);
    private int _position;

    public CelNode ParseExpression()
    {
        var condition = ParseOr();
        if (Peek("?"))
        {
            _position++;
            var then = ParseOr();
            Expect(":");
            var otherwise = ParseExpression();
            return new CelNode.Conditional(condition, then, otherwise);
        }
        return condition;
    }

    public void ExpectEnd()
    {
        if (_position < _tokens.Count) throw new CelException($"Unexpected '{_tokens[_position].Text}' in '{source}'.");
    }

    private CelNode ParseOr()
    {
        var left = ParseAnd();
        while (Peek("||"))
        {
            _position++;
            left = new CelNode.Binary("||", left, ParseAnd());
        }
        return left;
    }

    private CelNode ParseAnd()
    {
        var left = ParseRelation();
        while (Peek("&&"))
        {
            _position++;
            left = new CelNode.Binary("&&", left, ParseRelation());
        }
        return left;
    }

    private CelNode ParseRelation()
    {
        var left = ParseAddition();
        while (_position < _tokens.Count && _tokens[_position].Kind == "op" && _tokens[_position].Text is "==" or "!=" or "<" or "<=" or ">" or ">=" or "in")
        {
            var op = _tokens[_position++].Text;
            left = new CelNode.Binary(op, left, ParseAddition());
        }
        return left;
    }

    private CelNode ParseAddition()
    {
        var left = ParseMultiplication();
        while (Peek("+") || Peek("-"))
        {
            var op = _tokens[_position++].Text;
            left = new CelNode.Binary(op, left, ParseMultiplication());
        }
        return left;
    }

    private CelNode ParseMultiplication()
    {
        var left = ParseUnary();
        while (Peek("*") || Peek("/") || Peek("%"))
        {
            var op = _tokens[_position++].Text;
            left = new CelNode.Binary(op, left, ParseUnary());
        }
        return left;
    }

    private CelNode ParseUnary()
    {
        if (Peek("!") || Peek("-"))
        {
            var op = _tokens[_position++].Text;
            return new CelNode.Unary(op, ParseUnary());
        }
        return ParsePostfix(ParsePrimary());
    }

    private CelNode ParsePostfix(CelNode node)
    {
        while (true)
        {
            if (Peek(".?"))
            {
                _position++;
                node = new CelNode.Member(node, ExpectIdent(), true);
                continue;
            }
            if (Peek("."))
            {
                _position++;
                var name = ExpectIdent();
                if (Peek("("))
                {
                    _position++;
                    if (environment.TryGetMacro(name, out var macro))
                    {
                        var variable = ExpectIdent();
                        Expect(",");
                        var body = ParseExpression();
                        Expect(")");
                        node = new CelNode.Comprehension(macro, node, variable, body);
                        continue;
                    }
                    node = Resolve(name, CelCallStyle.Receiver, node, ParseArguments());
                    continue;
                }
                node = new CelNode.Member(node, name, false);
                continue;
            }
            if (Peek("[") || Peek("[?"))
            {
                var optional = _tokens[_position++].Text == "[?";
                var key = ParseExpression();
                Expect("]");
                node = new CelNode.Index(node, key, optional);
                continue;
            }
            return node;
        }
    }

    private CelNode.Call Resolve(string name, CelCallStyle style, CelNode? receiver, List<CelNode> arguments)
    {
        if (!environment.TryGetFunction(name, style, out var function))
        {
            throw new CelException($"The function '{name}' is not supported by this CEL evaluator.");
        }
        if (arguments.Count < function.MinArguments || arguments.Count > function.MaxArguments)
        {
            throw new CelException($"The function '{name}' takes {function.Arity} argument{(function is { MinArguments: 1, MaxArguments: 1 or CelFunction.Variadic } ? "" : "s")}, not {arguments.Count}, in '{source}'.");
        }
        return new CelNode.Call(function, receiver, arguments);
    }

    private List<CelNode> ParseArguments()
    {
        var args = new List<CelNode>();
        if (!Peek(")"))
        {
            args.Add(ParseExpression());
            while (Peek(","))
            {
                _position++;
                args.Add(ParseExpression());
            }
        }
        Expect(")");
        return args;
    }

    private CelNode ParsePrimary()
    {
        if (_position >= _tokens.Count) throw new CelException($"'{source}' ends too early.");
        (string kind, string text) = _tokens[_position++];
        switch (kind)
        {
            case "int":
                return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    ? new CelNode.Literal(number)
                    : throw new CelException($"The int {text} is out of range, in '{source}'.");
            case "double": return new CelNode.Literal(double.Parse(text, CultureInfo.InvariantCulture));
            case "string": return new CelNode.Literal(text);
            case "ident":
                switch (text)
                {
                    case "true": return new CelNode.Literal(true);
                    case "false": return new CelNode.Literal(false);
                    case "null": return new CelNode.Literal(null);
                    case "has" when Peek("("):
                        Expect("(");
                        var target = ParsePostfix(ParsePrimary());
                        Expect(")");
                        return target is CelNode.Member { Optional: false } m ? new CelNode.Has(m.Target, m.Name) : throw new CelException("has() needs a field selection such as has(entry.end).");
                }
                if (Peek("("))
                {
                    _position++;
                    return Resolve(text, CelCallStyle.Global, null, ParseArguments());
                }
                if (QualifiedCall(text) is { } qualified) return qualified;
                return new CelNode.Ident(text);
            case "op":
                if (text == "(")
                {
                    var inner = ParseExpression();
                    Expect(")");
                    return inner;
                }
                if (text == "[")
                {
                    var items = new List<CelNode>();
                    if (!Peek("]"))
                    {
                        items.Add(ParseExpression());
                        while (Peek(","))
                        {
                            _position++;
                            if (Peek("]")) break;
                            items.Add(ParseExpression());
                        }
                    }
                    Expect("]");
                    return new CelNode.ListLiteral(items);
                }
                if (text == "{")
                {
                    var entries = new List<(CelNode, CelNode)>();
                    if (!Peek("}"))
                    {
                        do
                        {
                            if (Peek(",")) _position++;
                            if (Peek("}")) break;
                            var key = ParseExpression();
                            Expect(":");
                            entries.Add((key, ParseExpression()));
                        }
                        while (Peek(","));
                    }
                    Expect("}");
                    return new CelNode.MapLiteral(entries);
                }
                break;
        }
        throw new CelException($"Unexpected '{text}' in '{source}'.");
    }

    /// <summary>
    /// <c>cel.bind(…)</c>, or a global function with a dotted name such as <c>math.round(…)</c>, when
    /// <paramref name="head"/> is not a variable; null when the tokens are an ordinary selection.
    /// </summary>
    private CelNode? QualifiedCall(string head)
    {
        if (!Peek(".") || _position + 2 >= _tokens.Count || _tokens[_position + 1].Kind != "ident" || !(_tokens[_position + 2] is ("op", "(")))
        {
            return null;
        }
        if (environment.IsVariable(head)) return null;
        var name = $"{head}.{_tokens[_position + 1].Text}";
        if (name == "cel.bind")
        {
            _position += 3;
            var variable = ExpectIdent();
            Expect(",");
            var init = ParseExpression();
            Expect(",");
            var body = ParseExpression();
            Expect(")");
            return new CelNode.Bind(variable, init, body);
        }
        if (!environment.TryGetFunction(name, CelCallStyle.Global, out _)) return null;
        _position += 3;
        return Resolve(name, CelCallStyle.Global, null, ParseArguments());
    }

    private bool Peek(string text) => _position < _tokens.Count && _tokens[_position].Text == text && _tokens[_position].Kind is "op";

    private void Expect(string text)
    {
        if (!Peek(text)) throw new CelException($"Expected '{text}' in '{source}'.");
        _position++;
    }

    private string ExpectIdent()
    {
        if (_position >= _tokens.Count || _tokens[_position].Kind != "ident") throw new CelException($"Expected a name in '{source}'.");
        return _tokens[_position++].Text;
    }

    private static List<(string Kind, string Text)> Tokenize(string source)
    {
        var tokens = new List<(string, string)>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                tokens.Add(ReadNumber(source, ref i));
                continue;
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(("ident", source[start..i]));
                continue;
            }
            if (c is '\'' or '"')
            {
                tokens.Add(("string", ReadString(source, ref i)));
                continue;
            }
            var two = i + 1 < source.Length ? source.Substring(i, 2) : "";
            // '.?' (optional selection) and '[?' (optional index) are taken before '.', '[' and '?'.
            if (two is "&&" or "||" or "==" or "!=" or "<=" or ">=" or ".?" or "[?")
            {
                tokens.Add(("op", two));
                i += 2;
                continue;
            }
            if ("!-+*/%<>?:.,()[]{}".Contains(c))
            {
                tokens.Add(("op", c.ToString()));
                i++;
                continue;
            }
            throw new CelException($"'{c}' is not supported by this CEL evaluator, in '{source}'.");
        }
        // 'in' is an operator spelled as a name.
        return tokens.Select(t => t is ("ident", "in") ? ("op", "in") : t).ToList();
    }

    /// <summary>An int or double literal starting at <paramref name="i"/>, which is left just past it.</summary>
    private static (string Kind, string Text) ReadNumber(string source, ref int i)
    {
        var start = i;
        while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
        var isDouble = false;
        if (i + 1 < source.Length && source[i] == '.' && char.IsAsciiDigit(source[i + 1]))
        {
            isDouble = true;
            i++;
            while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
        }
        if (i < source.Length && source[i] is 'e' or 'E')
        {
            var exponent = i + 1;
            if (exponent < source.Length && source[exponent] is '+' or '-') exponent++;
            if (exponent < source.Length && char.IsAsciiDigit(source[exponent]))
            {
                isDouble = true;
                i = exponent;
                while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
            }
        }
        if (i < source.Length && source[i] is 'u' or 'U') throw new CelException("Unsigned integers are not supported by this CEL evaluator.");
        return (isDouble ? "double" : "int", source[start..i]);
    }

    /// <summary>The unescaped text of the string literal whose opening quote is at <paramref name="i"/>, which is left just past its closing quote.</summary>
    private static string ReadString(string source, ref int i)
    {
        var quote = source[i];
        var builder = new StringBuilder();
        i++;
        while (i < source.Length && source[i] != quote)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                i++;
                builder.Append(Escaped(source, ref i) is { } code ? char.ConvertFromUtf32(code) : Unescaped(source[i]).ToString());
            }
            else
            {
                builder.Append(source[i]);
            }
            i++;
        }
        if (i >= source.Length) throw new CelException($"A string is not closed in '{source}'.");
        i++;
        return builder.ToString();
    }

    /// <summary>The character a one-letter escape stands for (cel-spec, langdef); any other character stands for itself, as <c>\\</c> and the quotes do.</summary>
    private static char Unescaped(char letter) => letter switch
    {
        'a' => '\a',
        'b' => '\b',
        'f' => '\f',
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        'v' => '\v',
        _ => letter,
    };

    /// <summary>
    /// The code point of a numeric escape whose letter or first digit is at <paramref name="i"/> (cel-spec, langdef): <c>\x</c>
    /// or <c>\X</c> and two hex digits, <c>\u</c> and four, <c>\U</c> and eight, or three octal digits; <paramref name="i"/> is
    /// left on its last character. Null for any other escape.
    /// </summary>
    private static int? Escaped(string source, ref int i)
    {
        (int digits, int radix, int from) = source[i] switch
        {
            'x' or 'X' => (2, 16, i + 1),
            'u' => (4, 16, i + 1),
            'U' => (8, 16, i + 1),
            >= '0' and <= '3' => (3, 8, i),
            _ => (0, 0, i),
        };
        if (digits == 0 || from + digits > source.Length) return null;
        var code = 0;
        for (var at = from; at < from + digits; at++)
        {
            var digit = radix == 16 && char.IsAsciiHexDigit(source[at]) ? Convert.ToInt32(source[at].ToString(), 16) : radix == 8 && source[at] is >= '0' and <= '7' ? source[at] - '0' : -1;
            if (digit < 0) throw new CelException($"The escape at {i - 1} of '{source}' is not complete.");
            code = code * radix + digit;
        }
        if (code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF) throw new CelException($"The escape at {i - 1} of '{source}' names no code point.");
        i = from + digits - 1;
        return code;
    }
}
