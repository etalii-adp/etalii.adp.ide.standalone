namespace EtAlii.Adp.Specification.Cel;

/// <summary>
/// CEL's lists extension: <c>lists.range(n)</c>, and on a list <c>distinct()</c>, <c>flatten()</c>
/// (one level, or <c>flatten(depth)</c>), <c>slice(start, end)</c>, <c>sort()</c> and the macro
/// <c>sortBy(x, key)</c>. Both sorts are stable and order strings by code point (DISL §12.1);
/// <c>reverse()</c> and <c>join()</c> are shared with strings.
/// </summary>
public static class CelLists
{
    public static void Register(CelEnvironment environment)
    {
        environment.AddFunction(CelFunction.Global("lists.range", 1, a => Range(CelValues.AsInt(a[0])), a => a[0] is long n ? 1 + Math.Max(n, 0) : 1));
        environment.AddFunction(CelFunction.Receiver("distinct", 0, a => Distinct(CelValues.AsList(a[0])), a => Squared(a[0])));
        environment.AddFunction(new CelFunction("flatten", CelCallStyle.Receiver, 0, 1, call => Flatten(CelValues.AsList(call[0]), call.Count > 1 ? CelValues.AsInt(call[1]) : 1), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("slice", 2, a => Slice(CelValues.AsList(a[0]), CelValues.AsInt(a[1]), CelValues.AsInt(a[2])), CelCore.Length));
        environment.AddFunction(CelFunction.Receiver("sort", 0, a => Sort(CelValues.AsList(a[0]), item => item), Logarithmic));
        environment.AddMacro(new CelMacro("sortBy", (items, key) => Sort(items, key)));
    }

    private static List<object?> Range(long count)
    {
        var list = new List<object?>();
        for (var i = 0L; i < count; i++) list.Add(i);
        return list;
    }

    private static List<object?> Distinct(IReadOnlyList<object?> list)
    {
        var kept = new List<object?>();
        foreach (var item in list)
        {
            if (!kept.Any(k => CelValues.Equal(k, item))) kept.Add(item);
        }
        return kept;
    }

    private static List<object?> Flatten(IReadOnlyList<object?> list, long depth)
    {
        if (depth < 0) throw new CelException("flatten() needs a depth of 0 or more.");
        var flat = new List<object?>();
        foreach (var item in list)
        {
            if (depth > 0 && item is IReadOnlyList<object?> inner) flat.AddRange(Flatten(inner, depth - 1));
            else flat.Add(item);
        }
        return flat;
    }

    private static List<object?> Slice(IReadOnlyList<object?> list, long start, long end)
    {
        if (start < 0 || end < start || end > list.Count) throw new CelException($"slice({start}, {end}) is out of range for a list of {list.Count}.");
        return list.Skip((int)start).Take((int)(end - start)).ToList();
    }

    /// <summary>A stable sort by <paramref name="key"/>; the keys must be all strings, all numbers or all bools.</summary>
    private static List<object?> Sort(IReadOnlyList<object?> list, Func<object?, object?> key)
    {
        var keyed = list.Select(item => (Item: item, Key: key(item))).ToList();
        var kinds = keyed.Select(k => k.Key switch
        {
            string => "string",
            long or double => "number",
            bool => "bool",
            _ => throw new CelException("sort() needs strings, numbers or bools."),
        }).Distinct().Count();
        if (kinds > 1) throw new CelException("sort() needs values of one type.");
        // OrderBy is a stable sort, which DISL §12.1 requires; List.Sort is not.
        return keyed.OrderBy(k => k.Key, Comparer<object?>.Create(CelValues.Compare)).Select(k => k.Item).ToList();
    }

    private static long Squared(object? list) => list is IReadOnlyList<object?> l ? 1 + ((long)l.Count * l.Count) : 1;

    private static long Logarithmic(IReadOnlyList<object?> arguments) =>
        arguments[0] is IReadOnlyList<object?> l && l.Count > 1 ? 1 + (long)(l.Count * Math.Log2(l.Count)) : 1;
}
