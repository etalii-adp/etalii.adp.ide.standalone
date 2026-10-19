using System.Globalization;

namespace EtAlii.Adp.Specification.Json;

/// <summary>The JSON Pointers (RFC 6901) a loader names the place of a diagnostic with.</summary>
public static class JsonPointer
{
    /// <summary>One reference token: <c>~</c> and <c>/</c> escaped.</summary>
    public static string Token(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>The pointer to the member <paramref name="name"/> of the value at <paramref name="parent"/>.</summary>
    public static string Append(string parent, string name) => parent + "/" + Token(name);

    /// <summary>The pointer to the item <paramref name="index"/> of the array at <paramref name="parent"/>.</summary>
    public static string Append(string parent, int index) => parent + "/" + index.ToString(CultureInfo.InvariantCulture);
}
