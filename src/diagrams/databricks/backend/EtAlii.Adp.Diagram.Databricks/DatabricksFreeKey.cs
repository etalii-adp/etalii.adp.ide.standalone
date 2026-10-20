using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The CEL function of the job definition's <c>net.etalii.adp.databricks.taskKeys</c> plugin (DISL §13.1.1):
/// <c>freeTaskKey(keys, type)</c>, the first <c>&lt;type&gt;_&lt;n&gt;</c>, counting <c>n</c> from 1, that is not
/// in <c>keys</c>, which a dropped task is given.
/// </summary>
/// <remarks>
/// <b>It stands in for finding A11</b> (databricks-disl-fbl, gap 9): CEL has no range to search, and
/// <c>persistence.ids.suffix</c> appends to a taken id rather than making the first one <c>_1</c>, so counting the
/// tasks of a type and adding one, as the draft did, collides after a removal. It is deleted when DISL answers gap 9.
/// </remarks>
internal static class DatabricksFreeKey
{
    /// <summary>The function's name, as the job definition declares it.</summary>
    private const string Name = "freeTaskKey";

    /// <summary>The implementations, for <see cref="BundledDefinition.Load(System.Reflection.Assembly, string, DislPluginFunctions)"/>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add(Name, arguments => Of(Keys(arguments[0]), Text(arguments[1])));

    /// <summary>The first <c>&lt;<paramref name="type"/>&gt;_&lt;n&gt;</c>, counting <c>n</c> from 1, that <paramref name="keys"/> does not hold.</summary>
    public static string Of(IReadOnlyCollection<string> keys, string type)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(type);

        var taken = keys.ToHashSet(StringComparer.Ordinal);
        for (var counter = 1; ; counter++)
        {
            var candidate = $"{type}_{counter}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string[] Keys(object? argument) =>
        argument is IEnumerable<object?> keys
            ? [.. keys.Select(Text)]
            : throw new CelException("freeTaskKey() takes a list of task keys and a task type.");

    private static string Text(object? argument) =>
        argument as string ?? throw new CelException("freeTaskKey() takes a list of task keys and a task type.");
}
