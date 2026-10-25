using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using static EtAlii.Adp.Diagram.WardleyMap.WardleyContextActionProvider;
using static EtAlii.Adp.Diagram.WardleyMap.WardleyContextPropertyProvider;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

// The oracle (wardley-map-disl-fbl Requirement 3.6): the three providers' logic as it was written by hand
// before the toolbox, the menus, the rows and what they write were derived from the definition, kept
// here unchanged so that WardleyDerivedMenus, WardleyDerivedRows and WardleyDerivedOperations can compare
// the derived answers with it per document and element. Only the names changed, and the wire ids are
// the providers' constants.

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

    /// <summary>
    /// The component the selection names, or null when it names something else: the holder at
    /// the entry's ordinal, where two components carry one name (finding B9).
    /// </summary>
    public WardleyComponent? Component => Entry is { Kind: WardleyIdentityKind.Component } entry
        ? WardleyIdentityKeys.Component(Map, entry.Key)
        : null;

    /// <summary>
    /// Whether the selection is the first component to carry its name: the one every link,
    /// `evolve` and `pipeline` naming it attaches to (ruling 3). A later holder has none of them.
    /// </summary>
    public bool IsFirstHolder => Component is { } component && WardleyIdentityKeys.IsFirstHolder(Map, component);

    /// <summary>The pipeline child the selection names, with its pipeline, or null when it names something else.</summary>
    public (WardleyPipeline Pipeline, WardleyPipelineChild Child)? Child
    {
        get
        {
            return Entry is { Kind: WardleyIdentityKind.PipelineChild } entry
                ? WardleyIdentityKeys.Child(Map, entry.Key)
                : null;
        }
    }

    /// <summary>The `evolve` statement of the selected component, or null; a later holder has none.</summary>
    public WardleyEvolve? Evolve => Component is { } component && IsFirstHolder
        ? Map.Evolves.FirstOrDefault(candidate => candidate.Name == component.Name)
        : null;

    /// <summary>
    /// The id of the component called <paramref name="name"/>, or empty: the first holder's, which
    /// is the one a statement naming it attaches to.
    /// </summary>
    public string IdOf(string name) => IdOfHolder(name);

    /// <summary>The id of the component whose holder key is <paramref name="key"/>, or empty.</summary>
    public string IdOfHolder(string key) => Identities
        .FirstOrDefault(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == key)?.Id ?? "";

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
public static class HandWrittenWardleyActions
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
                DslNumber.Format(subject.Evolve?.Maturity ?? subject.Component!.Position.Maturity),
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
                : links.FirstOrDefault(candidate => candidate.Id == value) is { } chosen
                    ? Unlink(subject, chosen)
                    : null)
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

    /// <summary>The pipeline of the selected component, or null; a later holder has none (ruling 3).</summary>
    private static WardleyPipeline? PipelineOf(WardleySubject subject, WardleyComponent component) =>
        subject.IsFirstHolder
            ? subject.Map.Pipelines.FirstOrDefault(candidate => candidate.Parent == component.Name)
            : null;

    /// <summary>
    /// Every other element on the map, as the options of a "link to" dialog: each holder by its
    /// own id, a holder of the selection's own name among them, since a link to a later holder
    /// first gives it a name of its own (ruling 4).
    /// </summary>
    private static IReadOnlyList<ContextOptionNode> Others(WardleySubject subject) =>
        WardleyIdentityKeys.Holders(subject.Map.Components, WardleyIdentityKeys.Of)
            .Select(held => (Id: subject.IdOfHolder(held.Key), held.Item.Name))
            .Where(option => option.Id.Length > 0 && option.Id != subject.ElementId)
            .Select(option => new ContextOptionNode(option.Id, option.Name, Selectable: true))
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>The links the selection is, or is an end of, each labelled the way a human reads it.</summary>
    private static IReadOnlyList<WardleyLinkChoice> LinksOf(WardleySubject subject)
    {
        // A later holder has no link: every link naming it attaches to the first holder (ruling 3).
        var name = subject.Component?.Name;
        var links = name is not null
            ? subject.IsFirstHolder ? subject.Map.Links.Where(link => link.Source == name || link.Target == name) : []
            : WardleyIdentityKeys.Holders(subject.Map.Links, WardleyIdentityKeys.Of)
                .Where(held => held.Key == subject.Entry?.Key)
                .Select(held => held.Item);

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
}

/// <summary>
/// What a user can do to a Wardley element, offered the way every other action is - so it
/// reaches the ribbon, the right-click menu and the keyboard through one path, and the client
/// holds no key-to-action table of its own (Requirements 11.4, 11.5).
/// </summary>
/// <remarks>
/// <para>
/// On the shared frame (code-structure FR-034), with every action one record of
/// <see cref="WardleyActions"/>: what is offered, what is asked, and the command each makes. Every
/// action dispatches one of the module's commands through the project's history, so the menu, the
/// canvas and the property grid are three routes to one implementation with one inverse
/// (Requirement 9.8). The provider never touches the document.
/// </para>
/// <para>
/// <b>It answers from the target, not the model alone</b>: an element's id is the store's identity
/// for it, kept beside each map, so what an element offers is read with its map's identities.
/// </para>
/// </remarks>
public sealed class HandWrittenWardleyActionProvider(IHistoryStackStore historyStacks, IWardleyDocumentStore documents)
    : DiagramActionProvider<LineDocument, WardleyMap>(historyStacks, documents)
{
    private const string Gone = "That element is no longer on this map.";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <summary>
    /// A map of this module's, and one that can be written: nothing here can be done to a map that
    /// cannot, and Requirement 11.6 says an action that would fail is withheld rather than offered
    /// and refused.
    /// </summary>
    protected override bool Serves(ContextTarget target) =>
        base.Serves(target) && WardleyEditability.Editable(target.ResolvedFullPath);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<LineDocument, WardleyMap> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        // A map that could not be read offers nothing: every action on it would be refused.
        return entry.IsUsable ? HandWrittenWardleyActions.All.Offered(Subject(target, entry.Model)) : [];
    }

    /// <inheritdoc />
    protected override ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, WardleyMap> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        var subject = Subject(target, entry.Model);
        return HandWrittenWardleyActions.All.Applying(actionId, subject) is { } action
            ? RunActionAsync(target, action, subject, cancellationToken)
            : ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed(
                subject.IsGone ? Gone : $"'{actionId}' does not apply to this selection."));
    }

    /// <inheritdoc />
    protected override ContextValidationResult Validate(string actionId, string value) =>
        HandWrittenWardleyActions.All.Named(actionId)?.Validate(value) ?? ContextValidationResult.Accepted;

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, WardleyMap model, string actionId, string value)
    {
        var subject = Subject(target, model);
        return HandWrittenWardleyActions.All.Applying(actionId, subject)?.Command(subject, value);
    }

    private WardleySubject Subject(ContextTarget target, WardleyMap model) =>
        WardleySubject.Of(target.ResolvedFullPath, model, documents, target.ElementId);
}

/// <summary>
/// What the property grid shows for a selected Wardley element, and what happens when one of
/// those values is edited (Requirement 15).
/// </summary>
/// <remarks>
/// <para>
/// The numbers behind a position, readable and correctable without a text editor. Typing
/// <c>0.42</c> into the Maturity row and dragging the component to the same place are the same
/// command and the same undo entry (Requirement 15.7) - a user who prefers a grid and one who
/// prefers a mouse are not using two different features.
/// </para>
/// <para>
/// <b>An absent property is not contributed at all</b>, and an empty one is contributed empty
/// (Requirement 15.6). That distinction is the difference between "this component has no
/// <c>evolve</c>" and "this component has an empty one", and the contract preserves it
/// deliberately - so a row that is not here means the document does not say it.
/// </para>
/// <para>
/// On the shared frame (code-structure FR-034). The rows that are an action's - the name, the
/// evolution target, inertia - make their command through that action's record in
/// <see cref="WardleyActions"/>, so a row and a menu entry cannot mean two different things.
/// </para>
/// <para>
/// <b>Every read-only reason is a full sentence, and none is blank.</b> Both
/// <see cref="ContextPropertyDefinition.IsEditable"/> and the client's row test the reason's
/// LENGTH, so a reason of <c>" "</c> would render the row editable and let the write through.
/// The reason is also user-facing error text: <c>ContextPropertyResolver</c> returns it verbatim
/// when it refuses a write, so it has to read as both a label and a refusal.
/// </para>
/// </remarks>
public sealed class HandWrittenWardleyPropertyProvider(IHistoryStackStore historyStacks, IWardleyDocumentStore documents)
    : DiagramPropertyProvider<LineDocument, WardleyMap>(historyStacks, documents)
{
    private const string UrlPropertyId = WardleyContextPropertyProvider.UrlPropertyId;

    private const string IdentityGroup = "Identity";
    private const string PositionGroup = "Position";
    private const string StrategyGroup = "Strategy";
    private const string LinksGroup = "Links";

    private const string Gone = "That element is no longer on this map.";

    /// <summary>Why the derived stage is shown but not written (Requirement 15.5).</summary>
    private const string StageReason =
        "The evolution stage is derived from the component's maturity, so it is not set directly. Change Maturity instead.";

    private const string KindReason =
        "The kind is which statement declares the element, so changing it is a different statement rather than a different value. Remove it and add the kind you want.";

    private const string LabelOffsetReason =
        "A label offset is in pixels rather than on the map's scale, which is a property of the format ADP reproduces rather than edits. Change it where the map was written.";

    private const string ChildNameReason =
        "A pipeline component's name is also the only handle the map has on it, and nothing renames one yet. Remove it from the pipeline and add it under the name you want.";

    private const string LinkEndReason =
        "The ends of a link are what it is. Remove this link and draw the one you meant.";

    private const string LinkKindReason =
        "A dependency and a flow are different claims, written with different arrows. Remove this link and draw the other kind.";

    private const string UrlReason =
        "A url points at a definition elsewhere in the document, and nothing edits those here yet. Change it where the map was written.";

    private const string ChildrenReason =
        "A pipeline's components are listed here for reading; each one is edited by selecting it.";

    private const string ChildVisibilityReason =
        "A pipeline child takes its visibility from its parent, so it is not its own. Change it on '{0}'.";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <summary>An element of a map of this module's; the map itself has no rows.</summary>
    protected override bool Serves(ContextTarget target) =>
        base.Serves(target) && target.ElementId.Length > 0;

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(ContextTarget target, DocumentEntry<LineDocument, WardleyMap> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        var subject = Subject(target, entry.Model);
        if (subject.Entry is not { } identity)
        {
            return [];
        }

        var map = subject.Map;
        var rows = identity.Kind switch
        {
            WardleyIdentityKind.Component => ForComponent(map, identity),
            WardleyIdentityKind.PipelineChild => ForPipelineChild(map, identity),
            WardleyIdentityKind.Link => ForLink(map, identity),
            WardleyIdentityKind.Pipeline => ForPipeline(map, identity),
            _ => [],
        };

        // Requirement 15.11: in read-only mode every property is still contributed - the panel
        // must answer what the element IS - but nothing is editable, and each row says why.
        // Kept short and uniform, because otherwise every row repeats a paragraph.
        return WardleyEditability.Editable(target.ResolvedFullPath)
            ? rows
            : rows.Select(row => row with { ReadOnlyReason = WardleyEditability.Reason }).ToArray();
    }

    /// <inheritdoc />
    protected override DiagramPropertyChange Change(ContextTarget target, WardleyMap model, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return DiagramPropertyChange.Refused("That element is not on a Wardley map.");
        }

        if (!WardleyEditability.Editable(target.ResolvedFullPath))
        {
            return DiagramPropertyChange.Refused(WardleyEditability.Reason);
        }

        var subject = Subject(target, model);
        if (subject.Entry is not { } entry)
        {
            return DiagramPropertyChange.Refused(Gone);
        }

        var bodyPath = subject.BodyPath;
        var component = subject.Component;
        var child = subject.Child;

        switch (propertyId)
        {
            case NamePropertyId when component is not null:
                return Run(RenameActionId, subject, value);

            case VisibilityPropertyId when component is not null:
                return TryCoordinate(value, out var visibility, out var visibilityError)
                    ? DiagramPropertyChange.By(new MoveWardleyElementCommand(bodyPath, entry.Id, visibility, component.Position.Maturity))
                    : DiagramPropertyChange.Refused(visibilityError);

            case MaturityPropertyId when component is not null:
                return TryCoordinate(value, out var maturity, out var maturityError)
                    ? DiagramPropertyChange.By(new MoveWardleyElementCommand(bodyPath, entry.Id, component.Position.Visibility, maturity))
                    : DiagramPropertyChange.Refused(maturityError);

            case MaturityPropertyId when child is not null:
                // A child's visibility is its parent's, so only this half of the pair moves
                // (Requirement 5.4); the move command already knows that.
                return TryCoordinate(value, out var childMaturity, out var childError)
                    ? DiagramPropertyChange.By(new MoveWardleyElementCommand(bodyPath, entry.Id, 0d, childMaturity))
                    : DiagramPropertyChange.Refused(childError);

            // Emptying the row clears the statement, which is the row's own way of saying "this
            // is no longer heading anywhere".
            case EvolvePropertyId when component is not null:
                return string.IsNullOrWhiteSpace(value)
                    ? Run(ClearEvolveActionId, subject, "")
                    : Run(SetEvolveActionId, subject, value);

            case EvolveNamePropertyId when component is not null:
                return subject.Evolve is { } evolve
                    ? DiagramPropertyChange.By(new SetWardleyEvolveCommand(bodyPath, entry.Id, Present: true, evolve.Maturity, value.Trim()))
                    : DiagramPropertyChange.Refused($"'{component.Name}' is not evolving, so it has no name to arrive under.");

            case InertiaPropertyId when component is not null:
                return Run(ToggleInertiaActionId, subject, value.Length == 0 ? "false" : value);

            case DecoratorsPropertyId when component is not null:
                {
                    var decorators = WardleyDecorators.Parse(value, out var unknown);
                    return decorators is null
                        ? DiagramPropertyChange.Refused($"'{unknown}' is not one of this notation's decorators. They are: {WardleyDecorators.Vocabulary}.")
                        : DiagramPropertyChange.By(new SetWardleyDecoratorsCommand(bodyPath, entry.Id, decorators));
                }

            case LinkContextPropertyId:
                return LinkOf(subject) is { } link
                    ? DiagramPropertyChange.By(new SetWardleyLinkCommand(bodyPath, subject.IdOf(link.Source), subject.IdOf(link.Target), link.Kind, Present: true, value.Trim()))
                    : DiagramPropertyChange.Refused(Gone);

            default:
                return DiagramPropertyChange.Refused(ReasonFor(propertyId));
        }

        static WardleyLink? LinkOf(WardleySubject subject) =>
            WardleyIdentityKeys.Find(subject.Map.Links, WardleyIdentityKeys.Of, subject.Entry!.Key);
    }

    /// <summary>The change the action <paramref name="actionId"/> makes of <paramref name="value"/>, refused as the action refuses it.</summary>
    private static DiagramPropertyChange Run(string actionId, WardleySubject subject, string value)
    {
        var action = HandWrittenWardleyActions.All.Applying(actionId, subject);
        if (action is null)
        {
            return DiagramPropertyChange.Refused(Gone);
        }

        if (action.Validate(value) is { Valid: false } refused)
        {
            return DiagramPropertyChange.Refused(refused.Reason);
        }

        return action.Command(subject, value) is { } command
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused(Gone);
    }

    private WardleySubject Subject(ContextTarget target, WardleyMap model) =>
        WardleySubject.Of(target.ResolvedFullPath, model, documents, target.ElementId);

    // ---- what is shown ------------------------------------------------------------------------

    private static IReadOnlyList<ContextPropertyDefinition> ForComponent(WardleyMap map, WardleyIdentityEntry entry)
    {
        var component = WardleyIdentityKeys.Component(map, entry.Key);
        if (component is null)
        {
            return [];
        }

        // A later holder of a repeated name has no evolution: an `evolve` naming it attaches to
        // the first holder (ruling 3).
        var stage = WardleyEvolution.StageOf(component.Position.Maturity);
        var evolve = WardleyIdentityKeys.IsFirstHolder(map, component)
            ? map.Evolves.FirstOrDefault(candidate => candidate.Name == component.Name)
            : null;

        var rows = new List<ContextPropertyDefinition>
        {
            new(NamePropertyId, "Name", component.Name, Group: IdentityGroup),

            // Kind and decorators are separate rows because they are separate things in the
            // file: a market is a component that carries `(market)`, not a fourth kind
            // (Requirement 15.2).
            new(KindPropertyId, "Kind", Spell(component.Kind), ReadOnlyReason: KindReason, Group: IdentityGroup),

            new(VisibilityPropertyId, "Visibility", DslNumber.Format(component.Position.Visibility), Group: PositionGroup),
            new(MaturityPropertyId, "Maturity", DslNumber.Format(component.Position.Maturity), Group: PositionGroup),
            new(StagePropertyId, "Evolution stage", stage.Label, ReadOnlyReason: StageReason, Group: PositionGroup),
        };

        // Absent, not empty: a component with no label offset has no such row at all
        // (Requirement 15.6).
        if (component.LabelOffset is { } offset)
        {
            rows.Add(new ContextPropertyDefinition(
                LabelOffsetPropertyId,
                "Label offset",
                $"{DslNumber.Format(offset.X)}, {DslNumber.Format(offset.Y)}",
                ReadOnlyReason: LabelOffsetReason,
                Group: PositionGroup));
        }

        if (evolve is not null)
        {
            rows.Add(new ContextPropertyDefinition(EvolvePropertyId, "Evolves to", DslNumber.Format(evolve.Maturity), Group: StrategyGroup));

            if (evolve.Override.Length > 0)
            {
                rows.Add(new ContextPropertyDefinition(EvolveNamePropertyId, "Becomes", evolve.Override, Group: StrategyGroup));
            }
        }

        // Inertia is the one row that is always here even when the document says nothing: it is
        // a boolean the format expresses by the presence of a word, so "absent" IS false, and a
        // toggle with no off state could never be turned off.
        rows.Add(new ContextPropertyDefinition(
            InertiaPropertyId,
            "Inertia",
            component.Inertia ? "true" : "false",
            ContextPropertyEditor.Toggle,
            Group: StrategyGroup));

        rows.Add(new ContextPropertyDefinition(
            DecoratorsPropertyId,
            "Decorators",
            WardleyDecorators.Join(component.Decorators),
            Group: StrategyGroup));

        if (component.Url.Length > 0)
        {
            var address = map.Urls.FirstOrDefault(url => url.Name == component.Url);
            rows.Add(new ContextPropertyDefinition(
                UrlPropertyId,
                component.Kind == WardleyElementKind.Submap ? "Opens" : "Url",
                address is null ? component.Url : $"{component.Url} ({address.Address})",
                ReadOnlyReason: UrlReason,
                Group: LinksGroup));
        }

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> ForPipelineChild(WardleyMap map, WardleyIdentityEntry entry)
    {
        var found = FindChild(map, entry);
        if (found is null)
        {
            return [];
        }

        (WardleyPipeline pipeline, WardleyPipelineChild child) = found.Value;
        var parent = map.Components.FirstOrDefault(candidate => candidate.Name == pipeline.Parent);
        var stage = WardleyEvolution.StageOf(child.Maturity);

        return
        [
            new ContextPropertyDefinition(NamePropertyId, "Name", child.Name, ReadOnlyReason: ChildNameReason, Group: IdentityGroup),
            new ContextPropertyDefinition(KindPropertyId, "Kind", "pipeline component", ReadOnlyReason: KindReason, Group: IdentityGroup),

            // Shown as the parent's, with a reason saying so - rather than as a value of its own
            // that a user could type into and watch do nothing (Requirement 15.4).
            new ContextPropertyDefinition(
                VisibilityPropertyId,
                "Visibility",
                parent is null ? "" : DslNumber.Format(parent.Position.Visibility),
                ReadOnlyReason: string.Format(CultureInfo.InvariantCulture, ChildVisibilityReason, pipeline.Parent),
                Group: PositionGroup),

            new ContextPropertyDefinition(MaturityPropertyId, "Maturity", DslNumber.Format(child.Maturity), Group: PositionGroup),
            new ContextPropertyDefinition(StagePropertyId, "Evolution stage", stage.Label, ReadOnlyReason: StageReason, Group: PositionGroup),
        ];
    }

    private static IReadOnlyList<ContextPropertyDefinition> ForLink(WardleyMap map, WardleyIdentityEntry entry)
    {
        var link = WardleyIdentityKeys.Find(map.Links, WardleyIdentityKeys.Of, entry.Key);
        if (link is null)
        {
            return [];
        }

        var rows = new List<ContextPropertyDefinition>
        {
            new(LinkSourcePropertyId, "From", link.Source, ReadOnlyReason: LinkEndReason, Group: IdentityGroup),
            new(LinkTargetPropertyId, "To", link.Target, ReadOnlyReason: LinkEndReason, Group: IdentityGroup),
            new(
                LinkKindPropertyId,
                "Kind",
                link.Kind == WardleyLinkKind.Flow ? "flow" : "dependency",
                ReadOnlyReason: LinkKindReason,
                Group: IdentityGroup),
        };

        // Contributed only when the document gives one - and then editable, because the context
        // is the author's own words about why the link is there (Requirement 15.5).
        if (link.Context.Length > 0)
        {
            rows.Add(new ContextPropertyDefinition(LinkContextPropertyId, "Context", link.Context, Group: IdentityGroup));
        }

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> ForPipeline(WardleyMap map, WardleyIdentityEntry entry)
    {
        var pipeline = WardleyIdentityKeys.Find(map.Pipelines, WardleyIdentityKeys.Of, entry.Key);
        if (pipeline is null)
        {
            return [];
        }

        var parent = map.Components.FirstOrDefault(candidate => candidate.Name == pipeline.Parent);
        var rows = new List<ContextPropertyDefinition>
        {
            new(NamePropertyId, "Name", pipeline.Parent, ReadOnlyReason: KindReason, Group: IdentityGroup),
        };

        if (parent is not null)
        {
            rows.Add(new ContextPropertyDefinition(
                VisibilityPropertyId,
                "Visibility",
                DslNumber.Format(parent.Position.Visibility),
                ReadOnlyReason: string.Format(CultureInfo.InvariantCulture, ChildVisibilityReason, pipeline.Parent),
                Group: PositionGroup));
        }

        rows.Add(new ContextPropertyDefinition(
            PipelineChildrenPropertyId,
            "Components",
            string.Join(", ", pipeline.Children.Select(child => $"{child.Name} ({DslNumber.Format(child.Maturity)})")),
            ContextPropertyEditor.Text,
            ChildrenReason,
            PositionGroup));

        return rows;
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private static (WardleyPipeline Pipeline, WardleyPipelineChild Child)? FindChild(WardleyMap map, WardleyIdentityEntry entry)
    {
        return entry.Kind == WardleyIdentityKind.PipelineChild
            ? WardleyIdentityKeys.Child(map, entry.Key)
            : null;
    }

    /// <summary>
    /// A coordinate the user typed. Refused rather than clamped, unlike a drag: a drag past the
    /// edge is a slip of the hand, a typed 1.4 is a misunderstanding of the scale.
    /// </summary>
    private static bool TryCoordinate(string value, out double result, out string error)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            error = "This is a number between 0 and 1, where 0 is the invisible end and 1 the user need.";
            return false;
        }

        if (result is < 0d or > 1d)
        {
            error = "Both of a Wardley map's axes run from 0 to 1, so a position outside that is off the map.";
            result = 0d;
            return false;
        }

        error = "";
        return true;
    }

    /// <summary>
    /// Why a write to a property this provider does not accept was refused. The same sentence
    /// the row already carries, so the panel and the refusal say the same thing.
    /// </summary>
    private static string ReasonFor(string propertyId) => propertyId switch
    {
        StagePropertyId => StageReason,
        KindPropertyId => KindReason,
        LabelOffsetPropertyId => LabelOffsetReason,
        LinkSourcePropertyId or LinkTargetPropertyId => LinkEndReason,
        LinkKindPropertyId => LinkKindReason,
        UrlPropertyId => UrlReason,
        PipelineChildrenPropertyId => ChildrenReason,
        VisibilityPropertyId => "A pipeline child takes its visibility from its parent, so it is not its own. Change it on the parent.",
        _ => $"'{propertyId}' is not a property of this element.",
    };

    private static string Spell(WardleyElementKind kind) => kind switch
    {
        WardleyElementKind.Anchor => "anchor",
        WardleyElementKind.Submap => "submap",
        _ => "component",
    };
}

/// <summary>
/// What a Wardley map offers in the Toolbox: the statements a user writes rather than derives
/// (Requirement 13). Described by the backend as data - the client renders a palette it does not
/// interpret, exactly as it renders context actions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Market and Ecosystem are components, not kinds.</b> The DSL has three statement kinds -
/// `component`, `anchor`, `submap` - and five decorators. A market IS a component carrying
/// `(market)`, so its entry drops exactly that (Requirement 13.2). Offering it as a kind of its
/// own would be a palette that creates something the file cannot express.
/// </para>
/// <para>
/// <b>There is deliberately no Link entry.</b> A link needs two endpoints and a drop has one, so
/// dropping a link would always leave half of it unsaid. It is created through the context
/// action instead - `wardley.link` for a dependency, `wardley.flow` for a flow - which is what
/// Requirement 13.6 asks a type in this position to say out loud.
/// </para>
/// <para>
/// <b>Pipeline drops onto a component</b>, because that is what a pipeline belongs to: its
/// entry reuses the same action the menu's "Start a pipeline…" uses, which asks for the first
/// component to put in it.
/// </para>
/// <para>
/// The Toolbox panel renders this now, and the canvas registers it while a map is open. The
/// provider stays as Requirement 13.7 asked - <see cref="Items"/> static and assertable
/// without a UI - and the two criteria that need contract fields that do not exist (13.4's
/// drop position, 13.5's unavailable entries) remain reported as gaps rather than implemented
/// against a contract that has no field for either.
/// </para>
/// </remarks>
public sealed class HandWrittenWardleyToolbox : IDiagramToolboxProvider
{
    public ToolOrigin Origin => Diagram.WardleyMap.Origin;

    /// <summary>
    /// The entries, in the order the palette shows them: the two that make a value chain first,
    /// then the two decorated components, then the structures, then the two kinds of writing.
    /// </summary>
    public IReadOnlyList<ToolboxItemDefinition> Items { get; } =
    [
        new(
            "wardley.toolbox.component",
            "Component",
            "mdi-shape-outline",
            "Something the value chain is made of. Where you put it is what you are claiming about it.",
            AddComponentActionId),
        new(
            "wardley.toolbox.anchor",
            "Anchor",
            "mdi-anchor",
            "The user need the whole chain hangs from. A map usually has one.",
            AddAnchorActionId),
        new(
            "wardley.toolbox.market",
            "Market",
            "mdi-store-outline",
            "A component supplied by a market rather than by one supplier.",
            AddMarketActionId),
        new(
            "wardley.toolbox.ecosystem",
            "Ecosystem",
            "mdi-graph-outline",
            "A component with an ecosystem of others built around it.",
            AddEcosystemActionId),
        new(
            "wardley.toolbox.submap",
            "Submap",
            "mdi-map-outline",
            "A reference to another map, which activating opens.",
            AddSubmapActionId),
        new(
            "wardley.toolbox.pipeline",
            "Pipeline",
            "mdi-pipe",
            "A set of choices behind one component. Drop it on the component it belongs to.",
            AddToPipelineActionId),
        new(
            "wardley.toolbox.note",
            "Note",
            "mdi-note-outline",
            "Free text pinned to a place on the map.",
            AddNoteActionId),
        new(
            "wardley.toolbox.annotation",
            "Annotation",
            "mdi-comment-text-outline",
            "A numbered remark, shown on the map and listed in its own block.",
            AddAnnotationActionId),
    ];
}
