namespace EtAlii.Adp.Specification.Fbl.Expressions;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Where a CEL expression appears, which decides the variables it may use (FBL §2.4).</summary>
public enum CelContext
{
    /// <summary>A rule of a yaml, json or xml binding: entry, parent, path, line, registration.</summary>
    Tree,

    /// <summary>A rule of a lines or blocks binding: entry, parent, groups, line, registration.</summary>
    Lines,

    /// <summary><c>insert.when</c>: attributes.</summary>
    Insert,
}

public sealed class CelException(string message) : Exception(message);

/// <summary>
/// The subset of CEL this library evaluates: literals, member access and indexing, the logical,
/// relational and arithmetic operators, <c>in</c>, the conditional, <c>has()</c>, the macros
/// <c>all</c>, <c>exists</c>, <c>exists_one</c>, <c>filter</c> and <c>map</c>, and the functions
/// <c>size</c>, <c>matches</c>, <c>startsWith</c>, <c>endsWith</c>, <c>contains</c>, <c>replace</c>,
/// <c>lowerAscii</c>, <c>upperAscii</c>, <c>int</c>, <c>double</c> and <c>string</c>. Any other
/// construct is refused when the expression is compiled, naming the construct, so a binding that
/// needs more fails at load rather than at read.
/// </summary>
public static class CelCompiler
{
    private static readonly Dictionary<CelContext, string[]> _variables = new()
    {
        [CelContext.Tree] = ["entry", "parent", "path", "line", "registration"],
        [CelContext.Lines] = ["entry", "parent", "groups", "line", "registration"],
        [CelContext.Insert] = ["attributes"],
    };

    public static CelProgram Compile(string expression, CelContext context)
    {
        var parser = new CelParser(expression);
        var node = parser.ParseExpression();
        parser.ExpectEnd();
        Check(node, _variables[context], []);
        return new CelProgram(expression, node);
    }

    private static void Check(CelNode node, string[] variables, HashSet<string> bound)
    {
        switch (node)
        {
            case CelNode.Ident ident:
                if (!variables.Contains(ident.Name) && !bound.Contains(ident.Name))
                {
                    throw new CelException($"'{ident.Name}' is not a variable here; available: {string.Join(", ", variables)}.");
                }
                break;
            case CelNode.Macro macro:
                Check(macro.Target, variables, bound);
                var inner = new HashSet<string>(bound) { macro.Variable };
                Check(macro.Body, variables, inner);
                break;
            default:
                foreach (var child in node.Children) Check(child, variables, bound);
                break;
        }
    }
}

/// <summary>A compiled CEL expression.</summary>
public sealed class CelProgram
{
    private const int StepBudget = 100_000;

    internal CelProgram(string source, CelNode root)
    {
        Source = source;
        Root = root;
    }

    public string Source { get; }

    internal CelNode Root { get; }

    /// <summary>Evaluates with <paramref name="variables"/>; a value of <see cref="CelError"/> when evaluation fails.</summary>
    public object? Evaluate(IReadOnlyDictionary<string, object?> variables)
    {
        var steps = 0;
        try
        {
            return Root.Evaluate(new CelScope(variables, null), ref steps);
        }
        catch (CelException e)
        {
            return new CelError(e.Message);
        }
    }

    /// <summary>True only when the expression evaluates to true.</summary>
    public bool IsTrue(IReadOnlyDictionary<string, object?> variables) => Evaluate(variables) is true;

    internal static void Step(ref int steps)
    {
        if (++steps > StepBudget) throw new CelException("The expression exceeded its evaluation budget.");
    }
}

/// <summary>The result of an evaluation that failed: a missing key, a type mismatch, a division by zero.</summary>
public sealed record CelError(string Message);

/// <summary>The map type CEL values use: insertion-ordered, string keys.</summary>
public sealed class CelMap : Dictionary<string, object?>
{
    public CelMap() : base(StringComparer.Ordinal)
    {
    }
}

internal sealed class CelScope(IReadOnlyDictionary<string, object?> variables, CelScope? parent)
{
    private readonly Dictionary<string, object?> _locals = new(StringComparer.Ordinal);

    public CelScope With(string name, object? value)
    {
        var scope = new CelScope(variables, this);
        scope._locals[name] = value;
        return scope;
    }

    public object? Lookup(string name)
    {
        for (var s = this; s is not null; s = s.Parent)
        {
            if (s._locals.TryGetValue(name, out var local)) return local;
        }
        if (variables.TryGetValue(name, out var value)) return value;
        throw new CelException($"'{name}' has no value.");
    }

    private CelScope? Parent => parent;
}

internal abstract record CelNode
{
    public virtual IEnumerable<CelNode> Children => [];

    public abstract object? Evaluate(CelScope scope, ref int steps);

    internal sealed record Literal(object? Value) : CelNode
    {
        public override object? Evaluate(CelScope scope, ref int steps) => Value;
    }

    internal sealed record Ident(string Name) : CelNode
    {
        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            return scope.Lookup(Name);
        }
    }

    internal sealed record ListLiteral(IReadOnlyList<CelNode> Items) : CelNode
    {
        public override IEnumerable<CelNode> Children => Items;

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            var list = new List<object?>(Items.Count);
            foreach (var item in Items) list.Add(item.Evaluate(scope, ref steps));
            return list;
        }
    }

    internal sealed record MapLiteral(IReadOnlyList<(CelNode Key, CelNode Value)> Entries) : CelNode
    {
        public override IEnumerable<CelNode> Children => Entries.SelectMany(e => new[] { e.Key, e.Value });

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            var map = new CelMap();
            foreach (var (k, v) in Entries) map[CelValues.AsString(k.Evaluate(scope, ref steps))] = v.Evaluate(scope, ref steps);
            return map;
        }
    }

    internal sealed record Member(CelNode Target, string Name) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target];

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            var target = Target.Evaluate(scope, ref steps);
            if (target is CelError) return target;
            if (target is Dictionary<string, object?> map)
            {
                return map.TryGetValue(Name, out var value) ? value : throw new CelException($"No such key: '{Name}'.");
            }
            throw new CelException($"'{Name}' is selected from a value that is not a map.");
        }
    }

    internal sealed record Index(CelNode Target, CelNode Key) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target, Key];

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            var target = Target.Evaluate(scope, ref steps);
            var key = Key.Evaluate(scope, ref steps);
            switch (target)
            {
                case List<object?> list:
                    var i = CelValues.AsInt(key);
                    if (i < 0 || i >= list.Count) throw new CelException($"Index {i} is out of range.");
                    return list[(int)i];
                case Dictionary<string, object?> map:
                    var k = CelValues.AsString(key);
                    return map.TryGetValue(k, out var value) ? value : throw new CelException($"No such key: '{k}'.");
                default:
                    throw new CelException("Only a list or a map can be indexed.");
            }
        }
    }

    internal sealed record Has(CelNode Target, string Name) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target];

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            var target = Target.Evaluate(scope, ref steps);
            return target is Dictionary<string, object?> map ? map.ContainsKey(Name) : throw new CelException("has() needs a map.");
        }
    }

    internal sealed record Unary(string Operator, CelNode Operand) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Operand];

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            var value = Operand.Evaluate(scope, ref steps);
            return Operator switch
            {
                "!" => value is bool b ? !b : throw new CelException("'!' needs a bool."),
                "-" => value switch
                {
                    long l => -l,
                    double d => -d,
                    _ => throw new CelException("'-' needs a number."),
                },
                _ => throw new CelException($"Unknown operator '{Operator}'."),
            };
        }
    }

    internal sealed record Binary(string Operator, CelNode Left, CelNode Right) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Left, Right];

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            if (Operator is "&&" or "||")
            {
                object? left;
                try { left = Left.Evaluate(scope, ref steps); }
                catch (CelException e) { left = new CelError(e.Message); }
                if (Operator == "&&" && left is false) return false;
                if (Operator == "||" && left is true) return true;
                object? right;
                try { right = Right.Evaluate(scope, ref steps); }
                catch (CelException e) { right = new CelError(e.Message); }
                if (Operator == "&&" && right is false) return false;
                if (Operator == "||" && right is true) return true;
                if (left is bool && right is bool) return Operator == "&&";
                throw new CelException(left is CelError le ? le.Message : right is CelError re ? re.Message : $"'{Operator}' needs bools.");
            }
            var l = Left.Evaluate(scope, ref steps);
            var r = Right.Evaluate(scope, ref steps);
            switch (Operator)
            {
                case "==": return CelValues.Equal(l, r);
                case "!=": return !CelValues.Equal(l, r);
                case "<": return CelValues.Compare(l, r) < 0;
                case "<=": return CelValues.Compare(l, r) <= 0;
                case ">": return CelValues.Compare(l, r) > 0;
                case ">=": return CelValues.Compare(l, r) >= 0;
                case "in":
                    return r switch
                    {
                        List<object?> list => list.Any(item => CelValues.Equal(item, l)),
                        Dictionary<string, object?> map => l is string key && map.ContainsKey(key),
                        _ => throw new CelException("'in' needs a list or a map on its right."),
                    };
                case "+":
                    return (l, r) switch
                    {
                        (long a, long b) => a + b,
                        (string a, string b) => a + b,
                        (List<object?> a, List<object?> b) => a.Concat(b).ToList(),
                        _ => CelValues.AsDouble(l) + CelValues.AsDouble(r),
                    };
                case "-": return (l, r) is (long a1, long b1) ? a1 - b1 : CelValues.AsDouble(l) - CelValues.AsDouble(r);
                case "*": return (l, r) is (long a2, long b2) ? a2 * b2 : CelValues.AsDouble(l) * CelValues.AsDouble(r);
                case "/":
                    if ((l, r) is (long a3, long b3)) return b3 == 0 ? throw new CelException("Division by zero.") : a3 / b3;
                    return CelValues.AsDouble(l) / CelValues.AsDouble(r);
                case "%":
                    if ((l, r) is (long a4, long b4)) return b4 == 0 ? throw new CelException("Modulus by zero.") : a4 % b4;
                    throw new CelException("'%' needs ints.");
                default: throw new CelException($"Unknown operator '{Operator}'.");
            }
        }
    }

    internal sealed record Conditional(CelNode Condition, CelNode Then, CelNode Else) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Condition, Then, Else];

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            var condition = Condition.Evaluate(scope, ref steps);
            return condition switch
            {
                true => Then.Evaluate(scope, ref steps),
                false => Else.Evaluate(scope, ref steps),
                _ => throw new CelException("A conditional needs a bool."),
            };
        }
    }

    internal sealed record Macro(string Name, CelNode Target, string Variable, CelNode Body) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target, Body];

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            var target = Target.Evaluate(scope, ref steps);
            IEnumerable<object?> items = target switch
            {
                List<object?> list => list,
                Dictionary<string, object?> map => map.Keys,
                _ => throw new CelException($"'{Name}' needs a list or a map."),
            };
            var results = new List<object?>();
            foreach (var item in items)
            {
                var value = Body.Evaluate(scope.With(Variable, item), ref steps);
                switch (Name)
                {
                    case "all":
                        if (value is false) return false;
                        break;
                    case "exists":
                        if (value is true) return true;
                        break;
                    case "exists_one":
                    case "filter":
                        if (value is true) results.Add(item);
                        break;
                    case "map":
                        results.Add(value);
                        break;
                }
            }
            return Name switch
            {
                "all" => true,
                "exists" => false,
                "exists_one" => results.Count == 1,
                _ => results,
            };
        }
    }

    internal sealed record Call(string Function, CelNode? Receiver, IReadOnlyList<CelNode> Arguments) : CelNode
    {
        public override IEnumerable<CelNode> Children => Receiver is null ? Arguments : Arguments.Prepend(Receiver);

        public override object? Evaluate(CelScope scope, ref int steps)
        {
            CelProgram.Step(ref steps);
            var receiver = Receiver?.Evaluate(scope, ref steps);
            var args = new List<object?>();
            foreach (var a in Arguments) args.Add(a.Evaluate(scope, ref steps));
            if (Receiver is null && args.Count > 0)
            {
                receiver = args[0];
                args.RemoveAt(0);
            }
            return Function switch
            {
                "size" => receiver switch
                {
                    string s => (long)new StringInfo(s).LengthInTextElements,
                    List<object?> l => (long)l.Count,
                    Dictionary<string, object?> m => (long)m.Count,
                    _ => throw new CelException("size() needs a string, a list or a map."),
                },
                "matches" => Regex.IsMatch(CelValues.AsString(receiver), RegexSubset.ToDotNet(CelValues.AsString(args[0]), false), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)),
                "startsWith" => CelValues.AsString(receiver).StartsWith(CelValues.AsString(args[0]), StringComparison.Ordinal),
                "endsWith" => CelValues.AsString(receiver).EndsWith(CelValues.AsString(args[0]), StringComparison.Ordinal),
                "contains" => CelValues.AsString(receiver).Contains(CelValues.AsString(args[0]), StringComparison.Ordinal),
                "replace" => CelValues.AsString(receiver).Replace(CelValues.AsString(args[0]), CelValues.AsString(args[1]), StringComparison.Ordinal),
                "lowerAscii" => CelValues.AsString(receiver).ToLowerInvariant(),
                "upperAscii" => CelValues.AsString(receiver).ToUpperInvariant(),
                "int" => receiver switch
                {
                    long l => l,
                    double d => (long)d,
                    string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
                    _ => throw new CelException("int() cannot convert this value."),
                },
                "double" => CelValues.AsDouble(receiver is string ds ? double.Parse(ds, CultureInfo.InvariantCulture) : receiver),
                "string" => CelValues.Format(receiver),
                _ => throw new CelException($"The function '{Function}' is not supported."),
            };
        }
    }
}

internal static class CelValues
{
    public static string AsString(object? value) => value as string ?? throw new CelException("A string was expected.");

    public static long AsInt(object? value) => value is long l ? l : throw new CelException("An int was expected.");

    public static double AsDouble(object? value) => value switch
    {
        long l => l,
        double d => d,
        _ => throw new CelException("A number was expected."),
    };

    public static bool Equal(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (long x, double y) => x == y,
        (double x, long y) => x == y,
        (List<object?> x, List<object?> y) => x.Count == y.Count && x.Zip(y).All(p => Equal(p.First, p.Second)),
        _ => Equals(a, b),
    };

    public static int Compare(object? a, object? b) => (a, b) switch
    {
        (string x, string y) => string.CompareOrdinal(x, y),
        (bool x, bool y) => x.CompareTo(y),
        _ => AsDouble(a).CompareTo(AsDouble(b)),
    };

    public static string Format(object? value) => value switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

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
        var (kind, text) = _tokens[_position++];
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
