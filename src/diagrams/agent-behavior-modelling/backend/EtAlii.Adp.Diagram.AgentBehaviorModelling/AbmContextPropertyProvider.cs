using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The property rows of a selected node, contributed as data the grid renders without
/// understanding: its kind, its label, a Retry's attempts, and its notes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The kind is a choice among the kinds the node can become</b>: a node with children cannot
/// become a Do, a Check, an Ask the user or a Delegate, and one with several cannot become a Retry
/// or any other wrapper. Offering only those keeps a refusal from being the grid's way of saying so.
/// </para>
/// <para>
/// <b>The notes are where the detail goes</b> - the tool to use, the format to answer in, what to
/// watch out for - and they are written into the Markdown under the node, where the agent reads them.
/// </para>
/// </remarks>
public sealed class AbmContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>A node's kind.</summary>
    public const string KindProperty = "abm.kind";

    /// <summary>A node's label.</summary>
    public const string LabelProperty = "abm.label";

    /// <summary>A Retry's attempts.</summary>
    public const string AttemptsProperty = "abm.attempts";

    /// <summary>A node's notes.</summary>
    public const string NotesProperty = "abm.notes";

    /// <summary>A node's place in the tree, read-only.</summary>
    public const string PlaceProperty = "abm.place";

    private const string NodeGroup = "Node";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IAbmDocumentStore _documents;

    public AbmContextPropertyProvider(IHistoryStackStore historyStacks, IAbmDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.Origin != Diagram.AgentBehaviorModelling.Origin
            || _documents.GetOrLoad(target.ResolvedFullPath).Model.NodeOf(target.ElementId) is not { } node)
        {
            return Rows([]);
        }

        List<ContextPropertyDefinition> rows =
        [
            new(KindProperty, "Kind", Choice(node.Kind), ContextPropertyEditor.Choice, Group: NodeGroup, Candidates: [.. KindsFor(node).Select(Choice)]),
            new(LabelProperty, "Label", node.Label, Group: NodeGroup),
        ];
        if (node.Kind == AbmNodeKinds.Retry)
        {
            rows.Add(new(AttemptsProperty, "Attempts", node.RetryCount.ToString(CultureInfo.InvariantCulture), Group: NodeGroup));
        }

        rows.Add(new(NotesProperty, "Notes", node.Notes, ContextPropertyEditor.Text, Group: NodeGroup));
        rows.Add(new(PlaceProperty, "Place", node.Id, ReadOnlyReason: "A node's place follows from where it sits in the tree.", Group: NodeGroup));
        return Rows(rows);
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var body = target.ResolvedFullPath;
        var node = _documents.GetOrLoad(body).Model.NodeOf(target.ElementId);
        if (node is null)
        {
            return ContextPropertyResult.Failure("That node is no longer in this behavior model.");
        }

        ICommand? command;
        switch (propertyId)
        {
            case KindProperty:
                var kind = AbmNodeKinds.All.FirstOrDefault(candidate => Choice(candidate.Id) == value || candidate.Id == value);
                if (kind is null)
                {
                    return ContextPropertyResult.Failure($"'{value}' is not a kind of node.");
                }

                command = new SetAbmNodeKindCommand(body, node.Id, kind.Id);
                break;
            case LabelProperty:
                command = new RenameAbmNodeCommand(body, node.Id, value);
                break;
            case AttemptsProperty:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempts) || attempts < 1)
                {
                    return ContextPropertyResult.Failure($"'{value}' is not a number of attempts; a Retry allows at least 1.");
                }

                command = new SetAbmNodeKindCommand(body, node.Id, AbmNodeKinds.Retry, attempts);
                break;
            case NotesProperty:
                command = new SetAbmNotesCommand(body, node.Id, value);
                break;
            default:
                return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        // Through the project's history and out through the delta stream - never written by the grid.
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    /// <summary>The kinds a node can become without losing a child.</summary>
    private static IEnumerable<AbmNodeKind> KindsFor(AbmNode node) => AbmNodeKinds.All.Where(kind => kind.Category switch
    {
        AbmNodeCategory.Leaf => node.ChildIds.Count == 0,
        AbmNodeCategory.Decorator => node.ChildIds.Count <= 1,
        _ => true,
    });

    /// <summary>A kind as the choice list reads it.</summary>
    private static string Choice(string kind) => kind == AbmNodeKinds.Retry ? "Retry" : AbmNodeKinds.Of(kind).Keyword;

    private static string Choice(AbmNodeKind kind) => Choice(kind.Id);

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
