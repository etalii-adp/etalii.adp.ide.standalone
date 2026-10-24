using System.Globalization;
using System.Runtime.CompilerServices;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The binding's reading of a <see cref="SankeyBody"/> as the module reads it: the <see cref="SankeyModel"/> the
/// layout, the mapper, the session and the commands read, every value turned from the text it was written with into
/// the module's value, and the sentences under <c>sankey.unreadable</c> for what will not read (Requirement 4.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for language gaps 2 and 3</b>, and names them in the companion document. FBL reads every slot as
/// its written text (gap 1, answered by FBL 0.5's <c>read: "text"</c>), but it cannot read a value that will not
/// convert without losing its entry, nor word a finding for an entry that is still drawn (an unknown key, a value
/// that will not read, the header's three sentences), so this class converts and reports. The binding declares no
/// <c>header</c>, so <c>fbl.header-mismatch</c> never appears (Requirement 4.5).
/// </para>
/// <para>
/// <b>Numbers are read as the module always has</b> (B2, Q2): .NET's invariant parse of the text, quoted or not, so
/// <c>"12"</c> is 12, and <c>0x10</c> and <c>.inf</c> are not numbers and are reported.
/// </para>
/// <para>
/// <b>An entry written as a flow mapping</b> (<c>- {id: a, name: A}</c>, finding B16) is one value to FBL, whose
/// members CEL sees but no key slot binds (FBL section 4.3), so its values are read from the binding's computed
/// <c>written</c> attribute: the written text of every member. It is the one place a value does not come from its
/// slot, and it is named in the companion beside the gaps.
/// </para>
/// <para>
/// <b>The problems are listed in the order the module has always listed them</b>: the document's unknown keys, its
/// settings, each node entry (its unknown keys, then its column), each flow entry (its unknown keys, its value and
/// its step), and the version last. <see cref="SankeyFindingOrder"/> then orders the findings by their lines.
/// </para>
/// <para>
/// <b>Reading never throws on content.</b> A body that cannot be read opens empty with the one problem it gives.
/// </para>
/// </remarks>
public static class SankeyReader
{
    /// <summary>How the sentence for a text that is not YAML begins; the reason follows it.</summary>
    internal const string NotYaml = "The document could not be read as YAML: ";

    private const string HeaderKey = "sankey";
    private const string Written = "written";
    private const string UnknownKeys = "unknownKeys";

    private static readonly ConditionalWeakTable<SankeyModel, SankeyBody> Bodies = [];

    /// <summary>What the store keeps of <paramref name="text"/>: the model, its problems and, for a body that cannot be read, why. <b>Never throws.</b></summary>
    public static ParseOutcome<SankeyModel> Outcome(string text) => SankeyBody.Parse(text).Reading;

    /// <summary>The model of <paramref name="text"/>. <b>Never throws.</b></summary>
    public static SankeyModel Parse(string text) => Outcome(text).Model;

    /// <summary>The body <paramref name="model"/> was read from; null for a model no body was read into, such as <see cref="SankeyModel.Empty"/>.</summary>
    internal static SankeyBody? BodyOf(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Bodies.TryGetValue(model, out var body) ? body : null;
    }

    /// <summary>
    /// The reader's findings for the definition's constraints (DISL section 8.7): a body that cannot be read is one
    /// <c>std.unparseable</c> with the YAML reader's sentence; otherwise every problem is a <c>std.unreadableEntry</c>
    /// on its 1-based line, in the order they are listed, and every node read without an id a <c>std.missingId</c>.
    /// </summary>
    internal static IReadOnlyList<DislReaderFinding> Findings(ParseOutcome<SankeyModel> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var model = outcome.Model;
        if (!outcome.IsUsable)
        {
            var problem = model.Problems.FirstOrDefault();
            var reason = problem?.Message is { } message && message.StartsWith(NotYaml, StringComparison.Ordinal) ? message[NotYaml.Length..] : outcome.Unreadable;
            return [DislReaderFinding.NotParsed(reason, (problem?.Line ?? 0) + 1)];
        }

        return
        [
            .. model.Problems.Select(problem => DislReaderFinding.Unreadable(problem.Message, problem.Line + 1)),
            .. model.Nodes
                .Where(node => node.Id.Length == 0)
                .Select(node => new DislReaderFinding(Specification.Fbl.FindingCodes.MissingId, new Dictionary<string, object?>(StringComparer.Ordinal), node.Range.Start + 1)),
        ];
    }

    /// <summary>What <paramref name="body"/> reads: its model, every problem, and why it cannot be read when it cannot.</summary>
    internal static ParseOutcome<SankeyModel> Read(SankeyBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Refusal is { } refusal)
        {
            var problem = new ParseProblem(refusal.Line, NotYaml + refusal.Message);
            return new ParseOutcome<SankeyModel>(Remember(SankeyModel.Empty with { Problems = [problem] }, body), [problem], problem.Message);
        }

        var elements = body.Model.Elements.OrderBy(element => element.OwnSpan.Start).ToList();
        if (elements.FirstOrDefault(element => element.Rule == "document") is not { } document)
        {
            // A document whose root is not a mapping declares nothing, and says nothing about it.
            var nothing = Remember(SankeyModel.Empty with { Problems = [] }, body);
            return new ParseOutcome<SankeyModel>(nothing, []);
        }

        List<ParseProblem> problems = [];
        var root = body.LinesOf(document.OwnSpan);
        ReportUnknownKeys(body, document, root, "diagram", problems);
        var settings = ReadSettings(body, document, root, problems);
        var nodes = new List<SankeyNode>();
        var flows = new List<SankeyFlow>();
        foreach (var element in elements)
        {
            switch (element.Rule)
            {
                case "node":
                    nodes.Add(ReadNode(body, element, problems));
                    break;
                case "node-not-a-mapping":
                    problems.Add(new ParseProblem(body.LinesOf(element.OwnSpan).Start, "A node entry is not a mapping and was passed over."));
                    break;
            }
        }

        foreach (var element in elements)
        {
            switch (element.Rule)
            {
                case "flow":
                    flows.Add(ReadFlow(body, element, problems));
                    break;
                case "flow-not-a-mapping":
                    problems.Add(new ParseProblem(body.LinesOf(element.OwnSpan).Start, "A flow entry is not a mapping and was passed over."));
                    break;
            }
        }

        var version = ReadVersion(body, document, root, problems);
        var model = Remember(new SankeyModel(settings, nodes, flows, problems, version), body);
        return new ParseOutcome<SankeyModel>(model, problems);
    }

    /// <summary>
    /// The binding's reading of <paramref name="body"/> with every value as the module reads it, for
    /// <see cref="DislModelBuilder"/>: a number as a number, a value that will not read or is empty left out, a
    /// setting as the module draws it, and an entry written as a flow mapping with the values and the id its members are
    /// written with. The library's own findings are left out: the definition's rules report what they would (Q4).
    /// </summary>
    internal static FblModel Values(SankeyBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Refusal is not null)
        {
            return new FblModel([], [], false);
        }

        var model = body.Reading.Model;
        var nodes = new Queue<SankeyNode>(model.Nodes);
        var flows = new Queue<SankeyFlow>(model.Flows);
        var taken = body.Model.Elements.Where(element => element.IdIsStored).Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var elements = new List<FblElement>();
        foreach (var element in body.Model.Elements.OrderBy(element => element.OwnSpan.Start))
        {
            switch (element.Rule)
            {
                case "document":
                    var settings = model.Settings;
                    elements.Add(With(element, element.Attributes.Where(attribute => attribute.Key is "version" or UnknownKeys), new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["format"] = settings.FormatLine >= 0 ? settings.Format : null,
                        ["flowColor"] = settings.FlowColorLine >= 0 ? settings.FlowColor : null,
                        ["thickness"] = settings.ThicknessLine >= 0 ? settings.Thickness : null,
                    }));
                    break;
                case "node":
                    var node = nodes.Dequeue();
                    var read = With(element, element.Attributes.Where(attribute => attribute.Key == UnknownKeys), new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["storedId"] = Present(node.Id),
                        ["name"] = Present(node.Name),
                        ["description"] = Present(node.Description),
                        ["color"] = Present(node.Color),
                        ["column"] = node.Column is { } column ? (long)column : null,
                        ["note"] = Present(node.Note),
                        ["format"] = Present(node.Format),
                    });

                    // A node written as a flow mapping is known by the id its member is written with, as its first holder.
                    elements.Add(!element.IdIsStored && !element.Attributes.ContainsKey("storedId") && node.Id.Length > 0 && taken.Add(node.Id)
                        ? read with { Id = node.Id, IdIsStored = true }
                        : read);
                    break;
                case "flow":
                    var flow = flows.Dequeue();
                    elements.Add(With(element, element.Attributes.Where(attribute => attribute.Key == UnknownKeys), new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["statedId"] = Present(flow.ExplicitId),
                        ["from"] = Present(flow.From),
                        ["to"] = Present(flow.To),
                        ["value"] = flow.Value,
                        ["step"] = flow.Step,
                        ["color"] = Present(flow.Color),
                        ["description"] = Present(flow.Description),
                    }));
                    break;
            }
        }

        return new FblModel(elements, [], false);
    }

    /// <summary><paramref name="element"/> with the attributes <paramref name="kept"/> and the values <paramref name="read"/> that are set.</summary>
    private static FblElement With(FblElement element, IEnumerable<KeyValuePair<string, object?>> kept, Dictionary<string, object?> read)
    {
        var attributes = kept.ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        foreach ((string name, object? value) in read.Where(attribute => attribute.Value is not null))
        {
            attributes[name] = value;
        }

        return element with { Attributes = attributes };
    }

    /// <summary>A value read as empty text is not written: absent, as the binding reads a key the entry does not state.</summary>
    private static string? Present(string value) => value.Length > 0 ? value : null;

    private static SankeyModel Remember(SankeyModel model, SankeyBody body)
    {
        Bodies.AddOrUpdate(model, body);
        return model;
    }

    private static SankeySettings ReadSettings(SankeyBody body, FblElement document, LineRange root, List<ParseProblem> problems)
    {
        var settings = SankeySettings.Default;

        if (Scalar(document, "format", "format") is { } format)
        {
            settings = settings with { Format = format, FormatLine = body.KeyLine(root, "format") };
        }

        if (Scalar(document, "flowColor", "flow-color") is { } flowColor)
        {
            var line = body.KeyLine(root, "flow-color");
            if (flowColor is SankeySettings.FromSource or SankeySettings.FromTarget)
            {
                settings = settings with { FlowColor = flowColor };
            }
            else
            {
                problems.Add(new ParseProblem(line, $"`flow-color: {flowColor}` is neither `{SankeySettings.FromSource}` nor `{SankeySettings.FromTarget}`; flows take their target's colour."));
            }

            settings = settings with { FlowColorLine = line };
        }

        if (Scalar(document, "thickness", "thickness") is { } thickness)
        {
            var line = body.KeyLine(root, "thickness");
            if (double.TryParse(thickness, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0)
            {
                settings = settings with { Thickness = Math.Clamp(value, SankeyGeometry.MinimumScale, SankeyGeometry.MaximumScale) };
            }
            else
            {
                problems.Add(new ParseProblem(line, $"`thickness: {thickness}` is not a number above zero and was ignored."));
            }

            settings = settings with { ThicknessLine = line };
        }

        return settings;
    }

    private static int? ReadVersion(SankeyBody body, FblElement document, LineRange root, List<ParseProblem> problems)
    {
        if (Scalar(document, "version", HeaderKey) is not { } text)
        {
            problems.Add(new ParseProblem(0, $"The document does not begin with `{HeaderKey}: {SankeyModel.CurrentVersion}`."));
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            problems.Add(new ParseProblem(body.KeyLine(root, HeaderKey), $"The version `{text}` is not a number."));
            return null;
        }

        if (version != SankeyModel.CurrentVersion)
        {
            problems.Add(new ParseProblem(body.KeyLine(root, HeaderKey), $"This document states version {version}; this module reads version {SankeyModel.CurrentVersion}."));
        }

        return version;
    }

    private static SankeyNode ReadNode(SankeyBody body, FblElement element, List<ParseProblem> problems)
    {
        var range = body.LinesOf(element.OwnSpan);
        ReportUnknownKeys(body, element, range, "node", problems);

        int? column = null;
        if (Number(body, element, "column", "column", range, problems) is { } stated)
        {
            if (stated >= 1 && Math.Abs(stated - Math.Floor(stated)) < double.Tolerance)
            {
                column = (int)Math.Min(stated, 1000);
            }
            else
            {
                problems.Add(new ParseProblem(range.Start, $"`column: {stated.ToString(CultureInfo.InvariantCulture)}` is not a whole number from 1; the flows place this node."));
            }
        }

        return new SankeyNode(
            Scalar(element, "storedId", "id") ?? "",
            Scalar(element, "name", "name") ?? "",
            Scalar(element, "color", "color") ?? "",
            Scalar(element, "note", "note") ?? "",
            Scalar(element, "format", "format") ?? "",
            column,
            Scalar(element, "description", "description") ?? "",
            range);
    }

    private static SankeyFlow ReadFlow(SankeyBody body, FblElement element, List<ParseProblem> problems)
    {
        var range = body.LinesOf(element.OwnSpan);
        ReportUnknownKeys(body, element, range, "flow", problems);
        return new SankeyFlow(
            Scalar(element, "statedId", "id") ?? "",
            Scalar(element, "from", "from") ?? "",
            Scalar(element, "to", "to") ?? "",
            Number(body, element, "value", "value", range, problems),
            Number(body, element, "step", "step", range, problems),
            Scalar(element, "color", "color") ?? "",
            Scalar(element, "description", "description") ?? "",
            range);
    }

    /// <summary>The keys an entry holds that the module does not read: the binding's computed <c>unknownKeys</c>, each reported on its own line and kept.</summary>
    private static void ReportUnknownKeys(SankeyBody body, FblElement element, LineRange range, string what, List<ParseProblem> problems)
    {
        if (element.Attributes.GetValueOrDefault(UnknownKeys) is not IEnumerable<object?> keys)
        {
            return;
        }

        foreach (var key in keys.OfType<string>())
        {
            problems.Add(new ParseProblem(body.KeyLine(range, key), $"`{key}` is not a key this module reads on a {what}; the line is kept."));
        }
    }

    /// <summary>
    /// A number from the entry, or null when the key is absent or will not read - the second of which is reported,
    /// because a silently missing value reads as a lost edit.
    /// </summary>
    private static double? Number(SankeyBody body, FblElement element, string attribute, string key, LineRange range, List<ParseProblem> problems)
    {
        if (Scalar(element, attribute, key) is not { } text)
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return value;
        }

        problems.Add(new ParseProblem(body.KeyLine(range, key), $"`{key}: {text}` is not a number and was ignored."));
        return null;
    }

    /// <summary>
    /// The text <paramref name="attribute"/> is written with: its slot's, or, for an entry written as a flow mapping,
    /// its member's under <paramref name="key"/>; null when it is not written or is no scalar.
    /// </summary>
    private static string? Scalar(FblElement element, string attribute, string key) =>
        element.Attributes.TryGetValue(attribute, out var value)
            ? value as string
            : element.Attributes.GetValueOrDefault(Written) is IReadOnlyDictionary<string, object?> members ? members.GetValueOrDefault(key) as string : null;
}
