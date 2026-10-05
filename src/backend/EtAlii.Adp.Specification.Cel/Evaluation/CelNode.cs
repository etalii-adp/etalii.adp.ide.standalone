namespace EtAlii.Adp.Specification.Cel;

internal abstract record CelNode
{
    public virtual IEnumerable<CelNode> Children => [];

    public abstract object? Evaluate(CelScope scope);

    internal sealed record Literal(object? Value) : CelNode
    {
        public override object? Evaluate(CelScope scope) => Value;
    }

    internal sealed record Ident(string Name) : CelNode
    {
        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            return scope.Lookup(Name);
        }
    }

    internal sealed record ListLiteral(IReadOnlyList<CelNode> Items) : CelNode
    {
        public override IEnumerable<CelNode> Children => Items;

        public override object Evaluate(CelScope scope)
        {
            var list = new List<object?>(Items.Count);
            foreach (var item in Items) list.Add(item.Evaluate(scope));
            return list;
        }
    }

    internal sealed record MapLiteral(IReadOnlyList<(CelNode Key, CelNode Value)> Entries) : CelNode
    {
        public override IEnumerable<CelNode> Children => Entries.SelectMany(e => new[] { e.Key, e.Value });

        public override object Evaluate(CelScope scope)
        {
            var map = new CelMap();
            foreach ((CelNode k, CelNode v) in Entries) map[CelValues.AsString(k.Evaluate(scope))] = v.Evaluate(scope);
            return map;
        }
    }

    /// <summary><c>x.name</c>, and <c>x.?name</c> when <paramref name="Optional"/>.</summary>
    internal sealed record Member(CelNode Target, string Name, bool Optional) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target];

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            var target = Target.Evaluate(scope);
            if (target is CelError) return target;
            if (target is CelOptional optional)
            {
                // Selection on an optional chains: none stays none, a value is selected from.
                return optional.HasValue ? Select(optional.Value, true) : CelOptional.None;
            }
            return Select(target, Optional);
        }

        private object? Select(object? target, bool optional)
        {
            switch (target)
            {
                case IReadOnlyDictionary<string, object?> map:
                    if (map.TryGetValue(Name, out var value)) return optional ? CelOptional.Of(value) : value;
                    return optional ? CelOptional.None : throw new CelException($"No such key: '{Name}'.");
                case ICelObject host:
                    if (optional) return host.HasMember(Name) && host.TryGetMember(Name, out var present) ? CelOptional.Of(present) : CelOptional.None;
                    return host.TryGetMember(Name, out var member) ? member : throw new CelException($"No such field: '{Name}'.");
                default:
                    throw new CelException($"'{Name}' is selected from a value that is not a map.");
            }
        }
    }

    /// <summary><c>x[key]</c>, and <c>x[?key]</c> when <paramref name="Optional"/>.</summary>
    internal sealed record Index(CelNode Target, CelNode Key, bool Optional) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target, Key];

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            var target = Target.Evaluate(scope);
            var key = Key.Evaluate(scope);
            if (target is CelOptional optional)
            {
                return optional.HasValue ? Lookup(optional.Value, key, true) : CelOptional.None;
            }
            return Lookup(target, key, Optional);
        }

        private static object? Lookup(object? target, object? key, bool optional)
        {
            switch (target)
            {
                case IReadOnlyList<object?> list:
                    var i = CelValues.AsInt(key);
                    if (i >= 0 && i < list.Count) return optional ? CelOptional.Of(list[(int)i]) : list[(int)i];
                    return optional ? CelOptional.None : throw new CelException($"Index {i} is out of range.");
                case IReadOnlyDictionary<string, object?> map:
                    var k = CelValues.AsString(key);
                    if (map.TryGetValue(k, out var value)) return optional ? CelOptional.Of(value) : value;
                    return optional ? CelOptional.None : throw new CelException($"No such key: '{k}'.");
                default:
                    throw new CelException("Only a list or a map can be indexed.");
            }
        }
    }

    internal sealed record Has(CelNode Target, string Name) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target];

        public override object Evaluate(CelScope scope)
        {
            scope.Step();
            var target = Target.Evaluate(scope);
            return target switch
            {
                IReadOnlyDictionary<string, object?> map => map.ContainsKey(Name),
                ICelObject host => host.HasMember(Name),
                _ => throw new CelException("has() needs a map."),
            };
        }
    }

    internal sealed record Unary(string Operator, CelNode Operand) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Operand];

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            var value = Operand.Evaluate(scope);
            return Operator switch
            {
                "!" => value is bool b ? !b : throw new CelException("'!' needs a bool."),
                // Each arm boxes its own type: a conditional of long and double would make every negated int a double.
                "-" => value is long l ? (object)(-l) : value is double d ? -d : throw new CelException("'-' needs a number."),
                _ => throw new CelException($"Unknown operator '{Operator}'."),
            };
        }
    }

    internal sealed record Binary(string Operator, CelNode Left, CelNode Right) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Left, Right];

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            if (Operator is "&&" or "||")
            {
                object? left;
                try { left = Left.Evaluate(scope); }
                catch (CelException e) when (!IsBudget(e)) { left = new CelError(e.Message); }
                if (Operator == "&&" && left is false) return false;
                if (Operator == "||" && left is true) return true;
                object? right;
                try { right = Right.Evaluate(scope); }
                catch (CelException e) when (!IsBudget(e)) { right = new CelError(e.Message); }
                if (Operator == "&&" && right is false) return false;
                if (Operator == "||" && right is true) return true;
                if (left is bool && right is bool) return Operator == "&&";
                throw new CelException(left is CelError le ? le.Message : right is CelError re ? re.Message : $"'{Operator}' needs bools.");
            }
            var l = Left.Evaluate(scope);
            var r = Right.Evaluate(scope);
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
                        IReadOnlyList<object?> list => list.Any(item => CelValues.Equal(item, l)),
                        IReadOnlyDictionary<string, object?> map => l is string key && map.ContainsKey(key),
                        _ => throw new CelException("'in' needs a list or a map on its right."),
                    };
                case "+":
                    return (l, r) switch
                    {
                        (long a, long b) => a + b,
                        (string a, string b) => a + b,
                        (IReadOnlyList<object?> a, IReadOnlyList<object?> b) => a.Concat(b).ToList(),
                        _ => CelValues.AsDouble(l) + CelValues.AsDouble(r),
                    };
                // An int result is boxed as itself: a conditional of long and double would be a double.
                case "-": return (l, r) is (long a1, long b1) ? (object)(a1 - b1) : CelValues.AsDouble(l) - CelValues.AsDouble(r);
                case "*": return (l, r) is (long a2, long b2) ? (object)(a2 * b2) : CelValues.AsDouble(l) * CelValues.AsDouble(r);
                case "/":
                    if ((l, r) is (long a3, long b3)) return b3 == 0 ? throw new CelException("Division by zero.") : a3 / b3;
                    return CelValues.AsDouble(l) / CelValues.AsDouble(r);
                case "%":
                    if ((l, r) is (long a4, long b4)) return b4 == 0 ? throw new CelException("Modulus by zero.") : a4 % b4;
                    throw new CelException("'%' needs ints.");
                default: throw new CelException($"Unknown operator '{Operator}'.");
            }
        }

        // Running out of budget is never absorbed by a short circuit: the evaluation is over.
        private static bool IsBudget(CelException e) => e.Message == "The expression exceeded its evaluation budget.";
    }

    internal sealed record Conditional(CelNode Condition, CelNode Then, CelNode Else) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Condition, Then, Else];

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            var condition = Condition.Evaluate(scope);
            return condition switch
            {
                true => Then.Evaluate(scope),
                false => Else.Evaluate(scope),
                _ => throw new CelException("A conditional needs a bool."),
            };
        }
    }

    /// <summary><c>target.macro(variable, body)</c>: a comprehension over a list, or over a map's keys.</summary>
    internal sealed record Comprehension(CelMacro Macro, CelNode Target, string Variable, CelNode Body) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Target, Body];

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            var target = Target.Evaluate(scope);
            IReadOnlyList<object?> items = target switch
            {
                IReadOnlyList<object?> list => list,
                IReadOnlyDictionary<string, object?> map => map.Keys.ToList<object?>(),
                _ => throw new CelException($"'{Macro.Name}' needs a list or a map."),
            };
            return Macro.Expand(items, item => Body.Evaluate(scope.With(Variable, item)));
        }
    }

    /// <summary><c>cel.bind(variable, init, body)</c>: <c>body</c> with <c>variable</c> bound to <c>init</c>, evaluated once.</summary>
    internal sealed record Bind(string Variable, CelNode Init, CelNode Body) : CelNode
    {
        public override IEnumerable<CelNode> Children => [Init, Body];

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            var value = Init.Evaluate(scope);
            return Body.Evaluate(scope.With(Variable, value));
        }
    }

    /// <summary>A call of <paramref name="Function"/>; for a receiver call, <paramref name="Receiver"/> is the first argument its body sees.</summary>
    internal sealed record Call(CelFunction Function, CelNode? Receiver, IReadOnlyList<CelNode> Arguments) : CelNode
    {
        public override IEnumerable<CelNode> Children => Receiver is null ? Arguments : Arguments.Prepend(Receiver);

        public override object? Evaluate(CelScope scope)
        {
            scope.Step();
            var receiver = Receiver?.Evaluate(scope);
            var args = new List<object?>(Arguments.Count + 1);
            foreach (var a in Arguments) args.Add(a.Evaluate(scope));
            if (Receiver is not null)
            {
                if (receiver is ICelObject host && host.TryInvoke(Function.Name, args, out var result))
                {
                    scope.Budget.Charge(Function.CostOf(args));
                    return result;
                }
                if (Function.Body is null) throw new CelException($"'{Function.Name}()' is not a method of this value.");
                args.Insert(0, receiver);
            }
            scope.Budget.Charge(Function.CostOf(args));
            return Function.Body!(new CelCall(args, scope.Budget));
        }
    }
}
