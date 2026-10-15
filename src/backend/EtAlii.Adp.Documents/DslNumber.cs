using System.Globalization;

namespace EtAlii.Adp.Documents;

/// <summary>
/// A number as the text of a document or a property row: invariant culture, at most four decimals,
/// and a whole number without a decimal point, so a value reads the same everywhere and a drag does
/// not make a whole coordinate sprout digits.
/// </summary>
/// <remarks>
/// One format for every module (code-structure FR-047, behaviour-changes B-01 and B-20): the
/// functional-decomposition-graph property rows rounded to two decimals and dependency-graph wrote
/// full precision before they joined it.
/// </remarks>
public static class DslNumber
{
    /// <summary><paramref name="value"/> as document text.</summary>
    public static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary><paramref name="value"/> as document text, or empty where there is none.</summary>
    public static string Format(double? value) => value is { } number ? Format(number) : "";
}
