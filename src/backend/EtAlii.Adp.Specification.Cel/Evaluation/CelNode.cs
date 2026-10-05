namespace EtAlii.Adp.Specification.Cel;

using System.Globalization;
using System.Text.RegularExpressions;

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

        public override object Evaluate(CelScope scope, ref int steps)
        {
            var list = new List<object?>(Items.Count);
            foreach (var item in Items) list.Add(item.Evaluate(scope, ref steps));
            return list;
        }
    }

    internal sealed record MapLiteral(IReadOnlyList<(CelNode Key, CelNode Value)> Entries) : CelNode
    {
        public override IEnumerable<CelNode> Children => Entries.SelectMany(e => new[] { e.Key, e.Value });

        public override object Evaluate(CelScope scope, ref int steps)
        {
            var map = new CelMap();
            foreach ((CelNode k, CelNode v) in Entries) map[CelValues.AsString(k.Evaluate(scope, ref steps))] = v.Evaluate(scope, ref steps);
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

        public override object Evaluate(CelScope scope, ref int steps)
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
                "-" => value is long l ? -l : value is double d ? -d : throw new CelException("'-' needs a number."),
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

        public override object Evaluate(CelScope scope, ref int steps)
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
                    string s => new StringInfo(s).LengthInTextElements,
                    List<object?> l => l.Count,
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
