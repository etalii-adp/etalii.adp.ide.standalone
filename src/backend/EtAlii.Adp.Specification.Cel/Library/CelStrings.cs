using System.Text;

namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// CEL's string functions (cel-spec's strings extension), on a string receiver unless noted:
/// <c>charAt</c>, <c>contains</c>, <c>endsWith</c>, <c>indexOf</c>, <c>lastIndexOf</c>,
/// <c>lowerAscii</c>, <c>replace</c>, <c>reverse</c>, <c>split</c>, <c>startsWith</c>,
/// <c>substring</c>, <c>trim</c> and <c>upperAscii</c>; <c>join</c> on a list of strings; and DISL's
/// global <c>lower(s)</c> and <c>upper(s)</c>, which map case over all of Unicode where the
/// <c>Ascii</c> pair maps only A to Z. Indexes count code points.
/// </summary>
public static class CelStrings
{
    public static void Register(CelEnvironment environment)
    {
        environment.AddFunction(CelFunction.Receiver("startsWith", 1, a => Text(a[0]).StartsWith(Text(a[1]), StringComparison.Ordinal), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("endsWith", 1, a => Text(a[0]).EndsWith(Text(a[1]), StringComparison.Ordinal), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("contains", 1, a => Text(a[0]).Contains(Text(a[1]), StringComparison.Ordinal), CelCore.Length));
        environment.AddFunction(new CelFunction("replace", CelCallStyle.Receiver, 2, 3, call => Replace(call.Arguments), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("lowerAscii", 0, a => MapAscii(Text(a[0]), 'A', 'Z', 'a' - 'A'), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("upperAscii", 0, a => MapAscii(Text(a[0]), 'a', 'z', 'A' - 'a'), CelCore.Length));
        environment.AddFunction(CelFunction.Global("lower", 1, a => Text(a[0]).ToLowerInvariant(), CelCore.Length));
        environment.AddFunction(CelFunction.Global("upper", 1, a => Text(a[0]).ToUpperInvariant(), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("charAt", 1, a => CharAt(Text(a[0]), CelValues.AsInt(a[1])), CelCore.Length));
        environment.AddFunction(new CelFunction("indexOf", CelCallStyle.Receiver, 1, 2, call => IndexOf(call.Arguments), CelCore.Length));
        environment.AddFunction(new CelFunction("lastIndexOf", CelCallStyle.Receiver, 1, 2, call => LastIndexOf(call.Arguments), CelCore.Length));
        environment.AddFunction(new CelFunction("substring", CelCallStyle.Receiver, 1, 2, call => Substring(call.Arguments), CelCore.Length));
        environment.AddFunction(new CelFunction("split", CelCallStyle.Receiver, 1, 2, call => Split(call.Arguments), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("trim", 0, a => Text(a[0]).Trim(), CelCore.Length));
        environment.AddFunction(new CelFunction("join", CelCallStyle.Receiver, 0, 1, call => Join(call.Arguments), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("reverse", 0, a => Reverse(a[0]), CelCore.Length));
    }

    private static string Text(object? value) => CelValues.AsString(value);

    private static string MapAscii(string text, char from, char to, int shift)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text) builder.Append(c >= from && c <= to ? (char)(c + shift) : c);
        return builder.ToString();
    }

    private static string CharAt(string text, long index)
    {
        var points = CodePoints.Of(text);
        if (index < 0 || index > points.Length) throw new CelException($"charAt({index}) is out of range for a string of {points.Length}.");
        return index == points.Length ? "" : CodePoints.Text(points.AsSpan((int)index, 1));
    }

    private static object IndexOf(IReadOnlyList<object?> a)
    {
        if (a[0] is IReadOnlyList<object?> list)
        {
            // DISL §12.4 l.indexOf(v): the first index, or -1.
            for (var i = 0; i < list.Count; i++) if (CelValues.Equal(list[i], a[1])) return (long)i;
            return -1L;
        }
        var text = CodePoints.Of(Text(a[0]));
        var sought = CodePoints.Of(Text(a[1]));
        var start = a.Count > 2 ? CelValues.AsInt(a[2]) : 0;
        if (start < 0 || start > text.Length) throw new CelException($"indexOf() start {start} is out of range for a string of {text.Length}.");
        return (long)CodePoints.IndexOf(text, sought, (int)start);
    }

    private static object LastIndexOf(IReadOnlyList<object?> a)
    {
        if (a[0] is IReadOnlyList<object?> list)
        {
            for (var i = list.Count - 1; i >= 0; i--) if (CelValues.Equal(list[i], a[1])) return (long)i;
            return -1L;
        }
        var text = CodePoints.Of(Text(a[0]));
        var sought = CodePoints.Of(Text(a[1]));
        var start = a.Count > 2 ? CelValues.AsInt(a[2]) : text.Length;
        if (start < 0 || start > text.Length) throw new CelException($"lastIndexOf() start {start} is out of range for a string of {text.Length}.");
        return (long)CodePoints.LastIndexOf(text, sought, (int)start);
    }

    private static string Substring(IReadOnlyList<object?> a)
    {
        var points = CodePoints.Of(Text(a[0]));
        var start = CelValues.AsInt(a[1]);
        var end = a.Count > 2 ? CelValues.AsInt(a[2]) : points.Length;
        if (start < 0 || start > points.Length || end < start || end > points.Length)
        {
            throw new CelException($"substring({start}, {end}) is out of range for a string of {points.Length}.");
        }
        return CodePoints.Text(points.AsSpan((int)start, (int)(end - start)));
    }

    private static string Replace(IReadOnlyList<object?> a)
    {
        var text = Text(a[0]);
        var old = Text(a[1]);
        var replacement = Text(a[2]);
        var limit = a.Count > 3 ? CelValues.AsInt(a[3]) : -1;
        if (limit < 0) return text.Replace(old, replacement, StringComparison.Ordinal);
        var builder = new StringBuilder();
        var position = 0;
        for (var done = 0L; done < limit; done++)
        {
            var found = old.Length == 0 ? (position <= text.Length ? position : -1) : text.IndexOf(old, position, StringComparison.Ordinal);
            if (found < 0) break;
            builder.Append(text, position, found - position).Append(replacement);
            if (old.Length == 0)
            {
                // An empty match sits before each code point; step over one.
                if (found >= text.Length)
                {
                    position = text.Length + 1;
                    break;
                }
                var width = char.IsSurrogatePair(text, found) ? 2 : 1;
                builder.Append(text, found, width);
                position = found + width;
                continue;
            }
            position = found + old.Length;
        }
        if (position <= text.Length) builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    private static List<object?> Split(IReadOnlyList<object?> a)
    {
        var text = Text(a[0]);
        var separator = Text(a[1]);
        var limit = a.Count > 2 ? CelValues.AsInt(a[2]) : -1;
        if (limit == 0) return [];
        if (separator.Length == 0)
        {
            var points = CodePoints.Of(text);
            var parts = new List<object?>();
            for (var i = 0; i < points.Length; i++)
            {
                if (limit > 0 && parts.Count == limit - 1)
                {
                    parts.Add(CodePoints.Text(points.AsSpan(i)));
                    return parts;
                }
                parts.Add(CodePoints.Text(points.AsSpan(i, 1)));
            }
            return parts;
        }
        var pieces = limit < 0 ? text.Split(separator) : text.Split(separator, (int)Math.Min(limit, int.MaxValue));
        return [.. pieces];
    }

    private static string Join(IReadOnlyList<object?> a)
    {
        var list = CelValues.AsList(a[0]);
        var separator = a.Count > 1 ? Text(a[1]) : "";
        return string.Join(separator, list.Select(item => item as string ?? throw new CelException("join() needs a list of strings.")));
    }

    private static object Reverse(object? value)
    {
        if (value is IReadOnlyList<object?> list) return list.Reverse().ToList();
        var points = CodePoints.Of(Text(value));
        Array.Reverse(points);
        return CodePoints.Text(points);
    }
}
