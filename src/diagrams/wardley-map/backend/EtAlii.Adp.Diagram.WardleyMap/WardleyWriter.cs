using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Rewrites the statements an edit touches, and nothing else (Requirement 3.2).
/// </summary>
/// <remarks>
/// <para>
/// Every method here <b>splices a span inside one line</b> rather than rebuilding the line from
/// the model. That is the difference between an edit and a reformat: a line rebuilt from
/// `component Name [v, m]` loses the trailing comment, the decorators, the label offset and
/// whatever spacing its author chose, all of which the model either does not carry or does not
/// carry positionally.
/// </para>
/// <para>
/// Names are matched by <b>span</b>, never by substring replacement. Renaming `Tea` with a
/// naive replace would corrupt `Cup of Tea`, and in this notation names containing other names
/// are the norm rather than an edge case.
/// </para>
/// </remarks>
public static partial class WardleyWriter
{
    /// <summary>
    /// Moves one element by rewriting its coordinate pair in place. Returns false when the line
    /// no longer looks like the statement the model described, which is possible if the
    /// document changed underneath - the caller then reports a rejection rather than writing
    /// something wrong (Requirement 9.4).
    /// </summary>
    public static bool SetPosition(WardleyDocument document, WardleyComponent component, WardleyCoordinate position)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(position);

        return SetCoordinatePair(document, component.Line, position);
    }

    /// <summary>
    /// Moves a pipeline child, which carries an evolution position only - its visibility is the
    /// parent's, and there is nowhere in the format to write one of its own (Requirement 7.4).
    /// </summary>
    public static bool SetPipelineChildMaturity(WardleyDocument document, WardleyPipelineChild child, double maturity)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(child);

        var line = LineAt(document, child.Line);
        if (line is null)
        {
            return false;
        }

        var match = SingleValueExpression().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var value = match.Groups["value"];
        document.ReplaceLine(child.Line, Splice(line, value.Index, value.Length, Number(maturity)));
        return true;
    }

    /// <summary>
    /// Moves a legacy-form pipeline, whose two coordinates sit on the `pipeline` line itself
    /// (Requirement 5.4).
    /// </summary>
    public static bool SetPipelineExtent(WardleyDocument document, WardleyPipeline pipeline, WardleyCoordinate extent)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(extent);

        return pipeline.Form == WardleyPipelineForm.Legacy && SetCoordinatePair(document, pipeline.Line, extent);
    }

    /// <summary>
    /// Renames an element and every statement that refers to it by name - links, `evolve`,
    /// pipeline parents and pipeline children - in one pass (Requirement 4.4).
    /// </summary>
    /// <returns>How many lines were rewritten, so a caller can assert the whole rename landed.</returns>
    /// <remarks>
    /// One pass matters for undo: Requirement 9.6 says an undone rename restores every statement
    /// the rename touched, which only holds if they moved together in the first place.
    /// </remarks>
    public static int Rename(WardleyDocument document, WardleyMap map, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        if (oldName == newName)
        {
            return 0;
        }

        var changed = 0;

        foreach (var component in map.Components.Where(candidate => candidate.Name == oldName))
        {
            changed += RenameIn(document, component.Line, DeclarationNameExpression(), "name", newName) ? 1 : 0;
        }

        foreach (var link in map.Links.Where(candidate => candidate.Source == oldName || candidate.Target == oldName))
        {
            changed += RenameLink(document, link, oldName, newName) ? 1 : 0;
        }

        foreach (var evolve in map.Evolves.Where(candidate => candidate.Name == oldName))
        {
            changed += RenameIn(document, evolve.Line, EvolveNameExpression(), "name", newName) ? 1 : 0;
        }

        foreach (var pipeline in map.Pipelines.Where(candidate => candidate.Parent == oldName))
        {
            changed += RenameIn(document, pipeline.Line, PipelineParentExpression(), "name", newName) ? 1 : 0;
        }

        foreach (var child in map.Pipelines.SelectMany(pipeline => pipeline.Children).Where(candidate => candidate.Name == oldName))
        {
            changed += RenameIn(document, child.Line, DeclarationNameExpression(), "name", newName) ? 1 : 0;
        }

        return changed;
    }

    private static bool SetCoordinatePair(WardleyDocument document, uint number, WardleyCoordinate position)
    {
        var line = LineAt(document, number);
        if (line is null)
        {
            return false;
        }

        var match = PairExpression().Match(line);
        if (!match.Success)
        {
            return false;
        }

        // The two numbers are replaced individually so the separator between them - whatever
        // spacing the author used - survives untouched.
        var first = match.Groups["first"];
        var second = match.Groups["second"];
        var updated = Splice(line, second.Index, second.Length, Number(position.Maturity));
        updated = Splice(updated, first.Index, first.Length, Number(position.Visibility));

        document.ReplaceLine(number, updated);
        return true;
    }

    private static bool RenameLink(WardleyDocument document, WardleyLink link, string oldName, string newName)
    {
        var line = LineAt(document, link.Line);
        if (line is null)
        {
            return false;
        }

        var match = LinkPartsExpression().Match(line);
        if (!match.Success)
        {
            return false;
        }

        // Both ends first, right to left, so replacing the source cannot shift the target's
        // span out from under the second edit. A link from something to itself is legal.
        var updated = line;
        if (link.Target == oldName)
        {
            var target = match.Groups["target"];
            updated = Splice(updated, target.Index, target.Length, newName);
        }

        if (link.Source == oldName)
        {
            var source = match.Groups["source"];
            updated = Splice(updated, source.Index, source.Length, newName);
        }

        if (updated == line)
        {
            return false;
        }

        document.ReplaceLine(link.Line, updated);
        return true;
    }

    private static bool RenameIn(WardleyDocument document, uint number, Regex expression, string group, string newName)
    {
        var line = LineAt(document, number);
        if (line is null)
        {
            return false;
        }

        var match = expression.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var captured = match.Groups[group];
        document.ReplaceLine(number, Splice(line, captured.Index, captured.Length, newName));
        return true;
    }

    private static string? LineAt(WardleyDocument document, uint number) =>
        number >= 1 && number <= document.Lines.Count ? document.Lines[(int)number - 1] : null;

    private static string Splice(string line, int index, int length, string replacement) =>
        string.Concat(line.AsSpan(0, index), replacement, line.AsSpan(index + length));

    /// <summary>
    /// A number as the DSL writes it: invariant, and without a trailing `.0` that would appear
    /// as noise in a diff of a file the author wrote by hand.
    /// </summary>
    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The two numbers of a coordinate pair, captured separately so the separator survives.</summary>
    [GeneratedRegex(@"\[\s*(?<first>-?[\d.]+)\s*,\s*(?<second>-?[\d.]+)\s*\]")]
    private static partial Regex PairExpression();

    /// <summary>The single number a pipeline child carries.</summary>
    [GeneratedRegex(@"\[\s*(?<value>-?[\d.]+)\s*\]")]
    private static partial Regex SingleValueExpression();

    /// <summary>The name of a `component`, `anchor` or `submap`, up to its coordinates.</summary>
    [GeneratedRegex(@"^\s*(?:component|anchor|submap)\s+(?<name>.*?)(?=\s*\[)", RegexOptions.IgnoreCase)]
    private static partial Regex DeclarationNameExpression();

    /// <summary>The component an `evolve` names, before any `-&gt;` rename or trailing maturity.</summary>
    [GeneratedRegex(@"^\s*evolve\s+(?<name>.*?)(?=\s*(?:->|-?[\d.]+\s*$))", RegexOptions.IgnoreCase)]
    private static partial Regex EvolveNameExpression();

    /// <summary>The parent a `pipeline` names, in either form.</summary>
    [GeneratedRegex(@"^\s*pipeline\s+(?<name>.*?)(?=\s*(?:\[|\{|$))", RegexOptions.IgnoreCase)]
    private static partial Regex PipelineParentExpression();

    /// <summary>A link's two endpoints, captured separately for span-precise renaming.</summary>
    [GeneratedRegex(@"^(?<source>[^-+;]+?)\s*(?:->|\+>)\s*(?<target>[^;/]+?)\s*(?=;|//|$)")]
    private static partial Regex LinkPartsExpression();
}
