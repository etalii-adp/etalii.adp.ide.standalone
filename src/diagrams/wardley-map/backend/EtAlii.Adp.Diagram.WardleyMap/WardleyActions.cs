using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using static EtAlii.Adp.Diagram.WardleyMap.WardleyContextActionProvider;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What an action or a property row is run on: the map, the identities held beside it, and the
/// element the selection names, if the map still has it.
/// </summary>
/// <param name="BodyPath">The map's file.</param>
/// <param name="Map">The map as it reads now.</param>
/// <param name="Identities">The ids of the map's elements, kept beside it.</param>
/// <param name="ElementId">The id the selection names; empty for the map itself.</param>
public sealed record WardleySubject(
    string BodyPath,
    WardleyMap Map,
    IReadOnlyList<WardleyIdentityEntry> Identities,
    string ElementId)
{
    /// <summary>The identity the selection names, or null for the map itself or an element that is gone.</summary>
    public WardleyIdentityEntry? Entry { get; } = Identities.FirstOrDefault(candidate => candidate.Id == ElementId);

    /// <summary>Whether the selection is the map itself rather than an element on it.</summary>
    public bool IsMap => ElementId.Length == 0;

    /// <summary>Whether the selection names an element the map no longer has.</summary>
    public bool IsGone => !IsMap && Entry is null;

    /// <summary>The component the selection names, or null when it names something else.</summary>
    public WardleyComponent? Component => Entry is { Kind: WardleyIdentityKind.Component } entry
        ? Map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key)
        : null;

    /// <summary>The pipeline child the selection names, with its pipeline, or null when it names something else.</summary>
    public (WardleyPipeline Pipeline, WardleyPipelineChild Child)? Child
    {
        get
        {
            if (Entry is not { Kind: WardleyIdentityKind.PipelineChild } entry)
            {
                return null;
            }

            foreach (var pipeline in Map.Pipelines)
            {
                foreach (var child in pipeline.Children)
                {
                    if (WardleyIdentityKeys.Of(pipeline, child) == entry.Key)
                    {
                        return (pipeline, child);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>The `evolve` statement of the selected component, or null.</summary>
    public WardleyEvolve? Evolve => Component is { } component
        ? Map.Evolves.FirstOrDefault(candidate => candidate.Name == component.Name)
        : null;

    /// <summary>The id of the component called <paramref name="name"/>, or empty.</summary>
    public string IdOf(string name) => Identities
        .FirstOrDefault(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == name)?.Id ?? "";

    /// <summary>The subject the selection of <paramref name="elementId"/> on the map at <paramref name="bodyPath"/> is.</summary>
    public static WardleySubject Of(string bodyPath, WardleyMap map, IWardleyDocumentStore documents, string elementId)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return new WardleySubject(bodyPath, map, documents.Identities(bodyPath), elementId);
    }
}

/// <summary>
/// Every action a Wardley map offers, one record each (code-structure FR-034). The menu, the
/// commit of what an action asked for, and the property grid's rows for the same edits all make
/// their command here, where they used to be three mappings that could drift apart.
/// </summary>
/// <remarks>
/// <para>
/// An action that would fail is not offered (Requirement 11.6). That is why an offer reads the
/// map: an unlink on an element with no link, a pipeline child on a legacy pipeline and a "stop
/// evolving" on something that is not evolving are all decided by what the document says.
/// </para>
/// <para>
/// A note, an annotation, an accelerator or an attitude region has no action: this module has no
/// command that edits one yet, and offering an action nothing implements would be exactly the
/// disagreement between menu and outcome Requirement 11.6 forbids.
/// </para>
/// </remarks>
public static class WardleyActions
{
    private const string AddGroup = "add";
    private const string IdentityGroup = "identity";
    private const string StrategyGroup = "strategy";
    private const string DecoratorGroup = "decorators";
    private const string LinkGroup = "links";
    private const string PipelineGroup = "pipeline";

    private const string NoLink = "This element has no link to remove.";

    /// <summary>Every action, in the order the menu offers them.</summary>
    public static IReadOnlyList<DiagramAction<WardleySubject>> All { get; } =
    [
        Add(AddComponentActionId, "Add component…", "mdi-plus", "component", (path, value) => new AddWardleyElementCommand(path, "component", value, 0.5d, 0.5d)),
        Add(AddAnchorActionId, "Add anchor…", "mdi-anchor", "anchor", (path, value) => new AddWardleyElementCommand(path, "anchor", value, 0.5d, 0.5d)),
        Add(AddSubmapActionId, "Add submap…", "mdi-map-outline", "submap", (path, value) => new AddWardleyElementCommand(path, "submap", value, 0.5d, 0.5d)),

        // Market and ecosystem are components carrying a decorator, not kinds of their own
        // (Requirement 13.2) - written in one command, so one drop is one undo.
        Add(AddMarketActionId, "Add market…", "mdi-store-outline", "market", (path, value) => new AddWardleyElementCommand(path, "component", value, 0.5d, 0.5d, WardleyDecorator.Market)),
        Add(AddEcosystemActionId, "Add ecosystem…", "mdi-graph-outline", "ecosystem", (path, value) => new AddWardleyElementCommand(path, "component", value, 0.5d, 0.5d, WardleyDecorator.Ecosystem)),
        Add(AddNoteActionId, "Add note…", "mdi-note-outline", "note", (path, value) => new AddWardleyNoteCommand(path, value, 0.5d, 0.5d)),
        Add(AddAnnotationActionId, "Add annotation…", "mdi-comment-text-outline", "annotation", (path, value) => new AddWardleyAnnotationCommand(path, value, 0.5d, 0.5d)),

        new(RenameActionId, IsComponent, (subject, value) => new RenameWardleyElementCommand(subject.BodyPath, subject.ElementId, value.Trim()))
        {
            Group = IdentityGroup,
            Offer = subject => Offered(subject, new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2"))),

            // The component's name is the drawn label, so the client can edit it in place.
            Ask = subject => new ContextExecutionRequiresInput(new ContextInputRequest(
                "Rename element", "mdi-pencil-outline", "Name", subject.Component!.Name, "Rename", subject.ElementId)),
            Validate = Named("Every element needs a name."),
        },

        new(RemoveActionId, IsComponent, (subject, _) => new RemoveWardleyElementCommand(subject.BodyPath, subject.ElementId))
        {
            Group = IdentityGroup,
            Offer = subject => Offered(subject, new ContextActionDefinition(RemoveActionId, "Remove", "mdi-trash-can-outline", new ContextShortcutDefinition("Delete"))),

            // Confirmed, because removing an element takes its links and its `evolve` with it -
            // more than the one statement the user is looking at.
            Confirm = subject => new RemovalConfirmation(
                "Remove element",
                $"Remove '{subject.Component!.Name}', and every link and evolution that names it?",
                "Remove"),
        },

        new(SetEvolveActionId, IsComponent, (subject, value) =>
            Maturity(value) is { } maturity
                ? new SetWardleyEvolveCommand(subject.BodyPath, subject.ElementId, Present: true, maturity, subject.Evolve?.Override ?? "")
                : null)
        {
            Group = StrategyGroup,
            Offer = subject => Offered(subject, new ContextActionDefinition(
                SetEvolveActionId, subject.Evolve is null ? "Evolve to…" : "Change evolution target…", "mdi-arrow-right-bold-outline")),
            Ask = subject => new ContextExecutionRequiresInput(new ContextInputRequest(
                "Evolve element",
                "mdi-arrow-right-bold-outline",
                "Target maturity (0 to 1)",
                Number(subject.Evolve?.Maturity ?? subject.Component!.Position.Maturity),
                "Save")),

            // Refused rather than clamped, unlike a drag: a drag past the edge is a slip of the
            // hand, while a typed `1.4` is a statement about a scale the user has misunderstood.
            Validate = value => Maturity(value) is not null
                ? ContextValidationResult.Accepted
                : ContextValidationResult.Rejected("An evolution target is a number between 0 and 1."),
        },

        new(ClearEvolveActionId, IsComponent, (subject, _) => new SetWardleyEvolveCommand(subject.BodyPath, subject.ElementId, Present: false, 0d))
        {
            Group = StrategyGroup,

            // Only offered when there is one to stop, which is the whole of Requirement 11.6.
            Offer = subject => subject.Component is not null && subject.Evolve is not null
                ? new ContextActionDefinition(ClearEvolveActionId, "Stop evolving", "mdi-arrow-right-bold-outline")
                : null,
        },

        new(ToggleInertiaActionId, IsComponent, (subject, value) =>
            new SetWardleyInertiaCommand(subject.BodyPath, subject.ElementId, Toggled(value, subject.Component!.Inertia)))
        {
            Group = StrategyGroup,
            Offer = subject => Offered(subject, new ContextActionDefinition(
                ToggleInertiaActionId, subject.Component?.Inertia == true ? "Clear inertia" : "Mark as having inertia", "mdi-weight")),
        },

        .. WardleyDecorators.All.Select(Decorator),

        Link(LinkActionId, "Link to…", "mdi-arrow-right", "Link to", "Link", WardleyLinkKind.Dependency),
        Link(FlowActionId, "Flow to…", "mdi-transfer-right", "Flow to", "Flow", WardleyLinkKind.Flow),

        new(UnlinkActionId, subject => subject.Entry is not null, (subject, value) =>
            LinksOf(subject) is var links && value.Length == 0 && links.Count == 1
                ? Unlink(subject, links[0])
                : links.FirstOrDefault(link => link.Id == value) is { } link ? Unlink(subject, link) : null)
        {
            Group = LinkGroup,
            Offer = subject => subject.Entry?.Kind switch
            {
                WardleyIdentityKind.Component when LinksOf(subject).Count > 0 =>
                    new ContextActionDefinition(UnlinkActionId, "Remove link…", "mdi-link-variant-off"),
                WardleyIdentityKind.Link when LinksOf(subject).Count > 0 =>
                    new ContextActionDefinition(UnlinkActionId, "Remove link", "mdi-link-variant-off", new ContextShortcutDefinition("Delete")),
                _ => null,
            },

            // One link is not a choice. Asking which of one would be a dialog that exists only
            // to be dismissed.
            Ask = subject => LinksOf(subject) switch
            {
                [] => new ContextExecutionFailed(NoLink),
                [_] => null,
                var links => new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                    "Remove link",
                    "mdi-link-variant-off",
                    "Remove",
                    links.Select(link => new ContextOptionNode(link.Id, link.Label, Selectable: true)).ToArray(),
                    NoLink)),
            },
        },

        new(AddToPipelineActionId, IsComponent, (subject, value) =>
            new SetWardleyPipelineMembershipCommand(subject.BodyPath, subject.ElementId, value.Trim(), Member: true))
        {
            Group = PipelineGroup,

            // A legacy two-coordinate pipeline holds no components, and Requirement 3.2 forbids
            // rewriting it into the form that does - so there is nothing to offer on one.
            Offer = subject => subject.Component is { } component && PipelineOf(subject, component) is not { Form: WardleyPipelineForm.Legacy }
                ? new ContextActionDefinition(
                    AddToPipelineActionId, PipelineOf(subject, component) is null ? "Start a pipeline…" : "Add to pipeline…", "mdi-pipe")
                : null,
            Ask = subject => new ContextExecutionRequiresInput(new ContextInputRequest(
                $"Add to {subject.Component!.Name}'s pipeline", "mdi-pipe", "Name", "", "Add")),
            Validate = Named("Every element needs a name."),
        },

        new(RemoveFromPipelineActionId, subject => subject.Entry?.Kind == WardleyIdentityKind.PipelineChild, (subject, _) =>
            subject.Child is var (pipeline, child)
                ? new SetWardleyPipelineMembershipCommand(subject.BodyPath, subject.IdOf(pipeline.Parent), child.Name, Member: false)
                : null)
        {
            Group = PipelineGroup,
            Offer = subject => subject.Child is not null
                ? new ContextActionDefinition(RemoveFromPipelineActionId, "Remove from pipeline", "mdi-trash-can-outline", new ContextShortcutDefinition("Delete"))
                : null,
        },
    ];

    // ---- the shapes several actions share -----------------------------------------------------

    /// <summary>
    /// An add, offered on the map itself and asked for before anything is written, so the element
    /// arrives named rather than as "New component" for the user to find and rename.
    /// </summary>
    /// <remarks>
    /// Placed in the middle of the map. Requirement 13.4 asks for a dropped element to land where
    /// it was dropped, but nothing carries a drop position to a provider: <c>ExecuteActionRequest</c>
    /// has no such field and <c>ContextTarget</c> no such member. The gap is reported rather than
    /// papered over (Requirement 12.5); until it closes, an added element lands in the middle and
    /// the user's next drag places it.
    /// </remarks>
    private static DiagramAction<WardleySubject> Add(
        string id,
        string label,
        string icon,
        string spelled,
        Func<string, string, ICommand> command)
    {
        // A note and an annotation carry text rather than a name, and telling someone their note
        // needs a name is telling them the wrong thing.
        var text = id is AddNoteActionId or AddAnnotationActionId;

        return new DiagramAction<WardleySubject>(id, _ => true, (subject, value) => command(subject.BodyPath, value.Trim()))
        {
            Group = AddGroup,
            Offer = subject => subject.IsMap ? new ContextActionDefinition(id, label, icon) : null,
            Ask = _ => new ContextExecutionRequiresInput(new ContextInputRequest(
                $"Add {spelled}", "mdi-plus", text ? "Text" : "Name", "", "Add")),
            Validate = Named(text ? "A note needs something to say." : "Every element needs a name."),
        };
    }

    /// <summary>The toggle of one decorator, which the property grid's toggle row sets the same way.</summary>
    private static DiagramAction<WardleySubject> Decorator(WardleyDecorator decorator)
    {
        var id = DecoratorActionIdFor(decorator);
        return new DiagramAction<WardleySubject>(id, IsComponent, (subject, value) =>
            new SetWardleyDecoratorCommand(subject.BodyPath, subject.ElementId, decorator, Toggled(value, subject.Component!.Decorators.Contains(decorator))))
        {
            Group = DecoratorGroup,
            Offer = subject => Offered(subject, new ContextActionDefinition(
                id,
                subject.Component?.Decorators.Contains(decorator) == true
                    ? $"Clear {WardleyDecorators.Spell(decorator)}"
                    : $"Mark as {WardleyDecorators.Spell(decorator)}",
                "mdi-tag-outline")),
        };
    }

    /// <summary>
    /// A link or a flow to another element. Each option's id is the element id the command needs,
    /// so the dialog's answer is already what is dispatched.
    /// </summary>
    private static DiagramAction<WardleySubject> Link(string id, string label, string icon, string title, string confirm, WardleyLinkKind kind) =>
        new(id, IsComponent, (subject, value) => new SetWardleyLinkCommand(subject.BodyPath, subject.ElementId, value, kind, Present: true))
        {
            Group = LinkGroup,
            Offer = subject => Offered(subject, new ContextActionDefinition(id, label, icon)),
            Ask = subject => new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                title, "mdi-arrow-right", confirm, Others(subject), "This map has no other element to link to.")),
        };

    // ---- looking things up --------------------------------------------------------------------

    private static bool IsComponent(WardleySubject subject) => subject.Component is not null;

    /// <summary><paramref name="definition"/> where the subject is a component; no offer otherwise.</summary>
    private static ContextActionDefinition? Offered(WardleySubject subject, ContextActionDefinition definition) =>
        subject.Component is null ? null : definition;

    private static WardleyPipeline? PipelineOf(WardleySubject subject, WardleyComponent component) =>
        subject.Map.Pipelines.FirstOrDefault(candidate => candidate.Parent == component.Name);

    /// <summary>Every other element on the map, as the options of a "link to" dialog.</summary>
    private static IReadOnlyList<ContextOptionNode> Others(WardleySubject subject) =>
        subject.Map.Components
            .Where(candidate => candidate.Name != subject.Component!.Name)
            .Select(candidate => new ContextOptionNode(subject.IdOf(candidate.Name), candidate.Name, Selectable: true))
            .Where(option => option.Id.Length > 0)
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>The links the selection is, or is an end of, each labelled the way a human reads it.</summary>
    private static IReadOnlyList<WardleyLinkChoice> LinksOf(WardleySubject subject)
    {
        var name = subject.Component?.Name;
        var links = name is not null
            ? subject.Map.Links.Where(link => link.Source == name || link.Target == name)
            : subject.Map.Links.Where(link => WardleyIdentityKeys.Of(link) == subject.Entry?.Key);

        return links
            .Select(link => new WardleyLinkChoice(
                link.Source,
                link.Target,
                link.Kind,
                link.Kind == WardleyLinkKind.Flow ? $"{link.Source} +> {link.Target}" : $"{link.Source} -> {link.Target}",
                $"{link.Source}{WardleyIdentityKeys.Separator}{link.Target}{WardleyIdentityKeys.Separator}{link.Kind}"))
            .ToArray();
    }

    private static SetWardleyLinkCommand Unlink(WardleySubject subject, WardleyLinkChoice link) =>
        new(subject.BodyPath, subject.IdOf(link.Source), subject.IdOf(link.Target), link.Kind, Present: false);

    // ---- reading what the user gave -----------------------------------------------------------

    /// <summary>A toggle's new state: the opposite of <paramref name="current"/> from the menu, the given one from the grid.</summary>
    private static bool Toggled(string value, bool current) =>
        value.Length == 0 ? !current : string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>A typed maturity, or null when it is not a number between 0 and 1.</summary>
    private static double? Maturity(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var maturity) && maturity is >= 0d and <= 1d
            ? maturity
            : null;

    private static Func<string, ContextValidationResult> Named(string refusal) =>
        value => string.IsNullOrWhiteSpace(value) ? ContextValidationResult.Rejected(refusal) : ContextValidationResult.Accepted;

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
