namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// A type whose values are the property's options, by id: shown by the option's name, sorted by
/// its place among the options, and grouped an option each, in the options' order.
/// </summary>
internal abstract class KnowledgeOptionType(string name, IReadOnlyList<string> comparisons) : KnowledgeValueType(name, "option", comparisons)
{
    public override bool HoldsOptions => true;

    public override bool CanGroup => true;

    public override string Shown(KnowledgeProperty property, string value, Func<string, string, string>? labelOf) =>
        property.Options.FirstOrDefault(option => option.Id == value)?.Name ?? "";

    /// <summary>By the option's place among the options, not by its name.</summary>
    public override KnowledgeSortKey SortKeyOf(KnowledgeProperty property, string value)
    {
        for (var index = 0; index < property.Options.Count; index++)
        {
            if (property.Options[index].Id == value)
            {
                return new KnowledgeSortKey(index, "");
            }
        }

        return KnowledgeSortKey.Empty;
    }

    public override IEnumerable<(string Key, string Label)> NamedGroups(KnowledgeProperty grouped) => grouped.Options.Select(option => (option.Id, option.Name));

    /// <summary>A group that is one of the options; one that is not names no value to give.</summary>
    public override bool TakesGroup(KnowledgeProperty grouped, string group) => grouped.Options.Any(option => option.Id == group);
}

/// <summary>A selection: one of the property's options.</summary>
internal sealed class KnowledgeSelectionType() : KnowledgeOptionType("selection", ["is", "is-not"])
{
    protected override bool HoldsGiven(string comparison, IReadOnlyList<string> values, string given) => comparison switch
    {
        "is" => First(values) == given,
        "is-not" => First(values) != given,
        _ => true,
    };

    public override string Write(string value, out object? written)
    {
        written = value;
        return "";
    }

    /// <summary>To text by the option's name; to a multiple selection as its one option.</summary>
    public override void ConvertTo(KnowledgeValueType to, KnowledgeConversion conversion)
    {
        if (to is KnowledgeTextType && conversion.Names.TryGetValue(conversion.Value, out var name))
        {
            conversion.Replace((to.StoredKey, name));
        }
        else if (to is KnowledgeMultipleSelectionType)
        {
            conversion.Replace();
            conversion.AddItem(to.StoredKey, conversion.Value);
        }
    }

    /// <summary>A text that says something is the option of that name, made when the property has none.</summary>
    public override void FromText(string value, KnowledgeConversion conversion)
    {
        if (value.Length > 0)
        {
            conversion.Replace((StoredKey, conversion.Option(value)));
        }
    }
}

/// <summary>A multiple selection: any of the property's options, each an item of the cell.</summary>
internal sealed class KnowledgeMultipleSelectionType() : KnowledgeOptionType("multipleSelection", ["contains", "does-not-contain"])
{
    public override string CellKey => "options";

    public override bool HoldsSeveral => true;

    protected override bool HoldsGiven(string comparison, IReadOnlyList<string> values, string given) => HoldsAmong(comparison, values, given);

    /// <summary>A condition compares one option at a time.</summary>
    public override string WriteCondition(string value, out string key, out object? written)
    {
        key = StoredKey;
        written = value;
        return "";
    }

    /// <summary>To text as the options' names, when each is still an option; to a selection when it holds one.</summary>
    public override void ConvertTo(KnowledgeValueType to, KnowledgeConversion conversion)
    {
        if (to is KnowledgeTextType && conversion.Values.All(conversion.Names.ContainsKey))
        {
            conversion.Set((to.StoredKey, string.Join(", ", conversion.Values.Select(item => conversion.Names[item]))));
            foreach (var item in conversion.Values)
            {
                conversion.RemoveItem(item);
            }
        }
        else if (to is KnowledgeSelectionType && conversion.Values.Count == 1)
        {
            conversion.RemoveItem(conversion.Value);
            conversion.Set((to.StoredKey, conversion.Value));
        }
    }

    /// <summary>A text that says something is the one option of that name, made when the property has none.</summary>
    public override void FromText(string value, KnowledgeConversion conversion)
    {
        if (value.Length > 0)
        {
            var made = conversion.Option(value);
            conversion.Replace();
            conversion.AddItem(StoredKey, made);
        }
    }
}
