using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Modules;

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
    private readonly Func<ContextTarget, CancellationToken, ValueTask<DocumentEntry<TBody, TModel>>> _load;

    protected DiagramActionProvider(IHistoryStackStore historyStacks, IDiagramDocumentStore<TBody, TModel> documents)
        : this(historyStacks, DiagramPropertyProvider<TBody, TModel>.LoadFrom(documents))
    {
    }

    /// <summary>
    /// A provider whose documents are loaded by <paramref name="load"/>, for a module whose store
    /// needs more of a target than the path of its body to read it.
    /// </summary>
    protected DiagramActionProvider(IHistoryStackStore historyStacks, Func<ContextTarget, CancellationToken, ValueTask<DocumentEntry<TBody, TModel>>> load)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(load);
        _historyStacks = historyStacks;
        _load = load;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        return Serves(target)
            ? Discover(target, await LoadAsync(target, cancellationToken))
            : [];
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = await LoadAsync(target, cancellationToken);
        return entry.IsUsable
            ? await RunAsync(target, entry, actionId, cancellationToken)
            : new ContextExecutionFailed(Unreadable(target));
    }

    /// <inheritdoc />
    public async ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        return Validate(target, await LoadAsync(target, cancellationToken), actionId, value);
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

        var entry = await LoadAsync(target, cancellationToken);
        if (!entry.IsUsable)
        {
            return ContextCommitResult.Failed(Unreadable(target));
        }

        var change = Commit(target, entry, actionId, value);
        if (change.Command is not { } command)
        {
            return change.Refusal.Length == 0 ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(change.Refusal);
        }

        var result = await _historyStacks.Get(target.Root).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>The refusal of an edit to a document that could not be read, in one wording.</summary>
    internal static string Unreadable(ContextTarget target) =>
        $"{System.IO.Path.GetFileName(target.ResolvedFullPath)} could not be read, so it cannot be edited.";

    /// <summary>The entry of the document <paramref name="target"/> is in, for a leg that is not handed one.</summary>
    protected ValueTask<DocumentEntry<TBody, TModel>> LoadAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _load(target, cancellationToken);
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
    /// discovers. None by default, for a module that overrides the overload given the target instead.
    /// </summary>
    protected virtual IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<TBody, TModel> entry, string elementId) => [];

    /// <summary>
    /// The actions <paramref name="target"/> offers in <paramref name="entry"/>: those its element
    /// offers, by default. A module whose actions depend on the connection as well, such as what
    /// it has open, adds those here, and one whose elements are known by more than the model, such
    /// as ids kept beside each document, answers here alone.
    /// </summary>
    protected virtual IReadOnlyList<ContextActionGroupDefinition> Discover(ContextTarget target, DocumentEntry<TBody, TModel> entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Discover(entry, target.ElementId);
    }

    /// <summary>Runs <paramref name="actionId"/> on <paramref name="target"/> in a document that was read.</summary>
    protected abstract ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<TBody, TModel> entry,
        string actionId,
        CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="value"/> may be committed for <paramref name="actionId"/>, whatever it targets; anything, by default.</summary>
    protected virtual ContextValidationResult Validate(string actionId, string value) =>
        ContextValidationResult.Accepted;

    /// <summary>
    /// Whether <paramref name="value"/> may be committed for <paramref name="actionId"/> on
    /// <paramref name="target"/> in <paramref name="entry"/>: what the overload not given the entry
    /// says, by default. A module that checks the value against the document answers here.
    /// </summary>
    protected virtual ContextValidationResult Validate(ContextTarget target, DocumentEntry<TBody, TModel> entry, string actionId, string value) =>
        Validate(actionId, value);

    /// <summary>
    /// The command that commits <paramref name="value"/> for <paramref name="actionId"/>, asked for
    /// earlier as input or a confirmation, or null when the action does not apply to the target.
    /// None by default, for a module that overrides the overload given the whole entry instead.
    /// </summary>
    protected virtual ICommand? CommitCommand(ContextTarget target, TModel model, string actionId, string value) => null;

    /// <summary>
    /// The command that commits <paramref name="value"/> for <paramref name="actionId"/> on
    /// <paramref name="target"/> in <paramref name="entry"/>: what the overload given the model says,
    /// by default. A module that needs the document's body as well answers here.
    /// </summary>
    protected virtual ICommand? CommitCommand(ContextTarget target, DocumentEntry<TBody, TModel> entry, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return CommitCommand(target, entry.Model, actionId, value);
    }

    /// <summary>
    /// What committing <paramref name="value"/> for <paramref name="actionId"/> on <paramref name="target"/> in
    /// <paramref name="entry"/> is: the command <see cref="CommitCommand(ContextTarget, DocumentEntry{TBody, TModel}, string, string)"/>
    /// makes, or, when it makes none, refused as not applying, by default. A module whose commit is refused with a
    /// sentence of its own before any command is made, as a definition's operation refuses, answers here.
    /// </summary>
    protected virtual DiagramPropertyChange Commit(ContextTarget target, DocumentEntry<TBody, TModel> entry, string actionId, string value) =>
        CommitCommand(target, entry, actionId, value) is { } command
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused($"'{actionId}' does not apply to this selection.");

    /// <summary>Runs <paramref name="command"/> on the project's history.</summary>
    protected async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(command);

        var result = await _historyStacks.Get(target.Root).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on <paramref name="subject"/>: asks what it asks first, or asks the
    /// user to confirm what goes with it, or dispatches its command at once.
    /// </summary>
    protected async ValueTask<ContextExecutionResult> RunActionAsync<TSubject>(
        ContextTarget target,
        DiagramAction<TSubject> action,
        TSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action.Ask(subject) is { } asked)
        {
            return asked;
        }

        return action.Command(subject, "") is { } command
            ? await RemoveAsync(target, command, action.Confirm(subject), cancellationToken)
            : new ContextExecutionFailed($"'{action.Id}' does not apply to this selection.");
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
