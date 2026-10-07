using System.Globalization;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What is wrong with a map, as a pure function from a parsed model to problems
/// (Requirement 14.9).
/// </summary>
/// <remarks>
/// <para>
/// No file, no canvas, no connection: given a <see cref="WardleyMap"/> it returns a list. That
/// is what lets every rule be tested from a plain `.owm` string, and it is the shape
/// <c>C4RuleSet</c> established.
/// </para>
/// <para>
/// <b>A map with a problem still opens and still renders</b> (Requirement 3.5). Nothing here
/// stops anything; a dangling link is reported and drawn.
/// </para>
/// <para>
/// <b>Every rule is one pass over one collection, and the two that compare collections are
/// bounded by a lookup rather than a scan</b> - so a pathological document cannot hang a
/// "Validate all" (Requirement 14.9, <c>errors-and-warnings-panel</c> Requirement 3.5).
/// </para>
/// </remarks>
public static class WardleyRuleSet
{
    public const string LinkTargetMissingRuleId = "wardley.link-target-missing";
    public const string EvolveTargetMissingRuleId = "wardley.evolve-target-missing";
    public const string PipelineParentMissingRuleId = "wardley.pipeline-parent-missing";
    public const string CoordinateOutOfRangeRuleId = "wardley.coordinate-out-of-range";
    public const string DuplicateNameRuleId = "wardley.duplicate-name";
    public const string AnchorMissingRuleId = "wardley.anchor-missing";
    public const string UrlUndefinedRuleId = "wardley.url-undefined";

    /// <summary>Every problem in <paramref name="map"/>, in document order where it has one.</summary>
    public static IReadOnlyList<DiagramProblem> Validate(WardleyMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var problems = new List<DiagramProblem>();
        var names = map.Components.Select(component => component.Name).ToHashSet(StringComparer.Ordinal);

        DuplicateNames(map, problems);
        DanglingReferences(map, names, problems);
        Coordinates(map, problems);
        Urls(map, problems);
        Anchor(map, problems);

        return problems;
    }

    /// <summary>
    /// Two components sharing a name (Requirement 14.4).
    /// </summary>
    /// <remarks>
    /// The one rule here that reports an ELEMENT location rather than a line, and it is not an
    /// arbitrary choice: identity is keyed by name, so two statements with one name reconcile to
    /// <b>one</b> element. There is no single line that is "the" line - the defect is that one
    /// element is claimed twice - so the element is what the problem points at.
    /// </remarks>
    private static void DuplicateNames(WardleyMap map, List<DiagramProblem> problems)
    {
        foreach (var group in map.Components.GroupBy(component => component.Name, StringComparer.Ordinal))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            var lines = string.Join(", ", group.Select(component => component.Line.ToString(CultureInfo.InvariantCulture)));
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"'{group.Key}' is declared more than once (lines {lines}), so every link, evolve and pipeline that names it is ambiguous.",
                DuplicateNameRuleId,
                new DiagramProblemElementLocation(WardleyIdentityKeys.Of(group.First()))));
        }
    }

    /// <summary>
    /// A link, an `evolve` or a pipeline naming a component that does not exist
    /// (Requirement 14.2).
    /// </summary>
    /// <remarks>
    /// Each reports the LINE it was written on, because that is where the fix is made: the
    /// missing name is either a typo to correct or a component to add, and both are done in the
    /// statement the problem points at. The canvas does not need this to mark a dangling end -
    /// a link goes out carrying its endpoint names as written, so the canvas can already see
    /// which end resolves to nothing (<c>WardleyElementMapper</c>, Requirement 8.6).
    /// </remarks>
    private static void DanglingReferences(WardleyMap map, HashSet<string> names, List<DiagramProblem> problems)
    {
        foreach (var link in map.Links)
        {
            var arrow = link.Kind == WardleyLinkKind.Flow ? "+>" : "->";
            foreach (var end in new[] { link.Source, link.Target })
            {
                if (!names.Contains(end))
                {
                    problems.Add(new DiagramProblem(
                        DiagramProblemSeverity.Error,
                        $"'{end}' is not on this map, but '{link.Source}{arrow}{link.Target}' links to it.",
                        LinkTargetMissingRuleId,
                        new DiagramProblemLineLocation(link.Line)));
                }
            }
        }

        foreach (var evolve in map.Evolves.Where(evolve => !names.Contains(evolve.Name)))
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"'{evolve.Name}' is not on this map, but 'evolve {evolve.Name}' says where it is heading.",
                EvolveTargetMissingRuleId,
                new DiagramProblemLineLocation(evolve.Line)));
        }

        foreach (var pipeline in map.Pipelines.Where(pipeline => !names.Contains(pipeline.Parent)))
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"'{pipeline.Parent}' is not on this map, but a pipeline is declared for it.",
                PipelineParentMissingRuleId,
                new DiagramProblemLineLocation(pipeline.Line)));
        }
    }

    /// <summary>
    /// Any coordinate outside <c>0..1</c> (Requirement 14.3) - on anything the format positions,
    /// not only on components.
    /// </summary>
    /// <remarks>
    /// The map's space is bounded, so a value outside it cannot be drawn where the file says it
    /// is: the canvas clamps it, and the element then sits somewhere the document does not
    /// claim. That silent disagreement between file and picture is what this reports.
    /// </remarks>
    private static void Coordinates(WardleyMap map, List<DiagramProblem> problems)
    {
        foreach (var component in map.Components)
        {
            Check(component.Position, component.Line, $"'{component.Name}'");
        }

        foreach (var pipeline in map.Pipelines)
        {
            if (pipeline.LegacyExtent is { } extent)
            {
                Check(extent, pipeline.Line, $"the pipeline on '{pipeline.Parent}'");
            }

            foreach (var child in pipeline.Children.Where(child => Outside(child.Maturity)))
            {
                Report(child.Line, $"'{child.Name}'", child.Maturity, "maturity");
            }
        }

        foreach (var evolve in map.Evolves.Where(evolve => Outside(evolve.Maturity)))
        {
            Report(evolve.Line, $"the evolution target for '{evolve.Name}'", evolve.Maturity, "maturity");
        }

        foreach (var note in map.Notes)
        {
            Check(note.Position, note.Line, "a note");
        }

        foreach (var accelerator in map.Accelerators)
        {
            Check(accelerator.Position, accelerator.Line, $"'{accelerator.Name}'");
        }

        foreach (var attitude in map.Attitudes)
        {
            Check(attitude.From, attitude.Line, $"a {attitude.Kind} region");
            Check(attitude.To, attitude.Line, $"a {attitude.Kind} region");
        }

        foreach (var annotation in map.Annotations)
        {
            foreach (var occurrence in annotation.Occurrences)
            {
                Check(occurrence, annotation.Line, $"annotation {annotation.Number.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        void Check(WardleyCoordinate position, uint line, string what)
        {
            if (Outside(position.Visibility))
            {
                Report(line, what, position.Visibility, "visibility");
            }

            if (Outside(position.Maturity))
            {
                Report(line, what, position.Maturity, "maturity");
            }
        }

        void Report(uint line, string what, double value, string axis)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Error,
                $"The {axis} of {what} is {value.ToString("0.####", CultureInfo.InvariantCulture)}, and a Wardley map's axes run from 0 to 1.",
                CoordinateOutOfRangeRuleId,
                new DiagramProblemLineLocation(line)));
        }
    }

    /// <summary>
    /// A `url(...)` naming no `url` definition (Requirement 14.6, the part that can be judged
    /// without a filesystem).
    /// </summary>
    /// <remarks>
    /// Whether a defined address actually resolves is not this function's business - it would
    /// need the disk, and Requirement 14.9 says these rules take a model and nothing else.
    /// <see cref="WardleyValidator"/> adds that check, where the request carries the project
    /// root.
    /// </remarks>
    private static void Urls(WardleyMap map, List<DiagramProblem> problems)
    {
        var defined = map.Urls.Select(url => url.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var component in map.Components.Where(component => component.Url.Length > 0 && !defined.Contains(component.Url)))
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"'{component.Name}' points at a url called '{component.Url}', which this map does not define.",
                UrlUndefinedRuleId,
                new DiagramProblemLineLocation(component.Line)));
        }
    }

    /// <summary>
    /// A map with no `anchor` (Requirement 14.5).
    /// </summary>
    /// <remarks>
    /// A warning, and reported against the file rather than any line - there is no statement to
    /// point at, because the problem is the statement that is not there. It never blocks
    /// anything: a map under construction has no anchor yet, and being told off for that while
    /// typing would be worse than useless.
    /// </remarks>
    private static void Anchor(WardleyMap map, List<DiagramProblem> problems)
    {
        if (map.Components.Count > 0 && map.Components.All(component => component.Kind != WardleyElementKind.Anchor))
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                "This map has no anchor, so nothing says whose need the value chain hangs from.",
                AnchorMissingRuleId));
        }
    }

    private static bool Outside(double value) => value is < 0d or > 1d;
}
