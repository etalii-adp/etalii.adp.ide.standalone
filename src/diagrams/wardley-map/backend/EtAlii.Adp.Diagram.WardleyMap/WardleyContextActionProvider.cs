using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What a user can do to a Wardley element, offered the way every other action is - so it
/// reaches the ribbon, the right-click menu and the keyboard through one path, and the client
/// holds no key-to-action table of its own (Requirements 11.4, 11.5).
/// </summary>
/// <remarks>
/// <para>
/// Every action here dispatches one of the module's commands through the project's history, so
/// the menu, the canvas and the property grid are three routes to one implementation with one
/// inverse (Requirement 9.8). The provider never touches the document.
/// </para>
/// <para>
/// An action that would fail is not offered (Requirement 11.6). That is the whole reason
/// <see cref="DiscoverAsync"/> reads the map rather than answering from a fixed list: an unlink
/// on an element with no link, a pipeline child on a legacy pipeline and a "stop evolving" on
/// something that is not evolving are all decided by what the document currently says.
/// </para>
/// </remarks>
public sealed class WardleyContextActionProvider : IContextActionProvider
{
    public const string AddComponentActionId = "wardley.add-component";
    public const string AddAnchorActionId = "wardley.add-anchor";
    public const string AddSubmapActionId = "wardley.add-submap";
    public const string AddMarketActionId = "wardley.add-market";
    public const string AddEcosystemActionId = "wardley.add-ecosystem";
    public const string AddNoteActionId = "wardley.add-note";
    public const string AddAnnotationActionId = "wardley.add-annotation";
    public const string RenameActionId = "wardley.rename";
    public const string RemoveActionId = "wardley.remove";
    public const string SetEvolveActionId = "wardley.set-evolve";
    public const string ClearEvolveActionId = "wardley.clear-evolve";
    public const string ToggleInertiaActionId = "wardley.toggle-inertia";
    public const string LinkActionId = "wardley.link";
    public const string FlowActionId = "wardley.flow";
    public const string UnlinkActionId = "wardley.unlink";
    public const string AddToPipelineActionId = "wardley.add-to-pipeline";
    public const string RemoveFromPipelineActionId = "wardley.remove-from-pipeline";

    /// <summary>The toggle for one of the five decorators - `wardley.toggle-buy` and its kin.</summary>
    public static string DecoratorActionIdFor(WardleyDecorator decorator) =>
        $"wardley.toggle-{decorator.ToString().ToLowerInvariant()}";

    /// <summary>The add action for one statement kind, which the toolbox names for its drops.</summary>
    public static string AddActionIdFor(WardleyElementKind kind) => kind switch
    {
        WardleyElementKind.Anchor => AddAnchorActionId,
        WardleyElementKind.Submap => AddSubmapActionId,
        _ => AddComponentActionId,
    };

    private const string Gone = "That element is no longer on this map.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IWardleyDocumentStore _documents;

    public WardleyContextActionProvider(IHistoryStackStore historyStacks, IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(
        ContextTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // Every provider in this scope is consulted for every element in it, including elements
        // of other diagram types - so the first question is whether this file is one of ours.
        // Reading it before asking would have this module parsing another notation's document.
        if (target.Scope != ContextScope.DiagramElement || !Diagram.IsBody(target.ResolvedFullPath))
        {
            return Empty();
        }

        if (!WardleyEditability.Editable(target.ResolvedFullPath))
        {
            // Nothing here can be done to a map that cannot be written, and Requirement 11.6
            // says an action that would fail is withheld rather than offered and refused.
            return Empty();
        }

        var map = WardleyParser.Parse(_documents.GetOrLoad(target.ResolvedFullPath));

        // No element: the map itself, where the only things to do are the adds.
        if (target.ElementId.Length == 0)
        {
            return Groups(new ContextActionGroupDefinition(AddActions()));
        }

        var entry = _documents
            .Identities(target.ResolvedFullPath)
            .FirstOrDefault(candidate => candidate.Id == target.ElementId);

        if (entry is null)
        {
            return Empty();
        }

        return entry.Kind switch
        {
            WardleyIdentityKind.Component => ForComponent(map, entry),
            WardleyIdentityKind.PipelineChild => ForPipelineChild(map, entry),
            WardleyIdentityKind.Link => ForLink(map, entry),

            // A note, an annotation, an accelerator or an attitude region: this module has no
            // command that edits one yet, and offering an action nothing implements would be
            // exactly the disagreement between menu and outcome Requirement 11.6 forbids.
            _ => Empty(),
        };
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(
        ContextTarget target,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (IsAdd(actionId))
        {
            // Asked before anything is written, so the element arrives named rather than as
            // "New component" for the user to find and rename.
            return Result(new ContextExecutionRequiresInput(new ContextInputRequest(
                $"Add {SpellAdd(actionId)}", "mdi-plus", AddFieldLabel(actionId), "", "Add")));
        }

        var map = WardleyParser.Parse(_documents.GetOrLoad(target.ResolvedFullPath));
        var entry = _documents
            .Identities(target.ResolvedFullPath)
            .FirstOrDefault(candidate => candidate.Id == target.ElementId);

        if (entry is null)
        {
            return Result(new ContextExecutionFailed(Gone));
        }

        var component = ComponentOf(map, entry);

        switch (actionId)
        {
            case RenameActionId when component is not null:
                return Result(new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename element", "mdi-pencil-outline", "Name", component.Name, "Rename", target.ElementId)));

            case RemoveActionId when component is not null:
                // Confirmed, because removing an element takes its links and its `evolve` with
                // it - more than the one statement the user is looking at.
                return Result(new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove element",
                    "mdi-trash-can-outline",
                    $"Remove '{component.Name}', and every link and evolution that names it?",
                    "Remove",
                    Danger: true)));

            case SetEvolveActionId when component is not null:
                {
                    var evolve = map.Evolves.FirstOrDefault(candidate => candidate.Name == component.Name);
                    var initial = Number(evolve?.Maturity ?? component.Position.Maturity);
                    return Result(new ContextExecutionRequiresInput(new ContextInputRequest(
                        "Evolve element",
                        "mdi-arrow-right-bold-outline",
                        "Target maturity (0 to 1)",
                        initial,
                        "Save")));
                }

            case LinkActionId or FlowActionId when component is not null:
                return Result(new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                    actionId == FlowActionId ? "Flow to" : "Link to",
                    "mdi-arrow-right",
                    actionId == FlowActionId ? "Flow" : "Link",
                    Others(target.ResolvedFullPath, map, component),
                    "This map has no other element to link to.")));

            case UnlinkActionId:
                {
                    var links = LinksOf(map, entry, component);
                    return links.Count switch
                    {
                        0 => Result(new ContextExecutionFailed("This element has no link to remove.")),

                        // One link is not a choice. Asking which of one would be a dialog that
                        // exists only to be dismissed.
                        1 => RemoveLink(target, links[0], cancellationToken),
                        _ => Result(new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                            "Remove link",
                            "mdi-link-variant-off",
                            "Remove",
                            links.Select(link => new ContextOptionNode(link.Id, link.Label, Selectable: true)).ToArray(),
                            "This element has no link to remove."))),
                    };
                }

            case AddToPipelineActionId when component is not null:
                return Result(new ContextExecutionRequiresInput(new ContextInputRequest(
                    $"Add to {component.Name}'s pipeline", "mdi-pipe", "Name", "", "Add")));

            case ClearEvolveActionId when component is not null:
                return Dispatch(target, new SetWardleyEvolveCommand(target.ResolvedFullPath, entry.Id, Present: false, 0d), cancellationToken);

            case ToggleInertiaActionId when component is not null:
                return Dispatch(target, new SetWardleyInertiaCommand(target.ResolvedFullPath, entry.Id, !component.Inertia), cancellationToken);

            case RemoveFromPipelineActionId when entry.Kind == WardleyIdentityKind.PipelineChild:
                {
                    var found = FindChild(map, entry);
                    return found is null
                        ? Result(new ContextExecutionFailed(Gone))
                        : Dispatch(
                            target,
                            new SetWardleyPipelineMembershipCommand(
                                target.ResolvedFullPath, IdOfParent(target, found.Value.Pipeline), found.Value.Child.Name, Member: false),
                            cancellationToken);
                }

            default:
                if (component is not null && DecoratorOf(actionId) is { } decorator)
                {
                    var present = component.Decorators.Contains(decorator);
                    return Dispatch(
                        target,
                        new SetWardleyDecoratorCommand(target.ResolvedFullPath, entry.Id, decorator, !present),
                        cancellationToken);
                }

                return Result(new ContextExecutionFailed($"'{actionId}' does not apply to this selection."));
        }
    }

    /// <summary>
    /// Judges the typed value as it is typed: a name that is blank, and an evolution target that
    /// is not a number between 0 and 1.
    /// </summary>
    /// <remarks>
    /// The maturity is refused rather than clamped here, unlike a drag: a drag past the edge is
    /// a slip of the hand, while a typed `1.4` is a statement about a scale the user has
    /// misunderstood, and saying so is more use than silently storing something else.
    /// </remarks>
    public ValueTask<ContextValidationResult> ValidateAsync(
        ContextTarget target,
        string actionId,
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = target;

        if (actionId == SetEvolveActionId)
        {
            return ValueTask.FromResult(
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var maturity) &&
                maturity is >= 0d and <= 1d
                    ? ContextValidationResult.Accepted
                    : ContextValidationResult.Rejected("An evolution target is a number between 0 and 1."));
        }

        if ((IsAdd(actionId) || actionId == RenameActionId || actionId == AddToPipelineActionId) &&
            string.IsNullOrWhiteSpace(value))
        {
            // A note and an annotation carry text rather than a name, and telling someone their
            // note needs a name is telling them the wrong thing.
            return ValueTask.FromResult(ContextValidationResult.Rejected(
                actionId is AddNoteActionId or AddAnnotationActionId
                    ? "A note needs something to say."
                    : "Every element needs a name."));
        }

        return ValueTask.FromResult(ContextValidationResult.Accepted);
    }

    public async ValueTask<ContextCommitResult> CommitAsync(
        ContextTarget target,
        string actionId,
        string value,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = text;

        var bodyPath = target.ResolvedFullPath;
        if (IsAdd(actionId))
        {
            // Placed in the middle of the map. Requirement 13.4 asks for a dropped element to
            // land where it was dropped - the one notation where a position is a claim rather
            // than a convenience - but nothing carries a drop position to a provider:
            // `ExecuteActionRequest` has no such field and `ContextTarget` no such member. The
            // gap is reported rather than papered over (Requirement 12.5); until it closes, an
            // added element lands in the middle and the user's next drag places it, which is at
            // least a position they chose rather than one that looks authoritative.
            return await Execute(
                target,
                AddCommandFor(actionId, bodyPath, value.Trim(), 0.5d, 0.5d),
                cancellationToken);
        }

        var map = WardleyParser.Parse(_documents.GetOrLoad(bodyPath));
        var entry = _documents.Identities(bodyPath).FirstOrDefault(candidate => candidate.Id == target.ElementId);
        if (entry is null)
        {
            return ContextCommitResult.Failed(Gone);
        }

        var component = ComponentOf(map, entry);

        switch (actionId)
        {
            case RenameActionId when component is not null:
                return await Execute(target, new RenameWardleyElementCommand(bodyPath, entry.Id, value.Trim()), cancellationToken);

            case RemoveActionId when component is not null:
                return await Execute(target, new RemoveWardleyElementCommand(bodyPath, entry.Id), cancellationToken);

            case SetEvolveActionId when component is not null:
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var maturity)
                    ? await Execute(target, new SetWardleyEvolveCommand(bodyPath, entry.Id, Present: true, maturity), cancellationToken)
                    : ContextCommitResult.Failed("An evolution target is a number between 0 and 1.");

            case LinkActionId or FlowActionId when component is not null:
                return await Execute(
                    target,
                    new SetWardleyLinkCommand(
                        bodyPath,
                        entry.Id,
                        value,
                        actionId == FlowActionId ? WardleyLinkKind.Flow : WardleyLinkKind.Dependency,
                        Present: true),
                    cancellationToken);

            case UnlinkActionId:
                {
                    var link = LinksOf(map, entry, component).FirstOrDefault(candidate => candidate.Id == value);
                    return link is null
                        ? ContextCommitResult.Failed("That link is no longer on this map.")
                        : await Execute(target, RemoveLinkCommand(bodyPath, link), cancellationToken);
                }

            case AddToPipelineActionId when component is not null:
                return await Execute(
                    target,
                    new SetWardleyPipelineMembershipCommand(bodyPath, entry.Id, value.Trim(), Member: true),
                    cancellationToken);

            default:
                return ContextCommitResult.Failed($"Unknown action '{actionId}'.");
        }
    }

    // ---- what is offered ----------------------------------------------------------------------

    private static IReadOnlyList<ContextActionDefinition> AddActions() =>
    [
        new(AddComponentActionId, "Add component…", "mdi-plus"),
        new(AddAnchorActionId, "Add anchor…", "mdi-anchor"),
        new(AddSubmapActionId, "Add submap…", "mdi-map-outline"),
        new(AddMarketActionId, "Add market…", "mdi-store-outline"),
        new(AddEcosystemActionId, "Add ecosystem…", "mdi-graph-outline"),
        new(AddNoteActionId, "Add note…", "mdi-note-outline"),
        new(AddAnnotationActionId, "Add annotation…", "mdi-comment-text-outline"),
    ];

    private ValueTask<IReadOnlyList<ContextActionGroupDefinition>> ForComponent(WardleyMap map, WardleyIdentityEntry entry)
    {
        var component = map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);
        if (component is null)
        {
            return Empty();
        }

        var identity = new ContextActionGroupDefinition([
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(RemoveActionId, "Remove", "mdi-trash-can-outline", new ContextShortcutDefinition("Delete")),
        ]);

        var evolving = map.Evolves.Any(evolve => evolve.Name == component.Name);
        var strategy = new List<ContextActionDefinition>
        {
            new(SetEvolveActionId, evolving ? "Change evolution target…" : "Evolve to…", "mdi-arrow-right-bold-outline"),
        };

        // Only offered when there is one to stop, which is the whole of Requirement 11.6.
        if (evolving)
        {
            strategy.Add(new ContextActionDefinition(ClearEvolveActionId, "Stop evolving", "mdi-arrow-right-bold-outline"));
        }

        strategy.Add(new ContextActionDefinition(
            ToggleInertiaActionId,
            component.Inertia ? "Clear inertia" : "Mark as having inertia",
            "mdi-weight"));

        var decorators = new ContextActionGroupDefinition(WardleyDecorators.All
            .Select(decorator => new ContextActionDefinition(
                DecoratorActionIdFor(decorator),
                component.Decorators.Contains(decorator)
                    ? $"Clear {WardleyDecorators.Spell(decorator)}"
                    : $"Mark as {WardleyDecorators.Spell(decorator)}",
                "mdi-tag-outline"))
            .ToArray());

        var links = new List<ContextActionDefinition>
        {
            new(LinkActionId, "Link to…", "mdi-arrow-right"),
            new(FlowActionId, "Flow to…", "mdi-transfer-right"),
        };

        if (map.Links.Any(link => link.Source == component.Name || link.Target == component.Name))
        {
            links.Add(new ContextActionDefinition(UnlinkActionId, "Remove link…", "mdi-link-variant-off"));
        }

        var pipeline = map.Pipelines.FirstOrDefault(candidate => candidate.Parent == component.Name);
        var membership = new List<ContextActionDefinition>();

        // A legacy two-coordinate pipeline holds no components, and Requirement 3.2 forbids
        // rewriting it into the form that does - so there is nothing to offer on one.
        if (pipeline is not { Form: WardleyPipelineForm.Legacy })
        {
            membership.Add(new ContextActionDefinition(
                AddToPipelineActionId,
                pipeline is null ? "Start a pipeline…" : "Add to pipeline…",
                "mdi-pipe"));
        }

        return membership.Count == 0
            ? Groups(identity, new ContextActionGroupDefinition(strategy), decorators, new ContextActionGroupDefinition(links))
            : Groups(
                identity,
                new ContextActionGroupDefinition(strategy),
                decorators,
                new ContextActionGroupDefinition(links),
                new ContextActionGroupDefinition(membership));
    }

    private ValueTask<IReadOnlyList<ContextActionGroupDefinition>> ForPipelineChild(WardleyMap map, WardleyIdentityEntry entry) =>
        FindChild(map, entry) is null
            ? Empty()
            : Groups(new ContextActionGroupDefinition([
                new(RemoveFromPipelineActionId, "Remove from pipeline", "mdi-trash-can-outline", new ContextShortcutDefinition("Delete")),
            ]));

    private ValueTask<IReadOnlyList<ContextActionGroupDefinition>> ForLink(WardleyMap map, WardleyIdentityEntry entry) =>
        map.Links.Any(link => WardleyIdentityKeys.Of(link) == entry.Key)
            ? Groups(new ContextActionGroupDefinition([
                new(UnlinkActionId, "Remove link", "mdi-link-variant-off", new ContextShortcutDefinition("Delete")),
            ]))
            : Empty();

    // ---- looking things up --------------------------------------------------------------------

    private static WardleyComponent? ComponentOf(WardleyMap map, WardleyIdentityEntry entry) =>
        entry.Kind != WardleyIdentityKind.Component
            ? null
            : map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);

    private static (WardleyPipeline Pipeline, WardleyPipelineChild Child)? FindChild(WardleyMap map, WardleyIdentityEntry entry)
    {
        foreach (var pipeline in map.Pipelines)
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

    private string IdOfParent(ContextTarget target, WardleyPipeline pipeline) =>
        _documents
            .Identities(target.ResolvedFullPath)
            .FirstOrDefault(candidate =>
                candidate.Kind == WardleyIdentityKind.Component && candidate.Key == pipeline.Parent)
            ?.Id ?? "";

    /// <summary>
    /// Every other element on the map, as the options of a "link to" dialog. Each option's id is
    /// the element id the command needs, so the dialog's answer is already what is dispatched.
    /// </summary>
    private IReadOnlyList<ContextOptionNode> Others(string bodyPath, WardleyMap map, WardleyComponent component)
    {
        var identities = _documents.Identities(bodyPath);

        return map.Components
            .Where(candidate => candidate.Name != component.Name)
            .Select(candidate => new ContextOptionNode(
                identities.FirstOrDefault(entry =>
                    entry.Kind == WardleyIdentityKind.Component && entry.Key == candidate.Name)?.Id ?? "",
                candidate.Name,
                Selectable: true))
            .Where(option => option.Id.Length > 0)
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The links one element is an end of, each labelled the way a human reads it.</summary>
    private IReadOnlyList<WardleyLinkChoice> LinksOf(WardleyMap map, WardleyIdentityEntry entry, WardleyComponent? component)
    {
        var name = component?.Name;
        var links = component is not null
            ? map.Links.Where(link => link.Source == name || link.Target == name)
            : map.Links.Where(link => WardleyIdentityKeys.Of(link) == entry.Key);

        return links
            .Select(link => new WardleyLinkChoice(
                link.Source,
                link.Target,
                link.Kind,
                link.Kind == WardleyLinkKind.Flow
                    ? $"{link.Source} +> {link.Target}"
                    : $"{link.Source} -> {link.Target}",
                $"{link.Source}{WardleyIdentityKeys.Separator}{link.Target}{WardleyIdentityKeys.Separator}{link.Kind}"))
            .ToArray();
    }

    private static string SpellAdd(string actionId) => actionId switch
    {
        AddAnchorActionId => "anchor",
        AddSubmapActionId => "submap",
        AddMarketActionId => "market",
        AddEcosystemActionId => "ecosystem",
        AddNoteActionId => "note",
        AddAnnotationActionId => "annotation",
        _ => "component",
    };

    /// <summary>What a new element of this kind is asked for: a name, or the text it carries.</summary>
    private static string AddFieldLabel(string actionId) =>
        actionId is AddNoteActionId or AddAnnotationActionId ? "Text" : "Name";

    private static bool IsAdd(string actionId) =>
        actionId is AddComponentActionId or AddAnchorActionId or AddSubmapActionId
            or AddMarketActionId or AddEcosystemActionId or AddNoteActionId or AddAnnotationActionId;

    /// <summary>The command one add action dispatches, at the position it was given.</summary>
    private static ICommand AddCommandFor(string actionId, string bodyPath, string value, double visibility, double maturity) =>
        actionId switch
        {
            AddNoteActionId => new AddWardleyNoteCommand(bodyPath, value, visibility, maturity),
            AddAnnotationActionId => new AddWardleyAnnotationCommand(bodyPath, value, visibility, maturity),

            // Market and ecosystem are components carrying a decorator, not kinds of their own
            // (Requirement 13.2) - written in one command, so one drop is one undo.
            AddMarketActionId => new AddWardleyElementCommand(bodyPath, "component", value, visibility, maturity, WardleyDecorator.Market),
            AddEcosystemActionId => new AddWardleyElementCommand(bodyPath, "component", value, visibility, maturity, WardleyDecorator.Ecosystem),
            AddAnchorActionId => new AddWardleyElementCommand(bodyPath, "anchor", value, visibility, maturity),
            AddSubmapActionId => new AddWardleyElementCommand(bodyPath, "submap", value, visibility, maturity),
            _ => new AddWardleyElementCommand(bodyPath, "component", value, visibility, maturity),
        };

    private static WardleyDecorator? DecoratorOf(string actionId)
    {
        foreach (var decorator in WardleyDecorators.All)
        {
            if (actionId == DecoratorActionIdFor(decorator))
            {
                return decorator;
            }
        }

        return null;
    }

    // ---- dispatching --------------------------------------------------------------------------

    private ValueTask<ContextExecutionResult> RemoveLink(
        ContextTarget target,
        WardleyLinkChoice link,
        CancellationToken cancellationToken) =>
        Dispatch(target, RemoveLinkCommand(target.ResolvedFullPath, link), cancellationToken);

    private ICommand RemoveLinkCommand(string bodyPath, WardleyLinkChoice link)
    {
        var identities = _documents.Identities(bodyPath);
        string IdOf(string name) => identities
            .FirstOrDefault(entry => entry.Kind == WardleyIdentityKind.Component && entry.Key == name)?.Id ?? "";

        return new SetWardleyLinkCommand(bodyPath, IdOf(link.Source), IdOf(link.Target), link.Kind, Present: false);
    }

    private async ValueTask<ContextExecutionResult> Dispatch(
        ContextTarget target,
        ICommand command,
        CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    private async ValueTask<ContextCommitResult> Execute(
        ContextTarget target,
        ICommand command,
        CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Empty() =>
        ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Groups(params ContextActionGroupDefinition[] groups) =>
        ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(groups);

    private static ValueTask<ContextExecutionResult> Result(ContextExecutionResult result) =>
        ValueTask.FromResult(result);
}
