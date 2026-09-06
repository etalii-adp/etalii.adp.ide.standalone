using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The named splice operations on a <c>databricks.yml</c>: resource skeletons in, scalars
/// rewritten - only the lines an edit concerns change (databricks-diagrams Requirement 2.2).
/// </summary>
/// <remarks>
/// Every operation answers with an empty string, or the sentence explaining why it refused -
/// before any splice, never write-then-repair (Requirement 6.4).
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
        DatabricksDocument document, BundleModel bundle, string kind, string key)
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
            var keyIndent = DatabricksSplices.Indent(document.Lines[last.Lines.Start].Text);
            document.Insert(last.Lines.End + 1, Skeleton(kind, key, keyIndent));
            return "";
        }

        var resourcesLine = RootKeyLine(document, "resources");
        if (resourcesLine >= 0)
        {
            var kindIndent = DatabricksSplices.KeyIndentWithin(
                document, new LineRange(resourcesLine, document.Lines.Count - 1));
            document.Insert(resourcesLine + 1, [$"{kindIndent}{kind}:", .. Skeleton(kind, key, kindIndent + "  ")]);
            return "";
        }

        // No resources: at all - the section is created at the end of the document along with
        // its first kind and entry.
        document.Insert(document.Lines.Count, ["resources:", $"  {kind}:", .. Skeleton(kind, key, "    ")]);
        return "";
    }

    /// <summary>Rewrites the bundle's name.</summary>
    public static string SetName(DatabricksDocument document, BundleModel bundle, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);

        if (name.Length == 0)
        {
            return "A bundle needs a name.";
        }

        var bundleLine = RootKeyLine(document, "bundle");
        if (bundleLine < 0)
        {
            return "The file has no bundle: section to name.";
        }

        var region = new LineRange(bundleLine, document.Lines.Count - 1);
        var nameLine = DatabricksSplices.FindKey(
            document, new LineRange(bundleLine + 1, RegionEnd(document, bundleLine)), "name");
        if (nameLine >= 0)
        {
            var text = document.Lines[nameLine].Text;
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            document.Replace(new LineRange(nameLine, nameLine), [$"{text[..(colon + 1)]} {DatabricksSplices.Quote(name)}"]);
            return "";
        }

        var keyIndent = DatabricksSplices.KeyIndentWithin(document, region);
        document.Insert(bundleLine + 1, [$"{keyIndent}name: {DatabricksSplices.Quote(name)}"]);
        return "";
    }

    private static List<string> Skeleton(string kind, string key, string keyIndent) =>
        kind == "jobs"
            ?
            [
                $"{keyIndent}{key}:",
                $"{keyIndent}  name: {key}",
                $"{keyIndent}  tasks:",
                $"{keyIndent}    - task_key: main",
                $"{keyIndent}      notebook_task:",
                $"{keyIndent}        notebook_path: notebooks/{key}",
            ]
            :
            [
                $"{keyIndent}{key}:",
                $"{keyIndent}  name: {key}",
                $"{keyIndent}  libraries:",
                $"{keyIndent}    - notebook:",
                $"{keyIndent}        path: transformations/{key}",
            ];

    /// <summary>
    /// The line declaring a root-level key - at column zero, so a target's own nested
    /// <c>resources:</c> is never mistaken for the document's.
    /// </summary>
    private static int RootKeyLine(DatabricksDocument document, string key)
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

    /// <summary>The last line still belonging to the root key starting at <paramref name="keyLine"/>: everything up to the next column-zero key.</summary>
    private static int RegionEnd(DatabricksDocument document, int keyLine)
    {
        for (var i = keyLine + 1; i < document.Lines.Count; i++)
        {
            var text = document.Lines[i].Text;
            if (text.Length > 0 && !char.IsWhiteSpace(text[0]) && !text.StartsWith('#'))
            {
                return i - 1;
            }
        }

        return document.Lines.Count - 1;
    }
}
