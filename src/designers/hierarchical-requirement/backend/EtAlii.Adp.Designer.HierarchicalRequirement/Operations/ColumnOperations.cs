using System.Globalization;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The operations on the widths of the columns of items of <c>hierarchical-requirement.des</c> (DESL 0.2 section 7.7.1):
/// <c>addColumnWidth</c> gives a column its first width, <c>setColumnWidth</c> changes it, and <c>resetColumnWidth</c>
/// gives it the host's width again. They write the registration's <c>widths</c> block alone, as one step of the history,
/// the gesture being the end of a drag of the column's right edge.
/// </summary>
internal static class ColumnOperations
{
    public const string AddColumnWidth = "addColumnWidth";
    public const string SetColumnWidth = "setColumnWidth";
    public const string ResetColumnWidth = "resetColumnWidth";

    /// <summary>The narrowest and the widest a column may be set, as the specification's Column.width says.</summary>
    public const int MinimumWidth = 120;
    public const int MaximumWidth = 2000;

    /// <summary>The prefix of a column's id, <c>'column/' + self.level</c> (hierarchical-requirement.des, ids).</summary>
    public const string IdPrefix = "column/";

    private const string WidthsBlock = "widths";

    /// <summary>Whether <paramref name="operation"/> is one of these.</summary>
    public static bool Handles(string operation) => operation is AddColumnWidth or SetColumnWidth or ResetColumnWidth;

    /// <summary>Whether <paramref name="operation"/> targets a stored width rather than the document.</summary>
    public static bool TargetsAColumn(string operation) => operation is SetColumnWidth or ResetColumnWidth;

    /// <summary>Whether <paramref name="parameters"/> can be written by <paramref name="operation"/>: the sentence saying why not, or empty.</summary>
    public static string Refusal(string operation, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (operation is not (AddColumnWidth or SetColumnWidth)) return "";
        if (operation == AddColumnWidth && (!parameters.TryGetValue("level", out var level) || Number(level) < 1)) return "A column is named by its depth, a whole number from 1.";
        var width = parameters.TryGetValue("width", out var typed) ? Number(typed) : 0;
        return width is >= MinimumWidth and <= MaximumWidth ? "" : $"A column is {MinimumWidth} to {MaximumWidth} pixels wide.";
    }

    /// <summary>
    /// The registration before and after <paramref name="operation"/>; or the sentence saying why it cannot be written, in
    /// which case nothing changed. The body is never written. A width the registration no longer holds is refused as a
    /// change made against an older file, and so is a first width for a column that has one.
    /// </summary>
    /// <param name="registration">The registration's bytes, or null when the body has none yet.</param>
    public static ListEdit Apply(byte[] body, string bodyName, byte[]? registration, string registrationName, string operation, string targetId, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);
        if (Refusal(operation, parameters) is { Length: > 0 } refusal) return ListEdit.Refused(refusal);

        var document = HierarchicalRequirementBody.Read(body, bodyName, registration, registrationName).Document;
        ModelChange.Setting setting;
        if (operation == AddColumnWidth)
        {
            var level = Number(parameters["level"]);
            if (document.Columns.Any(column => column.Level == level)) return ListEdit.Refused(HierarchicalRequirementGestures.Drifted);
            setting = new ModelChange.Setting(WidthsBlock, Text(level), Text(Number(parameters["width"])));
        }
        else
        {
            if (document.Columns.FirstOrDefault(column => IdPrefix + Text(column.Level) == targetId) is not { } stored) return ListEdit.Refused(HierarchicalRequirementGestures.Drifted);
            setting = operation == SetColumnWidth
                ? new ModelChange.Setting(WidthsBlock, Text(stored.Level), Text(Number(parameters["width"])))
                : new ModelChange.Setting(WidthsBlock, Text(stored.Level), RemoveEntry: true);
        }

        (byte[]? before, byte[]? after, string refused) = RegistrationEdit.Apply(body, bodyName, registration, registrationName, block => RegistrationEdit.Members(document, block), setting);
        return after is null ? ListEdit.Refused(refused) : new ListEdit(null, before, after, "");
    }

    /// <summary>The whole number <paramref name="text"/> is, digits only, or 0.</summary>
    private static int Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static string Text(int number) => number.ToString(CultureInfo.InvariantCulture);
}
