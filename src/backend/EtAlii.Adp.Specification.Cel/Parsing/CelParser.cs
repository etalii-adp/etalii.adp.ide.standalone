namespace EtAlii.Adp.Specification.Cel;

using System.Globalization;
using System.Text;

internal sealed class CelParser(string source)
{
    private static readonly HashSet<string> _macros = ["all", "exists", "exists_one", "filter", "map"];
    private static readonly HashSet<string> _functions = ["size", "matches", "startsWith", "endsWith", "contains", "replace", "lowerAscii", "upperAscii", "int", "double", "string"];

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
            if (Peek("."))
            {
                _position++;
                var name = ExpectIdent();
                if (Peek("("))
                {
                    _position++;
                    if (_macros.Contains(name))
                    {
                        var variable = ExpectIdent();
                        Expect(",");
                        var body = ParseExpression();
                        Expect(")");
                        node = new CelNode.Macro(name, node, variable, body);
                        continue;
                    }
                    if (!_functions.Contains(name)) throw new CelException($"The function '{name}' is not supported by this CEL evaluator.");
                    node = new CelNode.Call(name, node, ParseArguments());
                    continue;
                }
                node = new CelNode.Member(node, name);
                continue;
            }
            if (Peek("["))
            {
                _position++;
                var key = ParseExpression();
                Expect("]");
                node = new CelNode.Index(node, key);
                continue;
            }
            return node;
        }
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
            case "int": return new CelNode.Literal(long.Parse(text, CultureInfo.InvariantCulture));
            case "double": return new CelNode.Literal(double.Parse(text, CultureInfo.InvariantCulture));
            case "string": return new CelNode.Literal(text);
            case "ident":
                switch (text)
                {
                    case "true": return new CelNode.Literal(true);
                    case "false": return new CelNode.Literal(false);
                    case "null": return new CelNode.Literal(null);
                    case "has":
                        Expect("(");
                        var target = ParsePostfix(ParsePrimary());
                        Expect(")");
                        return target is CelNode.Member m ? new CelNode.Has(m.Target, m.Name) : throw new CelException("has() needs a field selection such as has(entry.end).");
                }
                if (Peek("("))
                {
                    _position++;
                    if (!_functions.Contains(text)) throw new CelException($"The function '{text}' is not supported by this CEL evaluator.");
                    return new CelNode.Call(text, null, ParseArguments());
                }
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
                var start = i;
                while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                var isDouble = false;
                if (i + 1 < source.Length && source[i] == '.' && char.IsAsciiDigit(source[i + 1]))
                {
                    isDouble = true;
                    i++;
                    while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                }
                if (i < source.Length && source[i] is 'u' or 'U') throw new CelException("Unsigned integers are not supported by this CEL evaluator.");
                tokens.Add((isDouble ? "double" : "int", source[start..i]));
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
                var builder = new StringBuilder();
                i++;
                while (i < source.Length && source[i] != c)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        i++;
                        builder.Append(source[i] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            _ => source[i],
                        });
                    }
                    else
                    {
                        builder.Append(source[i]);
                    }
                    i++;
                }
                if (i >= source.Length) throw new CelException($"A string is not closed in '{source}'.");
                i++;
                tokens.Add(("string", builder.ToString()));
                continue;
            }
            var two = i + 1 < source.Length ? source.Substring(i, 2) : "";
            if (two is "&&" or "||" or "==" or "!=" or "<=" or ">=")
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
}
