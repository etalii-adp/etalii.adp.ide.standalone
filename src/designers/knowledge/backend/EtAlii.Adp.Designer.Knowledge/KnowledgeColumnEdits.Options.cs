using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Specification.Fbl.Planning;
using static EtAlii.Adp.Designer.Knowledge.KnowledgeEdits;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>A selection's options: added, renamed, recoloured, moved and deleted.</summary>
internal static partial class KnowledgeColumnEdits
{
    private const string NotASelection = "Only a selection has options.";
    private const string OptionGone = "That option is no longer one of this property's.";
    public const string OptionNameNeeded = "An option needs a name.";

    internal static KnowledgeEdit AddOption(KnowledgeTable table, TableGesture gesture, Func<string> newId)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (!property.Type.HoldsOptions)
        {
            return Refused(NotASelection);
        }

        return OptionName(property, null, gesture, out var name) is { Length: > 0 } refusal
            ? Refused(refusal)
            : KnowledgeEdit.Of(new ModelChange.Add("Option", newId(), Attributes(("name", name), ("colour", ColourFor(property, name))), property.Id));
    }

    internal static KnowledgeEdit RenameOption(KnowledgeTable table, TableGesture gesture) =>
        ForOption(table, gesture, (property, option) => RenameOption(property, option, gesture));

    internal static KnowledgeEdit RecolourOption(KnowledgeTable table, TableGesture gesture) =>
        ForOption(table, gesture, (_, option) => RecolourOption(option, gesture));

    internal static KnowledgeEdit MoveOption(KnowledgeTable table, TableGesture gesture) =>
        ForOption(table, gesture, (property, option) => MoveTo(option.Id, property.Id, [.. property.Options.Select(candidate => candidate.Id)], gesture.Index));

    internal static KnowledgeEdit DeleteOption(KnowledgeTable table, TableGesture gesture) =>
        ForOption(table, gesture, (property, option) => DeleteOption(table, property, option));

    /// <summary>The selection a gesture names, and the option of it the gesture names, handed to what the gesture does.</summary>
    private static KnowledgeEdit ForOption(KnowledgeTable table, TableGesture gesture, Func<KnowledgeProperty, KnowledgeOption, KnowledgeEdit> edit)
    {
        if (table.Properties.FirstOrDefault(candidate => candidate.Id == gesture.ColumnId) is not { } property)
        {
            return Refused(PropertyGone);
        }

        if (!property.Type.HoldsOptions)
        {
            return Refused(NotASelection);
        }

        return property.Options.FirstOrDefault(candidate => candidate.Id == gesture.TargetId) is { } option ? edit(property, option) : Refused(OptionGone);
    }

    /// <summary>A name for an option of a property: given, and not the name of another of its options.</summary>
    private static string OptionName(KnowledgeProperty property, string? exceptId, TableGesture gesture, out string name)
    {
        name = (gesture.Values.FirstOrDefault() ?? "").Trim();
        var given = name;
        if (name.Length == 0)
        {
            return OptionNameNeeded;
        }

        return property.Options.Any(other => other.Id != exceptId && string.Equals(other.Name, given, StringComparison.OrdinalIgnoreCase))
            ? $"'{property.Name}' already has an option called '{given}'."
            : "";
    }

    /// <summary>A rename is one place in the file: rows hold an option's id, never its name.</summary>
    private static KnowledgeEdit RenameOption(KnowledgeProperty property, KnowledgeOption option, TableGesture gesture)
    {
        if (OptionName(property, option.Id, gesture, out var name) is { Length: > 0 } refusal)
        {
            return Refused(refusal);
        }

        return name == option.Name ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(Set(option.Id, ("name", name)));
    }

    private static KnowledgeEdit RecolourOption(KnowledgeOption option, TableGesture gesture)
    {
        var colour = gesture.Settings.GetValueOrDefault("colour") ?? "";
        if (!KnowledgeVocabulary.Colours.Contains(colour))
        {
            return Refused($"'{colour}' is not a colour an option can have.");
        }

        // The default colour is what an option has when the file says nothing.
        return colour == option.Colour ? KnowledgeEdit.Nothing : KnowledgeEdit.Of(Set(option.Id, ("colour", colour == "default" ? null : colour)));
    }

    /// <summary>
    /// An option deleted with every value that names it, in one step: the cells of a selection, the
    /// items of a multiple selection and the conditions comparing with it go with the option as the
    /// bindings cascade, and what a view grouped by this property remembered of the option's group
    /// goes here.
    /// </summary>
    private static KnowledgeEdit DeleteOption(KnowledgeTable table, KnowledgeProperty property, KnowledgeOption option)
    {
        List<ModelChange> changes = [];
        foreach (var view in table.Views.Where(view => view.GroupBy == property.Id))
        {
            changes.AddRange(new[] { ("groupOrder", view.GroupOrder), ("hiddenGroups", view.HiddenGroups), ("collapsed", view.Collapsed) }
                .Where(setting => setting.Item2.Contains(option.Id))
                .Select(setting => (ModelChange)new ModelChange.Remove($"{view.Id}/{setting.Item1}/{option.Id}")));
        }

        changes.Add(new ModelChange.Remove(option.Id));
        return new KnowledgeEdit(changes);
    }
}
