namespace EtAlii.Adp.History;

/// <summary>The name an edit gives something new, so it does not take the name of something already there.</summary>
public static class UniqueName
{
    /// <summary><paramref name="name"/>, or it with the lowest number that makes it unique among <paramref name="taken"/>.</summary>
    public static string Of(IEnumerable<string> taken, string name)
    {
        var names = taken.ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            var candidate = $"{name} {number}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
