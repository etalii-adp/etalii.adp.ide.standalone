using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The frame of a diagram module's property provider: the scope, the checks, the rows of the
/// module's own documents, the could-not-be-read guard and the dispatch through the project's
/// history, written once (code-structure FR-034). A module says which rows an element has and which
/// command a new value is.
/// </summary>
/// <typeparam name="TBody">The body the module's store reads a document into.</typeparam>
/// <typeparam name="TModel">The model the module's parser reads from a body.</typeparam>
public abstract class DiagramPropertyProvider<TBody, TModel> : IContextPropertyProvider
{
    private readonly IHistoryStackStore _historyStacks;
    private readonly IDiagramDocumentStore<TBody, TModel> _documents;

    protected DiagramPropertyProvider(IHistoryStackStore historyStacks, IDiagramDocumentStore<TBody, TModel> documents)
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

        return ValueTask.FromResult(Serves(target)
            ? Describe(_documents.GetOrLoad(target.ResolvedFullPath), target.ElementId)
            : []);
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return ContextPropertyResult.Failure(DiagramActionProvider<TBody, TModel>.Unreadable(target));
        }

        var change = Change(target, entry.Model, propertyId, value);
        if (change.Command is not { } command)
        {
            return change.Refusal.Length > 0 ? ContextPropertyResult.Failure(change.Refusal) : ContextPropertyResult.Success;
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    /// <summary>Whether <paramref name="path"/> is a document of this module.</summary>
    protected abstract bool IsBody(string path);

    /// <summary>
    /// Whether <paramref name="target"/> is an element of this module's: one in a document of its
    /// own, by default. A module whose documents share their extension with others' tells by the
    /// target's origin, which only the registration beside a body can give.
    /// </summary>
    protected virtual bool Serves(ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return IsBody(target.ResolvedFullPath);
    }

    /// <summary>The rows <paramref name="elementId"/> has in <paramref name="entry"/>, or none.</summary>
    protected abstract IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<TBody, TModel> entry, string elementId);

    /// <summary>What setting <paramref name="propertyId"/> to <paramref name="value"/> on <paramref name="target"/> is.</summary>
    protected abstract DiagramPropertyChange Change(ContextTarget target, TModel model, string propertyId, string value);
}

/// <summary>What a new value in the property grid is: a command to dispatch, nothing to do, or a refusal.</summary>
/// <param name="Command">The command that writes the value, or null when there is none to dispatch.</param>
/// <param name="Refusal">Why the value is refused; empty when it is not, or when there is nothing to do.</param>
public sealed record DiagramPropertyChange(ICommand? Command, string Refusal)
{
    /// <summary>The value needs no write, and nothing is refused.</summary>
    public static DiagramPropertyChange Unchanged { get; } = new(null, "");

    /// <summary>The value is written by <paramref name="command"/>.</summary>
    public static DiagramPropertyChange By(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new DiagramPropertyChange(command, "");
    }

    /// <summary>The value is refused, for <paramref name="reason"/>.</summary>
    public static DiagramPropertyChange Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new DiagramPropertyChange(null, reason);
    }
}
