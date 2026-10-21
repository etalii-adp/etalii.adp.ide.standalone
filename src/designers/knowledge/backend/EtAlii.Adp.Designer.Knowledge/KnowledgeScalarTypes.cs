using System.Globalization;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>Text: compared, sorted and grouped as itself, and what every other type converts to and from.</summary>
internal sealed class KnowledgeTextType() : KnowledgeValueType("text", "text", ["is", "is-not", "contains", "does-not-contain", "starts-with", "ends-with"])
{
    public override string Write(string value, out object? written)
    {
        written = value;
        return "";
    }

    /// <summary>A text becomes what it says in the new type, where it says something in it.</summary>
    public override void ConvertTo(KnowledgeValueType to, KnowledgeConversion conversion) => to.FromText(conversion.Value.Trim(), conversion);
}

/// <summary>A number: compared and sorted as one, and figured over in the line under its column.</summary>
internal sealed class KnowledgeNumberType() : KnowledgeValueType("number", "number", ["equals", "does-not-equal", "greater-than", "less-than", "at-least", "at-most"])
{
    public const string Expected = "A number is expected.";

    public override KnowledgeFigures Figures => KnowledgeFigures.Numbers;

    protected override bool HoldsGiven(string comparison, IReadOnlyList<string> values, string given)
    {
        if (!double.TryParse(given, NumberStyles.Float, CultureInfo.InvariantCulture, out var wanted))
        {
            return true;
        }

        if (!double.TryParse(First(values), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            // A row without a number is not equal to one, and is neither more nor less.
            return comparison == "does-not-equal";
        }

        return comparison switch
        {
            "equals" => number.Equals(wanted),
            "does-not-equal" => !number.Equals(wanted),
            "greater-than" => number > wanted,
            "less-than" => number < wanted,
            "at-least" => number >= wanted,
            "at-most" => number <= wanted,
            _ => true,
        };
    }

    public override KnowledgeSortKey SortKeyOf(KnowledgeProperty property, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? new KnowledgeSortKey(number, "") : KnowledgeSortKey.Empty;

    /// <summary>A whole number as one, else a finite real number.</summary>
    public override string Write(string value, out object? written)
    {
        written = value;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
        {
            written = whole;
            return "";
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && double.IsFinite(real))
        {
            written = real;
            return "";
        }

        return Expected;
    }

    public override void ConvertTo(KnowledgeValueType to, KnowledgeConversion conversion) => ToText(to, conversion);

    public override void FromText(string value, KnowledgeConversion conversion) => FromWritten(value, conversion);
}

/// <summary>A checkbox: ticked or not, and never empty - a box that is not ticked is a cell that is not there.</summary>
internal sealed class KnowledgeCheckboxType() : KnowledgeValueType("checkbox", "checked", ["is-checked", "is-not-checked"])
{
    public override bool CanGroup => true;

    public override bool IsValue(string value) => KnowledgeValues.IsTrue(value);

    /// <summary>A checkbox shows no text, and is never what a search finds.</summary>
    public override string? Shown(KnowledgeProperty property, string value, Func<string, string, string>? labelOf) => null;

    public override bool Holds(string comparison, IReadOnlyList<string> values, string given) => comparison switch
    {
        "is-checked" => First(values) == "true",
        "is-not-checked" => First(values) != "true",
        _ => base.Holds(comparison, values, given),
    };

    /// <summary>Unticked first.</summary>
    public override KnowledgeSortKey SortKeyOf(KnowledgeProperty property, string value) => new(value == "true" ? 1 : 0, "");

    /// <summary>Unticked and ticked, and no group of rows without a value: unticked is one.</summary>
    public override IEnumerable<(string Key, string Label)> NamedGroups(KnowledgeProperty grouped) => [("false", "Not checked"), ("true", "Checked")];

    public override bool HasNoValueGroup => false;

    public override IEnumerable<string> GroupsOf(IReadOnlyList<string> values) => [values.FirstOrDefault() == "true" ? "true" : "false"];

    public override bool TakesGroup(KnowledgeProperty grouped, string group) => group == "true";

    /// <summary>Ticked is stored as true; unticked is nothing stored.</summary>
    public override string Write(string value, out object? written)
    {
        written = value == "true" ? true : null;
        return "";
    }

    public override void ConvertTo(KnowledgeValueType to, KnowledgeConversion conversion) => ToText(to, conversion);

    /// <summary>A text that says true or false, whatever its case, ticks the box or leaves it unticked.</summary>
    public override void FromText(string value, KnowledgeConversion conversion)
    {
        if (value.ToLowerInvariant() is "true" or "false")
        {
            conversion.Replace((StoredKey, value.Equals("true", StringComparison.OrdinalIgnoreCase)));
        }
    }
}

/// <summary>
/// A type the designer's specification does not have, written by hand or by a later version: its
/// values are shown as text, stored under its own name, and cannot be changed here.
/// </summary>
internal sealed class KnowledgeUnknownType(string name) : KnowledgeValueType(name, name, [])
{
    public override bool IsKnown => false;
}
