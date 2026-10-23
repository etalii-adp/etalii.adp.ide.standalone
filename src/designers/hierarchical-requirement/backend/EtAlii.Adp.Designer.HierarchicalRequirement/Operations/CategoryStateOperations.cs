using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The operations on categories and states of <c>hierarchical-requirement.des</c> (FR-026, FR-030, FR-031): picking one
/// on a requirement or clearing it, and adding, renaming, recolouring, moving and deleting a member of either list. A
/// requirement's category and state are written in the Markdown file, the lists' colours and order in the registration's
/// <c>categories</c> and <c>states</c> blocks; an operation that changes both writes both in one step (spec 029 R17),
/// byte for byte as the conformance fixtures <c>hierarchical-requirement-category-state</c> and its <c>list</c> state.
/// </summary>
internal static class CategoryStateOperations
{
    /// <summary>The two lists: the operations' names end in the type's, and its members' ids begin with its prefix.</summary>
    private static readonly ClassificationList[] Lists =
    [
        new("Category", "category", "categories", "category/"),
        new("State", "state", "states", "state/"),
    ];

    /// <summary>Whether <paramref name="operation"/> is one of these.</summary>
    public static bool Handles(string operation) => ListOf(operation) is not null;

    /// <summary>Whether <paramref name="operation"/> targets the document rather than a requirement or a member.</summary>
    public static bool TargetsTheDocument(string operation) => ListOf(operation) is { } list && operation == "add" + list.Type;

    /// <summary>Whether <paramref name="operation"/> targets a member of a list rather than a requirement.</summary>
    public static bool TargetsAMember(string operation) => ListOf(operation) is { } list && operation[..^list.Type.Length] is "rename" or "recolour" or "move" or "delete";

    /// <summary>
    /// Whether <paramref name="parameters"/> can be written by <paramref name="operation"/>: the sentence saying why not, or
    /// empty. A new name is trimmed and a space in it written <c>-</c>, as the operations do; what is left is one word.
    /// </summary>
    public static string Refusal(string operation, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (ListOf(operation) is not { } list) return "";
        var noun = list.Attribute;
        if (operation == "add" + list.Type || operation == "rename" + list.Type)
        {
            if (!parameters.TryGetValue("name", out var name) || Normalised(name).Length == 0) return $"A {noun} needs a name.";
            if (Normalised(name).Any(char.IsWhiteSpace)) return $"'{name.Trim()}' cannot be a {noun}: a {noun}'s name is one word.";
        }

        if (operation == "set" + list.Type && (!parameters.TryGetValue(list.Attribute, out var picked) || picked.Trim().Length == 0)) return $"Pick a {noun}.";
        if (operation == "add" + list.Type || operation == "recolour" + list.Type)
        {
            var colour = parameters.GetValueOrDefault("colour", operation == "add" + list.Type ? "default" : "");
            if (!HierarchicalRequirementVocabulary.Enumerations["Colour"].Contains(colour)) return $"'{colour}' is not a colour.";
        }

        return "";
    }

    /// <summary>
    /// The files after <paramref name="operation"/> on <paramref name="targetId"/>, the surface's id of a requirement or a
    /// member's id: the body's bytes and the registration's before and after, each null when the operation does not write
    /// it; or the sentence saying why it cannot be, in which case nothing changed. A target the files no longer have is
    /// refused as a change made against an older file.
    /// </summary>
    /// <param name="registration">The registration's bytes, or null when the body has none yet.</param>
    public static ListEdit Apply(byte[] body, string bodyName, byte[]? registration, string registrationName, string operation, string targetId, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);
        if (ListOf(operation) is not { } list) return ListEdit.Refused($"'{operation}' cannot be done here.");
        if (Refusal(operation, parameters) is { Length: > 0 } refusal) return ListEdit.Refused(refusal);

        var read = HierarchicalRequirementBody.Read(body, bodyName, registration, registrationName);
        var document = read.Document;
        if (document.ReadOnlyReason.Length > 0) return ListEdit.Refused(document.ReadOnlyReason);
        var members = Members(document, list);
        var edit = new BodyEdit(read);
        ModelChange.Setting? setting = null;
        var verb = operation[..^list.Type.Length];

        if (verb is "set" or "clear")
        {
            var ids = HierarchicalRequirementSurfaceMapper.NodeIds(document.Entries);
            if (document.Entries.OfType<Requirement>().FirstOrDefault(candidate => ids[candidate] == targetId) is not { } requirement) return ListEdit.Refused(HierarchicalRequirementGestures.Drifted);
            if (verb == "clear")
            {
                if (ValueOf(requirement, list) is not null) edit.Set(edit.ElementOf(requirement), list.Attribute, null);
            }
            else
            {
                // A name equal to a member's in any case picks it, written with the list's spelling (DESL 0.2 section 7.7.2).
                var picked = parameters[list.Attribute].Trim();
                if (members.FirstOrDefault(candidate => Same(candidate, picked) || IdOf(list, candidate) == picked) is not { } member) return ListEdit.Refused($"'{picked}' is not a {list.Attribute} of this document.");
                if (ValueOf(requirement, list) != member) edit.Set(edit.ElementOf(requirement), list.Attribute, member);
            }
        }
        else if (verb == "add")
        {
            var name = Normalised(parameters["name"]);
            if (members.Any(member => Same(member, name))) return ListEdit.Refused($"There is already a {list.Attribute} named {name}.");
            setting = new ModelChange.Setting(list.Block, name, parameters.GetValueOrDefault("colour", "default"));
        }
        else
        {
            if (members.FirstOrDefault(candidate => IdOf(list, candidate) == targetId) is not { } member) return ListEdit.Refused(HierarchicalRequirementGestures.Drifted);
            switch (verb)
            {
                case "rename":
                    var name = Normalised(parameters["name"]);
                    if (members.Any(other => other != member && Same(other, name))) return ListEdit.Refused($"There is already a {list.Attribute} named {name}.");
                    foreach (var carrier in Carriers(document, list, member))
                    {
                        edit.Set(edit.ElementOf(carrier), list.Attribute, name);
                    }

                    setting = name == member ? null : new ModelChange.Setting(list.Block, member, Rename: name);
                    break;
                case "recolour":
                    setting = new ModelChange.Setting(list.Block, member, parameters["colour"]);
                    break;
                case "move":
                    if (!parameters.TryGetValue("after", out var after) || after.Length == 0)
                    {
                        setting = Same(members[0], member) ? null : new ModelChange.Setting(list.Block, member, Position: new SettingPosition(Before: members[0]));
                    }
                    else if (members.FirstOrDefault(other => IdOf(list, other) == after) is { } previous)
                    {
                        setting = Same(previous, member) ? null : new ModelChange.Setting(list.Block, member, Position: new SettingPosition(After: previous));
                    }
                    else
                    {
                        return ListEdit.Refused(HierarchicalRequirementGestures.Drifted);
                    }

                    break;
                default:
                    foreach (var carrier in Carriers(document, list, member))
                    {
                        edit.Set(edit.ElementOf(carrier), list.Attribute, null);
                    }

                    setting = new ModelChange.Setting(list.Block, member, RemoveEntry: true);
                    break;
            }

            // A member the list holds only because a requirement names it has no entry to rename or delete while the block
            // is written; with no block, the first change writes the whole list, and it is among it.
            if (setting is not null && read.Registration?.BlockEntries(list.Block) is { } held && held.All(entry => entry.Key != member))
            {
                setting = setting.RemoveEntry || setting.Rename is not null ? null : setting;
            }
        }

        // Only an operation that changes a requirement writes the body, and with it the numbering every edit of it writes.
        byte[]? newBody = null;
        if (edit.Sets > 0)
        {
            (byte[]? bytes, string unwritten) = edit.Write();
            if (bytes is null) return ListEdit.Refused(unwritten);
            newBody = bytes;
        }

        if (setting is null) return new ListEdit(newBody, null, null, "");

        (byte[]? before, byte[]? afterRegistration, string refused) = RegistrationEdit.Apply(body, bodyName, registration, registrationName, block => RegistrationEdit.Members(document, block), setting);
        return afterRegistration is null ? ListEdit.Refused(refused) : new ListEdit(newBody, before, afterRegistration, "");
    }

    private static ClassificationList? ListOf(string operation) =>
        Lists.FirstOrDefault(list => operation.EndsWith(list.Type, StringComparison.Ordinal) && operation[..^list.Type.Length] is "set" or "clear" or "add" or "rename" or "recolour" or "move" or "delete");

    /// <summary>A list as the page shows it: its members, then the names the requirements carry that it does not hold.</summary>
    private static List<string> Members(HierarchicalRequirementDocument document, ClassificationList list)
    {
        var members = (list.Block == "categories" ? document.Categories.Select(category => category.Name) : document.States.Select(state => state.Name)).ToList();
        foreach (var name in document.Entries.OfType<Requirement>().Select(requirement => ValueOf(requirement, list)).OfType<string>())
        {
            if (!members.Any(member => Same(member, name))) members.Add(name);
        }

        return members;
    }

    /// <summary>The requirements that carry <paramref name="member"/> in any case, in the file's order, as the operations' forEach takes them.</summary>
    private static IEnumerable<Requirement> Carriers(HierarchicalRequirementDocument document, ClassificationList list, string member) =>
        document.Entries.OfType<Requirement>().Where(requirement => ValueOf(requirement, list) is { } value && Same(value, member));

    private static string? ValueOf(Requirement requirement, ClassificationList list) => list.Block == "categories" ? requirement.Category : requirement.State;

    private static string IdOf(ClassificationList list, string member) => list.IdPrefix + LowerAscii(member);

    /// <summary>A typed name as the operations write it: <c>p.name.trim().replace(' ', '-')</c>.</summary>
    private static string Normalised(string name) => name.Trim().Replace(' ', '-');

    private static bool Same(string left, string right) => LowerAscii(left) == LowerAscii(right);

    /// <summary>CEL's <c>lowerAscii</c>, which the specification compares names and derives ids with.</summary>
    private static string LowerAscii(string text) => string.Concat(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));

    /// <param name="Type">The metamodel's type, which ends each operation's name: <c>setCategory</c>, <c>moveState</c>.</param>
    /// <param name="Attribute">The requirement's attribute, which is also the param a pick names.</param>
    /// <param name="Block">The registration's block that keeps the list.</param>
    /// <param name="IdPrefix">What a member's id begins with, before its name in lower case.</param>
    private sealed record ClassificationList(string Type, string Attribute, string Block, string IdPrefix);
}
