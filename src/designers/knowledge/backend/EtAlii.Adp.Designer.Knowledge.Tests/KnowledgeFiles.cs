using System.Globalization;
using System.Text;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>The module's shipped example documents, and a place to copy one to for a test that changes it.</summary>
internal static class KnowledgeFiles
{
    /// <summary>The three formats a knowledge file comes in, by extension.</summary>
    public static TheoryData<string> Extensions => [".yaml", ".json", ".xml"];

    /// <summary>
    /// A copy of a shipped example left in its second view, which filters, sorts and groups nothing: every
    /// row, in the file's order. The view the example itself is left in is whatever its file says.
    /// </summary>
    public static string CopyPlain(string name, string folder) => CopyInView(name, folder, "v2");

    /// <summary>
    /// A copy of a shipped example left in its first view, which filters, sorts and groups its rows. The view
    /// the example itself is left in is whatever its file says.
    /// </summary>
    public static string CopyGrouped(string name, string folder) => CopyInView(name, folder, "v1");

    /// <summary>A copy of a shipped example, left in <paramref name="view"/> whichever view its file names.</summary>
    private static string CopyInView(string name, string folder, string view)
    {
        var path = CopyExample(name, folder);
        var text = File.ReadAllText(path);
        var active = new System.Text.RegularExpressions.Regex("(activeView\"?[:=] ?\"?)v[0-9]+");

        // The pattern has to have found the one place the file names its view, or the copy is left as it was.
        Assert.Single(active.Matches(text));
        File.WriteAllText(path, active.Replace(text, "${1}" + view));
        return path;
    }

    /// <summary>The folder of the designer's bundled definition: its specification, its bindings and their prose.</summary>
    public static string DefinitionFolder => IoPath.GetFullPath(IoPath.Combine(ExamplesFolder, "..", "definition"));

    /// <summary>The module's examples folder.</summary>
    public static string ExamplesFolder => RepositoryPaths.Under("src", "designers", "knowledge", "examples");

    /// <summary>The repository's <c>src</c> folder.</summary>
    public static string SourceFolder => IoPath.GetFullPath(IoPath.Combine(ExamplesFolder, "..", "..", ".."));

    /// <summary>The designer's examples in the showcase project: the copies a reader opens.</summary>
    public static string ShowcaseFolder => IoPath.Combine(SourceFolder, "examples", "designers", "knowledge");

    /// <summary>The path of a shipped example.</summary>
    private static string Example(string name) => IoPath.Combine(ExamplesFolder, name);

    /// <summary>The bytes of a shipped example.</summary>
    public static byte[] ExampleBytes(string name) => File.ReadAllBytes(Example(name));

    /// <summary>The table the example's relation points at.</summary>
    public const string Related = "provinces.yaml";

    /// <summary>A copy of a shipped example in <paramref name="folder"/>, under its own name.</summary>
    public static string CopyExample(string name, string folder)
    {
        var path = IoPath.Combine(folder, name);
        File.Copy(Example(name), path);

        // The table the example's relation names goes with it, so the relation resolves as it does where the example ships.
        var related = IoPath.Combine(folder, Related);
        if (name != Related && !File.Exists(related))
        {
            File.Copy(Example(Related), related);
        }

        return path;
    }

    /// <summary>A table as text, so that two that differ say where.</summary>
    public static string Describe(KnowledgeTable table)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"table {table.Name} active={table.ActiveViewId}");
        foreach (var property in table.Properties)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"property {property.Id} {property.Name} {property.ValueType} title={property.IsTitle} target={property.TargetFile} limit={property.Limit} options=[{string.Join(", ", property.Options.Select(option => $"{option.Id}:{option.Name}:{option.Colour}"))}]");
        }

        foreach (var view in table.Views)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"view {view.Id} {view.Name} groupBy={view.GroupBy} hideEmpty={view.HideEmptyGroups} freeze={view.Freeze}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  columns {string.Join(", ", view.Columns)}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  sorts {string.Join(", ", view.Sorts)}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  filter {Describe(view.Filter)}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  order=[{string.Join(", ", view.GroupOrder)}] hidden=[{string.Join(", ", view.HiddenGroups)}] collapsed=[{string.Join(", ", view.Collapsed)}]");
        }

        foreach (var row in table.Rows)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"row {row.Id} {string.Join("; ", row.Cells.Select(cell => $"{cell.PropertyId}=[{string.Join(", ", cell.Values)}]"))}");
        }

        return text.ToString();
    }

    private static string Describe(KnowledgeFilterItem item) => item switch
    {
        KnowledgeFilterGroup group => $"{(group.Any ? "any" : "all")}({string.Join(", ", group.Items.Select(Describe))})",
        KnowledgeCondition condition => $"{condition.PropertyId} {condition.Operator} '{condition.Value}'",
        _ => "?",
    };
}
