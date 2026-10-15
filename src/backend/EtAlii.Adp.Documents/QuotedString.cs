namespace EtAlii.Adp.Documents;

/// <summary>A value written as a double-quoted string of a line-based language.</summary>
public static class QuotedString
{
    /// <summary><paramref name="value"/> in double quotes, each quote inside it written <c>\"</c>.</summary>
    public static string Write(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
