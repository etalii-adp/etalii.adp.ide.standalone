using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Today's order of the C4 findings, by element rather than by rule: the includes, then for each
/// element in document order its findings in the order the definition states its rules, then each
/// relationship's, then each view's, then the coverage findings (c4-disl-fbl design, <i>C4FindingOrder</i>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for gap C10</b> (c4-disl-fbl Q4, finding A11; the definitions' companion, gap 10):
/// DISL orders findings by rule (§8.6), and <c>constraints.order</c> orders by code and then by line, so
/// neither can state "element by element". The definition's findings are taken as the evaluator gives
/// them, which is by rule and within a rule by element in model order, and only re-sorted here; nothing
/// is added, dropped or changed. It leaves when the language can state the order.
/// </para>
/// <para>
/// <b>A view's findings</b> are found by the view their <see cref="DislFinding.Subject"/> names: the
/// unknown scope first, then a kind the view does not permit in the order the view's members are
/// listed in (what its <c>include</c> statements name, in their order, then the participants of its
/// interactions), then an empty view.
/// </para>
/// </remarks>
internal static class C4FindingOrder
{
    /// <summary>The codes of the five groups, in order; a code none names comes last.</summary>
    private static readonly Dictionary<string, int> Groups = new(StringComparer.Ordinal)
    {
        [C4Rules.IncludeNotFollowed] = 0,
        [C4Rules.MissingDescription] = 1,
        [C4Rules.MissingDeploymentDescription] = 1,
        [C4Rules.MissingInfrastructureDescription] = 1,
        [C4Rules.MissingTechnology] = 1,
        [C4Rules.MissingDeploymentTechnology] = 1,
        [C4Rules.MissingInfrastructureTechnology] = 1,
        [C4Rules.DisconnectedElement] = 1,
        [C4Rules.MisplacedElement] = 1,
        [C4Rules.DanglingRelationship] = 2,
        [C4Rules.UnlabelledRelationship] = 2,
        [C4Rules.MissingProtocol] = 2,
        [C4Rules.UnknownViewScope] = 3,
        [C4Rules.KindNotPermitted] = 3,
        [C4Rules.EmptyView] = 3,
        [C4Rules.ElementNotOnAnyView] = 4,
    };

    /// <summary>Within a view: the unknown scope, then the kinds not permitted, then the empty view.</summary>
    private static readonly Dictionary<string, int> WithinAView = new(StringComparer.Ordinal)
    {
        [C4Rules.UnknownViewScope] = 0,
        [C4Rules.KindNotPermitted] = 1,
        [C4Rules.EmptyView] = 2,
    };

    /// <summary>The key of a place a finding's group does not use.</summary>
    private const int None = 0;

    /// <summary><paramref name="findings"/> of <paramref name="workspace"/>, as the evaluator gave them, in today's order.</summary>
    public static IReadOnlyList<DislFinding> Order(IReadOnlyList<DislFinding> findings, C4Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(workspace);
        return
        [
            .. findings
                .Select((finding, arising) => (Finding: finding, Key: KeyOf(finding, workspace), Arising: arising))
                .OrderBy(entry => entry.Key.Group)
                .ThenBy(entry => entry.Key.Position)
                .ThenBy(entry => entry.Key.WithinView)
                .ThenBy(entry => entry.Key.Member)
                .ThenBy(entry => entry.Arising)
                .Select(entry => entry.Finding),
        ];
    }

    /// <summary>
    /// Where a finding goes: its group; the position of its element, relationship or view in the
    /// reading; within a view its place among that view's findings and the member it is about.
    /// </summary>
    private static (int Group, int Position, int WithinView, int Member) KeyOf(DislFinding finding, C4Workspace workspace)
    {
        var group = Groups.GetValueOrDefault(finding.Code, int.MaxValue);
        var id = finding.ElementIds is [var first, ..] ? first : null;
        switch (group)
        {
            case 1 or 4:
                return (group, ElementPosition(workspace, id, finding.Line), None, None);
            case 2:
                return (group, RelationshipPosition(workspace, id, finding.Line), None, None);
            case 3:
                var view = ViewPosition(workspace, finding);
                return view < int.MaxValue
                    ? (group, view, WithinAView[finding.Code], id is null ? None : MemberPosition(workspace, workspace.Views[view], id))
                    : (group, int.MaxValue, WithinAView[finding.Code], None);
            default:
                return (group, int.MaxValue, None, None);
        }
    }

    /// <summary>The element's place in document order: the one of that id at that line, else the first of that id.</summary>
    private static int ElementPosition(C4Workspace workspace, string? id, int? line)
    {
        var elements = workspace.Elements;
        var position = IndexOf(elements, element => string.Equals(element.Id, id, StringComparison.Ordinal) && (int)element.Line == line);
        return position < int.MaxValue ? position : IndexOf(elements, element => string.Equals(element.Id, id, StringComparison.Ordinal));
    }

    /// <summary>The relationship's place in document order: the one at that line, else the one of that id.</summary>
    private static int RelationshipPosition(C4Workspace workspace, string? id, int? line)
    {
        var relationships = workspace.Relationships;
        var position = IndexOf(relationships, relationship => (int)relationship.Line == line);
        return position < int.MaxValue ? position : IndexOf(relationships, relationship => string.Equals(relationship.Id, id, StringComparison.Ordinal));
    }

    /// <summary>The place of the view the finding's subject names: the one of that key at its line, else the first of that key.</summary>
    private static int ViewPosition(C4Workspace workspace, DislFinding finding)
    {
        var views = workspace.Views;
        var position = IndexOf(views, view => string.Equals(view.Key, finding.Subject, StringComparison.Ordinal) && (int)view.Line == finding.Line);
        if (position < int.MaxValue) return position;
        position = IndexOf(views, view => string.Equals(view.Key, finding.Subject, StringComparison.Ordinal));
        return position < int.MaxValue ? position : IndexOf(views, view => string.Equals(view.Key, finding.Subject, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Where <paramref name="id"/> is listed among <paramref name="view"/>'s members: the first
    /// <c>include</c> that names it, else after every include, the first interaction it takes part in.
    /// </summary>
    private static int MemberPosition(C4Workspace workspace, C4View view, string id)
    {
        bool Names(string written) => workspace.Find(written) is { } element && string.Equals(element.Id, id, StringComparison.Ordinal);

        var included = IndexOf(view.Includes, Names);
        if (included < int.MaxValue) return included;
        var participants = view.Interactions.SelectMany(interaction => new[] { interaction.SourceId, interaction.DestinationId }).ToList();
        var participant = IndexOf(participants, Names);
        return participant < int.MaxValue ? view.Includes.Count + participant : int.MaxValue;
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (predicate(items[index])) return index;
        }

        return int.MaxValue;
    }
}
