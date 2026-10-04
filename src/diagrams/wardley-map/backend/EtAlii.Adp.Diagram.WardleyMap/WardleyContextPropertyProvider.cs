using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

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
/// <b>Every read-only reason is a full sentence, and none is blank.</b> Both
/// <see cref="ContextPropertyDefinition.IsEditable"/> and the client's row test the reason's
/// LENGTH, so a reason of <c>" "</c> would render the row editable and let the write through.
/// The reason is also user-facing error text: <c>ContextPropertyResolver</c> returns it verbatim
/// when it refuses a write, so it has to read as both a label and a refusal.
/// </para>
/// </remarks>
public sealed class WardleyContextPropertyProvider : IContextPropertyProvider
{
    public const string NamePropertyId = "wardley.name";
    public const string KindPropertyId = "wardley.kind";
    public const string VisibilityPropertyId = "wardley.visibility";
    public const string MaturityPropertyId = "wardley.maturity";
    public const string StagePropertyId = "wardley.stage";
    public const string LabelOffsetPropertyId = "wardley.label-offset";
    public const string EvolvePropertyId = "wardley.evolve";
    public const string EvolveNamePropertyId = "wardley.evolve-name";
    public const string InertiaPropertyId = "wardley.inertia";
    public const string DecoratorsPropertyId = "wardley.decorators";
    public const string UrlPropertyId = "wardley.url";

    public const string LinkSourcePropertyId = "wardley.link.source";
    public const string LinkTargetPropertyId = "wardley.link.target";
    public const string LinkKindPropertyId = "wardley.link.kind";
    public const string LinkContextPropertyId = "wardley.link.context";

    public const string PipelineChildrenPropertyId = "wardley.pipeline.children";

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

    private readonly IHistoryStackStore _historyStacks;
    private readonly IWardleyDocumentStore _documents;

    public WardleyContextPropertyProvider(IHistoryStackStore historyStacks, IWardleyDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(
        ContextTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // Every provider in this scope is consulted for every element in it, including elements
        // of other diagram types - so the first question is whether this file is one of ours.
        if (target.Scope != ContextScope.DiagramElement || target.ElementId.Length == 0 ||
            !Diagram.IsBody(target.ResolvedFullPath))
        {
            return None();
        }

        var map = WardleyParser.Parse(_documents.GetOrLoad(target.ResolvedFullPath));
        var entry = _documents
            .Identities(target.ResolvedFullPath)
            .FirstOrDefault(candidate => candidate.Id == target.ElementId);

        if (entry is null)
        {
            return None();
        }

        var rows = entry.Kind switch
        {
            WardleyIdentityKind.Component => ForComponent(map, entry),
            WardleyIdentityKind.PipelineChild => ForPipelineChild(map, entry),
            WardleyIdentityKind.Link => ForLink(map, entry),
            WardleyIdentityKind.Pipeline => ForPipeline(map, entry),
            _ => [],
        };

        // Requirement 15.11: in read-only mode every property is still contributed - the panel
        // must answer what the element IS - but nothing is editable, and each row says why.
        // Kept short and uniform, because otherwise every row repeats a paragraph.
        return WardleyEditability.Editable(target.ResolvedFullPath)
            ? Some(rows)
            : Some(rows.Select(row => row with { ReadOnlyReason = WardleyEditability.Reason }).ToArray());
    }

    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return ContextPropertyResult.Failure("That element is not on a Wardley map.");
        }

        if (!WardleyEditability.Editable(target.ResolvedFullPath))
        {
            return ContextPropertyResult.Failure(WardleyEditability.Reason);
        }

        var bodyPath = target.ResolvedFullPath;
        var map = WardleyParser.Parse(_documents.GetOrLoad(bodyPath));
        var entry = _documents.Identities(bodyPath).FirstOrDefault(candidate => candidate.Id == target.ElementId);
        if (entry is null)
        {
            return ContextPropertyResult.Failure(Gone);
        }

        var component = ComponentOf(map, entry);
        var child = FindChild(map, entry);

        switch (propertyId)
        {
            case NamePropertyId when component is not null:
                return await Dispatch(target, new RenameWardleyElementCommand(bodyPath, entry.Id, value.Trim()), cancellationToken);

            case VisibilityPropertyId when component is not null:
                return TryCoordinate(value, out var visibility, out var visibilityError)
                    ? await Dispatch(target, new MoveWardleyElementCommand(bodyPath, entry.Id, visibility, component.Position.Maturity), cancellationToken)
                    : ContextPropertyResult.Failure(visibilityError);

            case MaturityPropertyId when component is not null:
                return TryCoordinate(value, out var maturity, out var maturityError)
                    ? await Dispatch(target, new MoveWardleyElementCommand(bodyPath, entry.Id, component.Position.Visibility, maturity), cancellationToken)
                    : ContextPropertyResult.Failure(maturityError);

            case MaturityPropertyId when child is not null:
                // A child's visibility is its parent's, so only this half of the pair moves
                // (Requirement 5.4); the move command already knows that.
                return TryCoordinate(value, out var childMaturity, out var childError)
                    ? await Dispatch(target, new MoveWardleyElementCommand(bodyPath, entry.Id, 0d, childMaturity), cancellationToken)
                    : ContextPropertyResult.Failure(childError);

            case EvolvePropertyId when component is not null:
                {
                    var evolve = map.Evolves.FirstOrDefault(candidate => candidate.Name == component.Name);

                    // Emptying the row clears the statement, which is the row's own way of
                    // saying "this is no longer heading anywhere".
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return await Dispatch(target, new SetWardleyEvolveCommand(bodyPath, entry.Id, Present: false, 0d), cancellationToken);
                    }

                    return TryCoordinate(value, out var target_, out var evolveError)
                        ? await Dispatch(target, new SetWardleyEvolveCommand(bodyPath, entry.Id, Present: true, target_, evolve?.Override ?? ""), cancellationToken)
                        : ContextPropertyResult.Failure(evolveError);
                }

            case EvolveNamePropertyId when component is not null:
                {
                    var evolve = map.Evolves.FirstOrDefault(candidate => candidate.Name == component.Name);
                    return evolve is null
                        ? ContextPropertyResult.Failure($"'{component.Name}' is not evolving, so it has no name to arrive under.")
                        : await Dispatch(target, new SetWardleyEvolveCommand(bodyPath, entry.Id, Present: true, evolve.Maturity, value.Trim()), cancellationToken);
                }

            case InertiaPropertyId when component is not null:
                return await Dispatch(
                    target,
                    new SetWardleyInertiaCommand(bodyPath, entry.Id, string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)),
                    cancellationToken);

            case DecoratorsPropertyId when component is not null:
                {
                    var decorators = WardleyDecorators.Parse(value, out var unknown);
                    return decorators is null
                        ? ContextPropertyResult.Failure($"'{unknown}' is not one of this notation's decorators. They are: {WardleyDecorators.Vocabulary}.")
                        : await Dispatch(target, new SetWardleyDecoratorsCommand(bodyPath, entry.Id, decorators), cancellationToken);
                }

            case LinkContextPropertyId:
                {
                    var link = map.Links.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);
                    if (link is null)
                    {
                        return ContextPropertyResult.Failure(Gone);
                    }

                    var identities = _documents.Identities(bodyPath);
                    string IdOf(string name) => identities
                        .FirstOrDefault(candidate => candidate.Kind == WardleyIdentityKind.Component && candidate.Key == name)?.Id ?? "";

                    return await Dispatch(
                        target,
                        new SetWardleyLinkCommand(bodyPath, IdOf(link.Source), IdOf(link.Target), link.Kind, Present: true, value.Trim()),
                        cancellationToken);
                }

            default:
                return ContextPropertyResult.Failure(ReasonFor(propertyId));
        }
    }

    // ---- what is shown ------------------------------------------------------------------------

    private static IReadOnlyList<ContextPropertyDefinition> ForComponent(WardleyMap map, WardleyIdentityEntry entry)
    {
        var component = map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);
        if (component is null)
        {
            return [];
        }

        var stage = WardleyEvolution.StageOf(component.Position.Maturity);
        var evolve = map.Evolves.FirstOrDefault(candidate => candidate.Name == component.Name);

        var rows = new List<ContextPropertyDefinition>
        {
            new(NamePropertyId, "Name", component.Name, Group: IdentityGroup),

            // Kind and decorators are separate rows because they are separate things in the
            // file: a market is a component that carries `(market)`, not a fourth kind
            // (Requirement 15.2).
            new(KindPropertyId, "Kind", Spell(component.Kind), ReadOnlyReason: KindReason, Group: IdentityGroup),

            new(VisibilityPropertyId, "Visibility", Number(component.Position.Visibility), Group: PositionGroup),
            new(MaturityPropertyId, "Maturity", Number(component.Position.Maturity), Group: PositionGroup),
            new(StagePropertyId, "Evolution stage", stage.Label, ReadOnlyReason: StageReason, Group: PositionGroup),
        };

        // Absent, not empty: a component with no label offset has no such row at all
        // (Requirement 15.6).
        if (component.LabelOffset is { } offset)
        {
            rows.Add(new ContextPropertyDefinition(
                LabelOffsetPropertyId,
                "Label offset",
                $"{Number(offset.X)}, {Number(offset.Y)}",
                ReadOnlyReason: LabelOffsetReason,
                Group: PositionGroup));
        }

        if (evolve is not null)
        {
            rows.Add(new ContextPropertyDefinition(EvolvePropertyId, "Evolves to", Number(evolve.Maturity), Group: StrategyGroup));

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
            new(NamePropertyId, "Name", child.Name, ReadOnlyReason: ChildNameReason, Group: IdentityGroup),
            new(KindPropertyId, "Kind", "pipeline component", ReadOnlyReason: KindReason, Group: IdentityGroup),

            // Shown as the parent's, with a reason saying so - rather than as a value of its own
            // that a user could type into and watch do nothing (Requirement 15.4).
            new(
                VisibilityPropertyId,
                "Visibility",
                parent is null ? "" : Number(parent.Position.Visibility),
                ReadOnlyReason: string.Format(CultureInfo.InvariantCulture, ChildVisibilityReason, pipeline.Parent),
                Group: PositionGroup),

            new(MaturityPropertyId, "Maturity", Number(child.Maturity), Group: PositionGroup),
            new(StagePropertyId, "Evolution stage", stage.Label, ReadOnlyReason: StageReason, Group: PositionGroup),
        ];
    }

    private static IReadOnlyList<ContextPropertyDefinition> ForLink(WardleyMap map, WardleyIdentityEntry entry)
    {
        var link = map.Links.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);
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
        var pipeline = map.Pipelines.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);
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
                Number(parent.Position.Visibility),
                ReadOnlyReason: string.Format(CultureInfo.InvariantCulture, ChildVisibilityReason, pipeline.Parent),
                Group: PositionGroup));
        }

        rows.Add(new ContextPropertyDefinition(
            PipelineChildrenPropertyId,
            "Components",
            string.Join(", ", pipeline.Children.Select(child => $"{child.Name} ({Number(child.Maturity)})")),
            ContextPropertyEditor.Text,
            ChildrenReason,
            PositionGroup));

        return rows;
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private static WardleyComponent? ComponentOf(WardleyMap map, WardleyIdentityEntry entry) =>
        entry.Kind != WardleyIdentityKind.Component
            ? null
            : map.Components.FirstOrDefault(candidate => WardleyIdentityKeys.Of(candidate) == entry.Key);

    private static (WardleyPipeline Pipeline, WardleyPipelineChild Child)? FindChild(WardleyMap map, WardleyIdentityEntry entry)
    {
        if (entry.Kind != WardleyIdentityKind.PipelineChild)
        {
            return null;
        }

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

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private async ValueTask<ContextPropertyResult> Dispatch(
        ContextTarget target,
        ICommand command,
        CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> None() =>
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Some(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
