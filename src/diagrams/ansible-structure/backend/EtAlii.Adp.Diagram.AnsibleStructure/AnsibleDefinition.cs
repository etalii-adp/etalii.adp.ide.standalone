using System.Runtime.CompilerServices;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The Ansible structure diagram's bundled DISL definition (<c>definition/ansible-structure.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what is derived from it: the property rows of every node and
/// relation and the sentence a reparenting move is refused with, mapped onto the host's ids by its
/// <c>x-ansible</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is built from today's reading</b> by <see cref="AnsibleGraphModel"/>, once per reading and
/// kept for as long as the reading lives, so every selection of one reading reads one model. Reading the
/// folder through the binding comes later (ansible-structure-disl-fbl, tasks 6 and 7).
/// </para>
/// <para>
/// <b>Everything is read-only</b> (<c>env.readOnly</c>): every row carries the reason its form item gives,
/// which names the file the value is defined in. An inventory's groups and variable folders are one row
/// per item of a list, which a form item cannot state, and come from <see cref="AnsibleInventoryRows"/>
/// after the rows its form derives (gap 7).
/// </para>
/// <para>
/// <b>The reparenting sentence is the definition's</b>: the message of its one containment rule, which
/// refuses every nesting. The sentence a property write is refused with is not stated here: the binding's
/// <c>readOnly</c> will be it, and until then the property provider keeps it (finding A6).
/// </para>
/// </remarks>
internal static class AnsibleDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(AnsibleDefinition).Assembly, "ansible-structure.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-ansible"));

    private static readonly Lazy<string> LoadedMoveRefusal = new(MoveRefusalOf);

    private static readonly ConditionalWeakTable<AnsibleProject, DislDiagram> Models = [];

    /// <summary>What <c>env</c> reads: nothing in this diagram can be edited.</summary>
    private static readonly DislEnv Env = new(ReadOnly: true);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-ansible</c>.</summary>
    public static WireIdMap Ids => LoadedIds.Value;

    /// <summary>Why nothing on the diagram can be moved under another element: the message of the definition's containment rule.</summary>
    public static string MoveRefusal => LoadedMoveRefusal.Value;

    /// <summary>The property rows of the element <paramref name="elementId"/> names in <paramref name="project"/>: its form's, and an inventory's groups and variable folders; none for an id the project does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(AnsibleProject project, string elementId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(elementId);
        if (ModelOf(project).ElementById(elementId) is not { } element) return [];

        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(Row), .. AnsibleInventoryRows.Of(element)];
    }

    /// <summary><paramref name="project"/> as the definition's model.</summary>
    public static DislDiagram ModelOf(AnsibleProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Models.GetValue(project, read => AnsibleGraphModel.Build(Specification, read, AnsibleGraph.Derive(read)));
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
