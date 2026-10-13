using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The frame of a diagram module's action provider: the scope, the checks, discovery on the
/// module's own documents, the could-not-be-read guard, the dispatch through the project's history,
/// the commit leg and the deletion confirmation, written once (code-structure FR-034). A module says
/// which actions an element offers and what each does.
/// </summary>
/// <remarks>
/// <para>
/// <b>A document that could not be read is not edited</b>: its emptiness is not the document, so
/// every action on it is refused before a command is made, and nothing is committed to it.
/// </para>
/// <para>
/// <b>Every action that needs no input dispatches in <see cref="RunAsync"/></b>: a completed
/// execution never reaches the commit leg, so answering completed without dispatching would do
/// nothing at all.
/// </para>
/// </remarks>
/// <typeparam name="TBody">The body the module's store reads a document into.</typeparam>
/// <typeparam name="TModel">The model the module's parser reads from a body.</typeparam>
public abstract class DiagramActionProvider<TBody, TModel> : IContextActionProvider
{
    private readonly IHistoryStackStore _historyStacks;
    private readonly IDiagramDocumentStore<TBody, TModel> _documents;

    protected DiagramActionProvider(IHistoryStackStore historyStacks, IDiagramDocumentStore<TBody, TModel> documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Serves(target)
            ? Discover(_documents.GetOrLoad(target.ResolvedFullPath), target.ElementId)
            : []);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        return entry.IsUsable
            ? await RunAsync(target, entry, actionId, cancellationToken)
            : new ContextExecutionFailed(Unreadable(target));
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Validate(target, actionId, value));
    }

    /// <inheritdoc />
    public async ValueTask<ContextCommitResult> CommitAsync(
        ContextTarget target,
        string actionId,
        string value,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = text;

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return ContextCommitResult.Failed(Unreadable(target));
        }

        if (CommitCommand(target, entry.Model, actionId, value) is not { } command)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>The refusal of an edit to a document that could not be read, in one wording.</summary>
    internal static string Unreadable(ContextTarget target) =>
        $"{System.IO.Path.GetFileName(target.ResolvedFullPath)} could not be read, so it cannot be edited.";

    /// <summary>The entry of the document <paramref name="target"/> is in, for a leg that is not handed one.</summary>
    protected DocumentEntry<TBody, TModel> Load(ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _documents.GetOrLoad(target.ResolvedFullPath);
    }

    /// <summary>Whether <paramref name="path"/> is a document of this module.</summary>
    protected abstract bool IsBody(string path);

    /// <inheritdoc cref="DiagramPropertyProvider{TBody, TModel}.Serves"/>
    protected virtual bool Serves(ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return IsBody(target.ResolvedFullPath);
    }

    /// <summary>
    /// The actions <paramref name="elementId"/> offers in <paramref name="entry"/>: an element, empty
    /// canvas, a drop or a finished gesture. Executing an action by id only finds actions its target
    /// discovers.
    /// </summary>
    protected abstract IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<TBody, TModel> entry, string elementId);

    /// <summary>Runs <paramref name="actionId"/> on <paramref name="target"/> in a document that was read.</summary>
    protected abstract ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<TBody, TModel> entry,
        string actionId,
        CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="value"/> may be committed for <paramref name="actionId"/> on <paramref name="target"/>; anything, by default.</summary>
    protected virtual ContextValidationResult Validate(ContextTarget target, string actionId, string value) =>
        ContextValidationResult.Accepted;

    /// <summary>
    /// The command that commits <paramref name="value"/> for <paramref name="actionId"/>, asked for
    /// earlier as input or a confirmation, or null when the action does not apply to the target.
    /// </summary>
    protected abstract ICommand? CommitCommand(ContextTarget target, TModel model, string actionId, string value);

    /// <summary>Runs <paramref name="command"/> on the project's history.</summary>
    protected async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(command);

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    /// <summary>
    /// Removes something: at once when <paramref name="confirmation"/> is null, or after the user
    /// confirms what goes with it, through the commit leg.
    /// </summary>
    protected async ValueTask<ContextExecutionResult> RemoveAsync(
        ContextTarget target,
        ICommand remove,
        RemovalConfirmation? confirmation,
        CancellationToken cancellationToken) =>
        confirmation is null
            ? await DispatchAsync(target, remove, cancellationToken)
            : new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                confirmation.Title, "mdi-delete-outline", confirmation.Message, confirmation.ConfirmLabel, Danger: confirmation.Danger));
}

/// <summary>What the user is asked before something is removed with what goes with it.</summary>
/// <param name="Title">The dialog's title.</param>
/// <param name="Message">What else goes, in a sentence.</param>
/// <param name="ConfirmLabel">The confirming button's label.</param>
/// <param name="Danger">Whether the button is drawn as destructive.</param>
public sealed record RemovalConfirmation(string Title, string Message, string ConfirmLabel, bool Danger = true);
