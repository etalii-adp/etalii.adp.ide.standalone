using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The Ansible structure diagram's bundled DISL definition (<c>definition/ansible-structure.dis</c>, from
/// etalii-adp/etalii.adp) and its binding (<c>ansible-structure.fbl</c>, the module's own), each loaded once,
/// and what is derived from them: the property rows of every node and relation, the findings, the
/// sentence a reparenting move is refused with and the one a property write is refused with, mapped onto
/// the host's ids by the definition's <c>x-ansible</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is built by <see cref="DislModelBuilder"/> from what the plugin delivers</b>
/// (<see cref="AnsibleFolderPlugin"/>), which computes every id again by the definition's <c>derived</c>
/// expressions. An open folder builds it once per reading (<see cref="AnsibleFolder.Disl"/>), so every
/// selection of one reading reads one model.
/// </para>
/// <para>
/// <b>Everything is read-only</b> (<c>env.readOnly</c>): every row carries the reason its form item gives,
/// which names the file the value is defined in. An inventory's groups and variable folders are one row
/// per item of a list, which a form item cannot state, and come from <see cref="AnsibleInventoryRows"/>
/// after the rows its form derives (gap 7).
/// </para>
/// <para>
/// <b>The reparenting sentence is the definition's</b>: the message of its one containment rule, which
/// refuses every nesting. The sentence a property write is refused with is the binding's <c>readOnly</c>,
/// stated once there (finding A6).
/// </para>
/// </remarks>
internal static class AnsibleDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(AnsibleDefinition).Assembly, "ansible-structure.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-ansible"));

    private static readonly Lazy<string> LoadedMoveRefusal = new(MoveRefusalOf);

    private static readonly Lazy<FblBinding> LoadedBinding = new(LoadBinding);

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The binding the folder is read through: <c>ansible-structure.fbl#ansible</c>.</summary>
    public static FblBinding Binding => LoadedBinding.Value;

    /// <summary>Why no property can be written: the binding's <c>readOnly</c>.</summary>
    public static string WriteRefusal => Binding.ReadOnly is { Length: > 0 } reason
        ? reason
        : throw new InvalidOperationException("The binding words no reason to refuse a property write with.");

    /// <summary>The wire ids of <c>x-ansible</c>.</summary>
    public static WireIdMap Ids => LoadedIds.Value;

    /// <summary>Why nothing on the diagram can be moved under another element: the message of the definition's containment rule.</summary>
    public static string MoveRefusal => LoadedMoveRefusal.Value;

    /// <summary>The property rows of the element <paramref name="elementId"/> names in <paramref name="diagram"/>: its form's, and an inventory's groups and variable folders; none for an id the model does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DislDiagram diagram, string elementId)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(elementId);
        if (diagram.ElementById(elementId) is not { } element) return [];

        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row), .. AnsibleInventoryRows.Of(element)];
    }

    /// <summary>
    /// The findings of <paramref name="model"/>: every finding of its reading as the reader's (a file that did not
    /// parse or could not be opened is <c>std.unparseable</c>), then the definition's rules, as
    /// <see cref="ConstraintEvaluator"/> gives them. What the host is handed is <see cref="AnsibleFindings.For"/>
    /// of these, in today's order and words.
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(DislModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ConstraintEvaluator.Evaluate(Specification, model.Diagram, new DislConstraintOptions(Env, [.. model.Findings.Select(ReaderFinding)]));
    }

    /// <summary>
    /// The label the definition gives the value of the enumerated <paramref name="attribute"/> of
    /// <paramref name="element"/>, which are the words a file spells a directive with; the value itself when
    /// the enumeration words none.
    /// </summary>
    public static string Label(DislElement element, string attribute)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attribute);
        var value = element.ValueOf(attribute) as string ?? "";
        return Specification.Metamodel.Enums.TryGetValue(element.Type.Attributes[attribute].Type, out var enumeration) && enumeration.ValueOf(value) is { } known
            ? known.Label ?? known.Key
            : value;
    }

    /// <summary>The model of a reading through the binding, as <see cref="DislModelBuilder"/> builds it under the definition.</summary>
    public static DislModel Model(FblModel reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return DislModelBuilder.From(reading, Specification);
    }

    /// <summary>
    /// A finding of the reading as the reader's finding of its code: its message is the reason, and a line of
    /// 0, which says the parser gave none, is no line.
    /// </summary>
    private static DislReaderFinding ReaderFinding(Finding finding)
    {
        var detail = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, string value) in finding.Detail ?? new Dictionary<string, string>())
        {
            detail[key] = value;
        }
        detail["reason"] = finding.Message;
        var line = finding.Location is { Line: > 0 } located ? located.Line : (int?)null;
        return new DislReaderFinding(finding.Code, detail, line, finding.Location?.File is { Length: > 0 } file ? file : null);
    }

    /// <summary>The module's own binding, embedded beside the code.</summary>
    private static FblBinding LoadBinding()
    {
        var assembly = typeof(AnsibleDefinition).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith("ansible-structure.fbl", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), out var document);
        return document?.Bindings.GetValueOrDefault("ansible")
            ?? throw new InvalidOperationException($"The Ansible structure diagram's FBL binding does not load: {string.Join("; ", problems)}");
    }

    /// <summary>The message of the one rule of <c>constraints.rules</c> that refuses a containment, as the definition writes it.</summary>
    private static string MoveRefusalOf()
    {
        var rules = Specification.Root.GetProperty("constraints").GetProperty("rules").EnumerateArray()
            .Where(rule => rule.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "containment")
            .ToList();
        return rules is [var only] && only.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
            ? message.GetString()!
            : throw new InvalidOperationException("The definition words no single containment rule to refuse a reparenting move with.");
    }

    /// <summary>A row as the grid draws it: every item here is a <c>readonly</c> widget, which is one line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);
}
