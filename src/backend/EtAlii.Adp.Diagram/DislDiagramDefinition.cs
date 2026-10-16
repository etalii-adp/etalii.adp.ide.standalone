using System.Reflection;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// A module's bundled DISL definition, loaded once, and what every module derives from it the same
/// way: the palette, the context menus and the property rows mapped onto the host's types with the
/// wire ids of the module's <c>x-</c> block, and a finding's severity as the Errors and Warnings panel
/// counts it.
/// </summary>
/// <remarks>
/// <para>
/// What differs between modules stays in the module: which element an id names, which environment a
/// menu or a form is derived in, where a finding is reported, and the edits the definition's
/// operations become. A module holds one of these in its <c>*Definition</c> class and hands it the
/// target, the element or the finding.
/// </para>
/// <para>
/// <b>A shortcut is sent as a browser spells the key</b> (<see cref="DerivedShortcut.EventKey"/>),
/// with its modifiers, because that is how the client sends a keypress and how
/// <see cref="ContextShortcutDefinition.Matches"/> compares it: <c>Alt+Up</c> is <c>ArrowUp</c> with
/// Alt.
/// </para>
/// </remarks>
public sealed class DislDiagramDefinition
{
    private readonly Lazy<BundledDefinition> _loaded;
    private readonly Lazy<WireIdMap> _ids;
    private readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> _toolbox;
    private readonly Lazy<Dictionary<string, string>> _standingReasons;

    /// <summary>Loads nothing yet: the definition is read the first time anything asks for it.</summary>
    /// <param name="assembly">The module's assembly, which carries the definition as an embedded resource.</param>
    /// <param name="logicalName">The resource's name, the definition's file name (<c>timeline.dis</c>).</param>
    /// <param name="wireBlock">The block holding the module's wire ids (<c>x-timeline</c>).</param>
    /// <param name="plugins">The host's implementations of the definition's plugin functions; none when null.</param>
    /// <param name="standingOperations">
    /// The operations whose menu entry carries its first <c>unavailable</c> sentence even while it is
    /// available, as a hand-written menu did; the client shows a reason only for an unavailable action.
    /// </param>
    public DislDiagramDefinition(
        Assembly assembly,
        string logicalName,
        string wireBlock,
        Func<DislPluginFunctions>? plugins = null,
        IReadOnlyCollection<string>? standingOperations = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(logicalName);
        ArgumentException.ThrowIfNullOrEmpty(wireBlock);

        _loaded = new(() => BundledDefinition.Load(assembly, logicalName, plugins?.Invoke() ?? DislPluginFunctions.None));
        _ids = new(() => WireIdMap.Of(Specification, wireBlock));
        _toolbox = new(() =>
        [
            .. ToolboxDerivation.Derive(Specification, Ids)
                .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
        ]);
        _standingReasons = new(() => StandingReasonsOf(standingOperations ?? []));
    }

    /// <summary>The definition.</summary>
    public DislSpecification Specification => _loaded.Value.Specification;

    /// <summary>What the runtime had to say about the definition as it loaded it.</summary>
    public IReadOnlyList<DislDiagnostic> Diagnostics => _loaded.Value.Diagnostics;

    /// <summary>The wire ids of the module's block.</summary>
    public WireIdMap Ids => _ids.Value;

    /// <summary>The palette.</summary>
    public IReadOnlyList<ToolboxItemDefinition> Toolbox => _toolbox.Value;

    /// <summary>The context menu of <paramref name="target"/>, derived in <paramref name="env"/>.</summary>
    public IReadOnlyList<ContextActionGroupDefinition> Menus(DislMenuTarget target, DislEnv env) =>
    [
        .. ContextMenuDerivation.Derive(Specification, target, env, Ids)
            .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
    ];

    /// <summary>The property rows of <paramref name="element"/>'s form, derived in <paramref name="env"/>.</summary>
    public IReadOnlyList<ContextPropertyDefinition> Rows(DislElement element, DislEnv env) =>
        [.. FormDerivation.Derive(Specification, element, env, Ids).Select(Row)];

    /// <summary>
    /// A menu entry as the host sends it: its shortcut as a browser spells the key, and the sentence
    /// of a standing operation even while it is available.
    /// </summary>
    public ContextActionDefinition Action(DerivedMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ContextActionDefinition(
            entry.Id,
            entry.Label,
            entry.Icon,
            ShortcutOf(entry),
            entry.Available,
            entry is { Available: true, Operation: { } operation } && _standingReasons.Value.TryGetValue(operation, out var standing)
                ? standing
                : entry.UnavailableReason);
    }

    /// <summary>A menu entry's shortcut as a browser spells the key, with its modifiers; none when it has none.</summary>
    public static ContextShortcutDefinition? ShortcutOf(DerivedMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Shortcut is { } key ? new ContextShortcutDefinition(key.EventKey, key.Ctrl, key.Shift, key.Alt, key.Meta) : null;
    }

    /// <summary>
    /// A row as the grid draws it: a retype row and a <c>select</c> are a Choice among their
    /// candidates, a <c>textarea</c> the Text editor, <c>tags</c>, <c>slider</c> and <c>switch</c>
    /// their own, anything else a line. Only a Choice, the tags and the slider carry candidates.
    /// </summary>
    public static ContextPropertyDefinition Row(DerivedRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var editor = row.Retypes
            ? ContextPropertyEditor.Choice
            : row.Widget switch
            {
                "select" => ContextPropertyEditor.Choice,
                "textarea" => ContextPropertyEditor.Text,
                "tags" => ContextPropertyEditor.Tags,
                "slider" => ContextPropertyEditor.Slider,
                "switch" => ContextPropertyEditor.Toggle,
                _ => ContextPropertyEditor.Line,
            };
        var candidates = editor is ContextPropertyEditor.Choice or ContextPropertyEditor.Tags or ContextPropertyEditor.Slider ? row.Candidates ?? [] : null;
        return new ContextPropertyDefinition(row.Id, row.Label, row.Value, editor, row.ReadOnlyReason, row.Group, candidates);
    }

    /// <summary>
    /// A finding's severity as the panel counts it: <c>error</c> an error, <c>info</c> and
    /// <c>hint</c> information, anything else a warning.
    /// </summary>
    public static DiagramProblemSeverity SeverityOf(DislFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.Severity switch
        {
            "error" => DiagramProblemSeverity.Error,
            "info" or "hint" => DiagramProblemSeverity.Info,
            _ => DiagramProblemSeverity.Warning,
        };
    }

    /// <summary>The first <c>unavailable</c> sentence of each of <paramref name="operations"/>.</summary>
    private Dictionary<string, string> StandingReasonsOf(IReadOnlyCollection<string> operations)
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        if (operations.Count == 0) return reasons;

        var declared = Specification.Root.GetProperty("behavior").GetProperty("operations");
        foreach (var name in operations)
        {
            var sentence = declared.TryGetProperty(name, out var operation)
                && operation.TryGetProperty("unavailable", out var unavailable)
                && unavailable.ValueKind == JsonValueKind.Array
                    ? unavailable.EnumerateArray()
                        .Select(reason => reason.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null)
                        .FirstOrDefault(message => message is not null)
                    : null;
            reasons[name] = sentence ?? throw new InvalidOperationException($"The definition's {name} states no unavailable sentence to carry while it is available.");
        }

        return reasons;
    }
}
