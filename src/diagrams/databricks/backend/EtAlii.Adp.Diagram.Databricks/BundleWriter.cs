using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Yaml;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The one edit not yet planned through the binding: a new resource's skeleton under <c>resources:</c>, spliced as lines
/// (databricks-diagrams Requirement 2.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in, and the last module code that writes a line of YAML</b> (databricks-disl-fbl task 12, Requirements
/// 6.1 and 7.2). FBL's YAML family cannot add an entry to a mapping keyed by a new name, such as
/// <c>/resources/jobs/&lt;key&gt;</c>: the library refuses with "The /resources/jobs of this file is not a list a Resource
/// can be added to.", and FBL 0.12 defines no such insert. TODO(fork): the insert is being specified in etalii.adp
/// (FBL 0.13, recommended on Peter's card); once it is, <see cref="AddDatabricksResourceCommandHandler"/> plans the add
/// through the binding and this class leaves the product.
/// </para>
/// <para>Every refusal is answered before any splice, never write-then-repair (Requirement 6.4).</para>
/// </remarks>
internal static class BundleWriter
{
    /// <summary>
    /// Adds a new resource of <paramref name="kind"/> under <c>resources:</c>, as a skeleton
    /// complete enough to open - a job arrives with one starter task, a pipeline with one
    /// starter library, because the schema requires each and an invalid skeleton would be a
    /// refusal factory.
    /// </summary>
    public static string AddResourceSkeleton(
        LineDocument document, BundleModel bundle, string kind, string key)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);

        if (key.Length == 0)
        {
            return "A resource needs a key.";
        }

        if (kind is not ("jobs" or "pipelines"))
        {
            return $"There is no '{kind}' resource kind to add.";
        }

        if (bundle.Resources.Any(resource => resource.Kind == kind && resource.Key == key))
        {
            return $"A {kind} resource named '{key}' is already there.";
        }

        var siblings = bundle.Resources.Where(resource => resource.Kind == kind).ToList();
        if (siblings.Count > 0)
        {
            var last = siblings[^1];
            var keyIndent = Indent(document.Lines[last.Lines.Start].Text);
            document.Insert(last.Lines.End + 1, Skeleton(kind, key, keyIndent));
            return "";
        }

        var resourcesLine = RootKeyLine(document, "resources");
        if (resourcesLine >= 0)
        {
            var kindIndent = IndentAfter(document, resourcesLine);
            document.Insert(resourcesLine + 1, [$"{kindIndent}{kind}:", .. Skeleton(kind, key, kindIndent + "  ")]);
            return "";
        }

        // No resources: at all - the section is created at the end of the document along with
        // its first kind and entry.
        document.Insert(document.Lines.Count, ["resources:", $"  {kind}:", .. Skeleton(kind, key, "    ")]);
        return "";
    }

    /// <summary>The lines of a new resource, each value written so that it reads back as the key it was made from (finding B9).</summary>
    private static List<string> Skeleton(string kind, string key, string keyIndent) =>
        kind == "jobs"
            ?
            [
                $"{keyIndent}{Quote(key)}:",
                $"{keyIndent}  name: {Quote(key)}",
                $"{keyIndent}  tasks:",
                $"{keyIndent}    - task_key: main",
                $"{keyIndent}      notebook_task:",
                $"{keyIndent}        notebook_path: {Quote($"notebooks/{key}")}",
            ]
            :
            [
                $"{keyIndent}{Quote(key)}:",
                $"{keyIndent}  name: {Quote(key)}",
                $"{keyIndent}  libraries:",
                $"{keyIndent}    - notebook:",
                $"{keyIndent}        path: {Quote($"transformations/{key}")}",
            ];

    /// <summary>
    /// A value plain only where it reads back as the same text, FBL 6.3's plain-safe rule, and double-quoted otherwise
    /// (finding B9); a <c>:</c> or a <c>#</c> anywhere, or a leading indicator, is quoted as the writers quoted it.
    /// </summary>
    private static string Quote(string value)
    {
        var needsQuoting =
            value.Length == 0 ||
            value.Contains(':', StringComparison.Ordinal) ||
            value.Contains('#', StringComparison.Ordinal) ||
            value[0] is '-' or ' ' or '"' or '\'' or '{' or '[' ||
            value.EndsWith(' ') ||
            !YamlScalars.IsPlainSafe(value, false);

        return needsQuoting ? YamlScalars.DoubleQuoted(value) : value;
    }

    /// <summary>The leading whitespace of a line's text.</summary>
    private static string Indent(string text) => text[..^text.TrimStart().Length];

    /// <summary>The indentation of the first line after <paramref name="keyLine"/> that holds anything, so a file written with four spaces stays written with four spaces.</summary>
    private static string IndentAfter(LineDocument document, int keyLine)
    {
        for (var i = keyLine + 1; i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return Indent(text);
            }
        }

        return Indent(document.Lines[keyLine].Text) + "  ";
    }

    /// <summary>
    /// The line declaring a root-level key - at column zero, so a target's own nested
    /// <c>resources:</c> is never mistaken for the document's.
    /// </summary>
    private static int RootKeyLine(LineDocument document, string key)
    {
        for (var i = 0; i < document.Lines.Count; i++)
        {
            if (document.Lines[i].Text.StartsWith($"{key}:", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
