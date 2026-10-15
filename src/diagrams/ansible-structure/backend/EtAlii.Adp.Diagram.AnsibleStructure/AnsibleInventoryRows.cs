using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The rows of an inventory the definition cannot state: one per group it defines and one per variable
/// folder it reads, after the rows its form derives.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for a language gap</b> (ansible-structure-disl-fbl, finding A9; gap 7 of the definition's
/// companion, <c>definition/ansible-structure.md</c>): a context-menu entry has <c>forEach</c> and a form
/// item has not, so one row per item of a list is written here, read from the model's <c>groups</c> and
/// <c>variableFolders</c> and nothing else. It goes when DISL gives a form item <c>forEach</c>.
/// </para>
/// <para>
/// <b>Each row is read-only with today's reason</b>: a group is defined in the inventory, a variable folder's
/// count in that folder. The section each falls in is the one its attribute declares.
/// </para>
/// </remarks>
internal static class AnsibleInventoryRows
{
    private const string GroupsAttribute = "groups";
    private const string VariableFoldersAttribute = "variableFolders";

    /// <summary>The rows of <paramref name="inventory"/>'s groups, then of its variable folders; none for an element that is not an inventory.</summary>
    public static IEnumerable<ContextPropertyDefinition> Of(DislElement inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!inventory.Type.Attributes.ContainsKey(GroupsAttribute) || !inventory.Type.Attributes.ContainsKey(VariableFoldersAttribute))
        {
            yield break;
        }

        var path = Text(inventory.ValueOf("path"));
        var groups = GroupOf(inventory, GroupsAttribute);
        foreach (var group in Items(inventory, GroupsAttribute))
        {
            var count = Count(group, "hostCount");
            yield return Row($"ansible.group.{Text(group.GetValueOrDefault("name"))}", Text(group.GetValueOrDefault("name")), count == 1 ? "1 host" : $"{count} hosts", path, groups);
        }

        var variables = GroupOf(inventory, VariableFoldersAttribute);
        foreach (var folder in Items(inventory, VariableFoldersAttribute))
        {
            var count = Count(folder, "fileCount");
            yield return Row($"ansible.vars.{Text(folder.GetValueOrDefault("name"))}", Text(folder.GetValueOrDefault("name")), count == 1 ? "1 file" : $"{count} files", Text(folder.GetValueOrDefault("path")), variables);
        }
    }

    private static IEnumerable<CelMap> Items(DislElement element, string attribute) =>
        element.ValueOf(attribute) is IReadOnlyList<object?> items ? items.OfType<CelMap>() : [];

    /// <summary>The section the attribute declares its rows in.</summary>
    private static string GroupOf(DislElement element, string attribute) =>
        element.Type.Attributes[attribute].Json.TryGetProperty("group", out var group) && group.ValueKind == JsonValueKind.String ? group.GetString()! : "";

    private static long Count(CelMap item, string field) => item.GetValueOrDefault(field) is long count ? count : 0;

    private static string Text(object? value) => value as string ?? "";

    private static ContextPropertyDefinition Row(string id, string label, string value, string source, string group) =>
        new(id, label, value, ContextPropertyEditor.Line, $"Defined in {source}; edit it in a text editor.", group);
}
