using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The operations on tags of <c>hierarchical-requirement.des</c> (FR-026): <c>addTag</c> and <c>removeTag</c> on an
/// entry, and <c>renameTag</c>, <c>recolourTag</c>, <c>moveTag</c> and <c>deleteTag</c> on a member of the tag list. The
/// tags an entry carries are in the Markdown file; the list's colours and order are in the registration's <c>tags</c>
/// block. An operation that changes both writes both in one step (spec 029 R17), byte for byte as the conformance
/// fixtures <c>hierarchical-requirement-tags</c> and <c>-tag-list</c> state.
/// </summary>
internal static class TagOperations
{
    public const string AddTag = "addTag";
    public const string RemoveTag = "removeTag";
    public const string RenameTag = "renameTag";
    public const string RecolourTag = "recolourTag";
    public const string MoveTag = "moveTag";
    public const string DeleteTag = "deleteTag";

    private const string TagsBlock = "tags";
    private const string TagsAttribute = "tags";

    /// <summary>The prefix of a tag's id, <c>'tag/' + name.lowerAscii()</c> (hierarchical-requirement.des, ids).</summary>
    private const string TagIdPrefix = "tag/";

    /// <summary>Whether <paramref name="operation"/> is one of these.</summary>
    public static bool Handles(string operation) => operation is AddTag or RemoveTag || TargetsATag(operation);

    /// <summary>Whether <paramref name="operation"/> targets a member of the tag list rather than an entry.</summary>
    public static bool TargetsATag(string operation) => operation is RenameTag or RecolourTag or MoveTag or DeleteTag;

    /// <summary>
    /// Whether <paramref name="parameters"/> can be written by <paramref name="operation"/>: the sentence saying why not, or
    /// empty. A name is trimmed and a space in it written <c>-</c>; what is left is a word without <c>#</c>.
    /// </summary>
    public static string Refusal(string operation, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        switch (operation)
        {
            case AddTag or RemoveTag or RenameTag:
                if (!parameters.TryGetValue("name", out var name) || Normalised(name).Length == 0) return "A tag needs a name.";
                if (operation == RemoveTag) return "";
                var normalised = Normalised(name);
                return normalised.Contains('#', StringComparison.Ordinal) || normalised.Any(char.IsWhiteSpace) ? $"'{name.Trim()}' cannot be a tag: a tag's name is one word without #." : "";
            case RecolourTag:
                return parameters.TryGetValue("colour", out var colour) && HierarchicalRequirementVocabulary.Enumerations["Colour"].Contains(colour)
                    ? ""
                    : $"'{parameters.GetValueOrDefault("colour", "")}' is not a colour.";
            default:
                return "";
        }
    }

    /// <summary>
    /// The files after <paramref name="operation"/> on <paramref name="targetId"/>, the surface's id of an entry or a tag's
    /// id: the body's bytes and the registration's before and after, each null when the operation does not write it; or
    /// the sentence saying why it cannot be, in which case nothing changed. A target the files no longer have is refused
    /// as a change made against an older file.
    /// </summary>
    /// <param name="registration">The registration's bytes, or null when the body has none yet.</param>
    public static TagEdit Apply(byte[] body, string bodyName, byte[]? registration, string registrationName, string operation, string targetId, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);
        if (Refusal(operation, parameters) is { Length: > 0 } refusal) return TagEdit.Refused(refusal);

        var read = HierarchicalRequirementBody.Read(body, bodyName, registration, registrationName);
        var document = read.Document;
        if (document.ReadOnlyReason.Length > 0) return TagEdit.Refused(document.ReadOnlyReason);
        var members = Members(document);
        var edit = new BodyEdit(read);
        ModelChange.Setting? setting;

        if (operation is AddTag or RemoveTag)
        {
            var ids = HierarchicalRequirementSurfaceMapper.NodeIds(document.Entries);
            if (document.Entries.FirstOrDefault(candidate => ids[candidate] == targetId) is not { } entry) return TagEdit.Refused(HierarchicalRequirementGestures.Drifted);
            var name = Normalised(parameters["name"]);
            if (operation == RemoveTag)
            {
                setting = null;
                SetTags(edit, entry, [.. entry.Tags.Where(tag => !Same(tag, name))]);
            }
            else
            {
                // A name equal to a member's in any case picks it, with the list's spelling; one equal to none creates it, last (FR-027).
                var found = members.FirstOrDefault(member => Same(member, name));
                setting = found is null ? new ModelChange.Setting(TagsBlock, name, "default") : null;
                if (!entry.Tags.Any(tag => Same(tag, name))) SetTags(edit, entry, [.. entry.Tags, found ?? name]);
            }
        }
        else
        {
            if (members.FirstOrDefault(member => TagIdPrefix + LowerAscii(member) == targetId) is not { } member) return TagEdit.Refused(HierarchicalRequirementGestures.Drifted);
            switch (operation)
            {
                case RenameTag:
                    var name = Normalised(parameters["name"]);
                    if (members.Any(other => other != member && Same(other, name))) return TagEdit.Refused($"There is already a tag named {name}.");
                    foreach (var carrier in Carriers(document, member))
                    {
                        SetTags(edit, carrier, [.. carrier.Tags.Select(tag => Same(tag, member) ? name : tag)]);
                    }

                    setting = name == member ? null : new ModelChange.Setting(TagsBlock, member, Rename: name);
                    break;
                case RecolourTag:
                    setting = new ModelChange.Setting(TagsBlock, member, parameters["colour"]);
                    break;
                case MoveTag:
                    if (!parameters.TryGetValue("after", out var after) || after.Length == 0)
                    {
                        setting = Same(members[0], member) ? null : new ModelChange.Setting(TagsBlock, member, Position: new SettingPosition(Before: members[0]));
                    }
                    else if (members.FirstOrDefault(other => TagIdPrefix + LowerAscii(other) == after) is { } previous)
                    {
                        setting = Same(previous, member) ? null : new ModelChange.Setting(TagsBlock, member, Position: new SettingPosition(After: previous));
                    }
                    else
                    {
                        return TagEdit.Refused(HierarchicalRequirementGestures.Drifted);
                    }

                    break;
                default:
                    foreach (var carrier in Carriers(document, member))
                    {
                        SetTags(edit, carrier, [.. carrier.Tags.Where(tag => !Same(tag, member))]);
                    }

                    setting = new ModelChange.Setting(TagsBlock, member, RemoveEntry: true);
                    break;
            }

            // A member the block does not hold, an agent's tag the list has not taken in, has nothing there to change.
            if (setting is not null && read.Registration?.BlockEntries(TagsBlock) is { } held && !held.Any(entry => entry.Key == member))
            {
                setting = setting.RemoveEntry || setting.Rename is not null ? null : setting;
            }
        }

        // Only an operation that changes an entry's tags writes the body, and with it the numbering every edit of it writes.
        byte[]? newBody = null;
        if (edit.Sets > 0)
        {
            (byte[]? bytes, string unwritten) = edit.Write();
            if (bytes is null) return TagEdit.Refused(unwritten);
            newBody = bytes;
        }

        if (setting is null) return new TagEdit(newBody, null, null, "");

        (byte[]? before, byte[]? afterRegistration, string refused) = RegistrationEdit.Apply(body, bodyName, registration, registrationName, block => Lists(document, block), setting);
        return afterRegistration is null ? TagEdit.Refused(refused) : new TagEdit(newBody, before, afterRegistration, "");
    }

    /// <summary>The tag list as the page shows it: its members, then the tags the entries carry that it does not hold.</summary>
    private static List<string> Members(HierarchicalRequirementDocument document)
    {
        var members = document.Tags.Select(tag => tag.Name).ToList();
        foreach (var tag in document.Entries.SelectMany(entry => entry.Tags))
        {
            if (!members.Any(member => Same(member, tag))) members.Add(tag);
        }

        return members;
    }

    /// <summary>A list's members as the document has them, by block: what a first change of an absent block writes.</summary>
    private static IReadOnlyList<KeyValuePair<string, string>> Lists(HierarchicalRequirementDocument document, string block) => block switch
    {
        TagsBlock => [.. document.Tags.Select(tag => new KeyValuePair<string, string>(tag.Name, tag.Colour))],
        "categories" => [.. document.Categories.Select(category => new KeyValuePair<string, string>(category.Name, category.Colour))],
        "states" => [.. document.States.Select(state => new KeyValuePair<string, string>(state.Name, state.Colour))],
        _ => [],
    };

    /// <summary>The items and the requirements that carry <paramref name="tag"/> in any case, items first, as the operations' forEach takes them.</summary>
    private static IEnumerable<RequirementEntry> Carriers(HierarchicalRequirementDocument document, string tag) =>
        document.Entries.OfType<Item>().Cast<RequirementEntry>().Concat(document.Entries.OfType<Requirement>()).Where(entry => entry.Tags.Any(carried => Same(carried, tag)));

    private static void SetTags(BodyEdit edit, RequirementEntry entry, List<string> tags)
    {
        if (tags.SequenceEqual(entry.Tags, StringComparer.Ordinal)) return;
        edit.Set(edit.ElementOf(entry), TagsAttribute, tags.Cast<object?>().ToList());
    }

    /// <summary>A typed name as the operations write it: <c>p.name.trim().replace(' ', '-')</c>.</summary>
    private static string Normalised(string name) => name.Trim().Replace(' ', '-');

    private static bool Same(string left, string right) => LowerAscii(left) == LowerAscii(right);

    /// <summary>CEL's <c>lowerAscii</c>, which the specification compares tags and derives their ids with.</summary>
    private static string LowerAscii(string text) => string.Concat(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));
}

/// <summary>What a tag operation writes: the body, and the registration with what it held before; null where it writes nothing.</summary>
/// <param name="Body">The body's bytes after the operation, or null when they do not change.</param>
/// <param name="RegistrationBefore">The registration as the step starts from it: as it is, or as it is created first, outside the step.</param>
/// <param name="RegistrationAfter">The registration's bytes after the operation, or null when it is not written.</param>
/// <param name="Refusal">Why nothing was written, or empty.</param>
internal sealed record TagEdit(byte[]? Body, byte[]? RegistrationBefore, byte[]? RegistrationAfter, string Refusal)
{
    public static TagEdit Refused(string refusal) => new(null, null, null, refusal);
}
