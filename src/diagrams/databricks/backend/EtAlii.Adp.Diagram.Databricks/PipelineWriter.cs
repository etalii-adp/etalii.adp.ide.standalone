using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The named splice operations on a pipeline: libraries in and out, scalars rewritten - in both
/// of the family's syntaxes, because a pipeline is declared in block YAML inside a bundle and in
/// flow JSON as a settings file, and only the lines an edit concerns may change in either
/// (databricks-diagrams Requirement 2.2).
/// </summary>
/// <remarks>
/// The JSON operations assume the layout the settings file conventionally has - one array item
/// per line - because a splice writer's contract is the file's own shape, not a reformatter's.
/// Every operation answers with an empty string, or the sentence explaining why it refused -
/// before any splice, never write-then-repair (Requirement 6.4).
/// </remarks>
internal static class PipelineWriter
{
    /// <summary>Appends a library entry after the pipeline's last one.</summary>
    public static string InsertLibrary(
        DatabricksDocument document, PipelineModel pipeline, string kind, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pipeline);

        if (kind is not ("notebook" or "file" or "glob"))
        {
            return $"There is no '{kind}' library kind to add.";
        }

        if (path.Length == 0)
        {
            return "A library needs a path.";
        }

        if (pipeline.Libraries.Any(library => library.Kind == kind && library.Path == path))
        {
            return $"The {kind} library '{path}' is already there.";
        }

        var pathKey = kind == "glob" ? "include" : "path";
        if (DatabricksSplices.IsJson(document))
        {
            if (pipeline.Libraries.Count == 0)
            {
                return "The settings file has no libraries array to add into.";
            }

            // The previous last item gains the comma that now separates it from the new one.
            var last = pipeline.Libraries[^1].Lines;
            var lastText = document.Lines[last.End].Text;
            var indent = DatabricksSplices.Indent(document.Lines[last.Start].Text);
            document.Replace(new LineRange(last.End, last.End), [lastText.TrimEnd() + ","]);
            document.Insert(last.End + 1, [$"{indent}{{ \"{kind}\": {{ \"{pathKey}\": \"{path}\" }} }}"]);
            return "";
        }

        string[] Entry(string entryIndent) =>
        [
            $"{entryIndent}- {kind}:",
            $"{entryIndent}    {pathKey}: {DatabricksSplices.Quote(path)}",
        ];

        if (pipeline.Libraries.Count > 0)
        {
            var last = pipeline.Libraries[^1];
            document.Insert(last.Lines.End + 1, Entry(DatabricksSplices.Indent(document.Lines[last.Lines.Start].Text)));
            return "";
        }

        var librariesLine = DatabricksSplices.FindKey(document, pipeline.Lines, "libraries");
        if (librariesLine >= 0)
        {
            document.Insert(librariesLine + 1, Entry(DatabricksSplices.Indent(document.Lines[librariesLine].Text) + "  "));
            return "";
        }

        var keyIndent = DatabricksSplices.KeyIndentWithin(document, pipeline.Lines);
        document.Insert(pipeline.Lines.End + 1, [$"{keyIndent}libraries:", .. Entry(keyIndent + "  ")]);
        return "";
    }

    /// <summary>
    /// Removes one library entry. The last one is refused rather than removed: a pipeline with
    /// no libraries has nothing to run, and the published schema requires at least one.
    /// </summary>
    public static string RemoveLibrary(
        DatabricksDocument document, PipelineModel pipeline, string kind, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pipeline);

        var library = pipeline.Libraries.FirstOrDefault(candidate => candidate.Kind == kind && candidate.Path == path);
        if (library is null)
        {
            return $"There is no {kind} library '{path}' any more.";
        }

        if (pipeline.Libraries.Count == 1)
        {
            return "A pipeline needs at least one library; removing the last one is not allowed.";
        }

        var wasLast = library == pipeline.Libraries[^1];
        document.Remove(library.Lines);

        if (wasLast && DatabricksSplices.IsJson(document))
        {
            // The item before it is the new last, and JSON forbids its trailing comma.
            var previous = pipeline.Libraries[^2].Lines;
            var text = document.Lines[previous.End].Text.TrimEnd();
            if (text.EndsWith(','))
            {
                document.Replace(new LineRange(previous.End, previous.End), [text[..^1]]);
            }
        }

        return "";
    }

    /// <summary>
    /// Rewrites one of the pipeline's own scalars - name, catalog, schema, channel - keeping the
    /// file's syntax: quoting and the trailing comma in JSON, the value style in YAML.
    /// </summary>
    public static string SetScalar(
        DatabricksDocument document, PipelineModel pipeline, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pipeline);

        if (key == "name" && value.Length == 0)
        {
            return "A pipeline needs a name.";
        }

        var line = DatabricksSplices.FindKey(document, pipeline.Lines, key);
        if (line < 0)
        {
            var keyIndent = DatabricksSplices.KeyIndentWithin(document, pipeline.Lines);
            if (DatabricksSplices.IsJson(document))
            {
                return $"The settings file has no \"{key}\" to rewrite.";
            }

            document.Insert(pipeline.Lines.Start + 1, [$"{keyIndent}{key}: {DatabricksSplices.Quote(value)}"]);
            return "";
        }

        var text = document.Lines[line].Text;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (DatabricksSplices.IsJson(document))
        {
            var hadComma = text.TrimEnd().EndsWith(',');
            var quoted = $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
            document.Replace(new LineRange(line, line), [$"{text[..(colon + 1)]} {quoted}{(hadComma ? "," : "")}"]);
            return "";
        }

        document.Replace(new LineRange(line, line), [$"{text[..(colon + 1)]} {DatabricksSplices.Quote(value)}"]);
        return "";
    }
}
