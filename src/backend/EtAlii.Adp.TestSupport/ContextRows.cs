using EtAlii.Adp.Context;

namespace EtAlii.Adp.TestSupport;

/// <summary>What a test reads off a property grid: one row, by its id.</summary>
public static class ContextRows
{
    /// <summary>The one row of <paramref name="rows"/> with the id <paramref name="id"/>; none, or two, throws, naming the ids there are.</summary>
    public static ContextPropertyDefinition Row(this IEnumerable<ContextPropertyDefinition> rows, string id)
    {
        var all = rows.ToList();
        var matching = all.Where(row => row.Id == id).ToList();
        return matching.Count == 1
            ? matching[0]
            : throw new InvalidOperationException(
                $"Expected one row {id}, found {matching.Count} among [{string.Join(", ", all.Select(row => row.Id))}].");
    }
}
