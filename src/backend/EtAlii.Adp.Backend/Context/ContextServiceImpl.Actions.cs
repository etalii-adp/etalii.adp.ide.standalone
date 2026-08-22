using Grpc.Core;

namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// The context-action half of <see cref="ContextServiceImpl"/>: discovering what a target
/// offers, starting an action, judging proposed input, and finishing or abandoning it.
/// An explicit source names the target; without one, the action applies to whatever the
/// connection currently has selected - which is how a ribbon button or a global
/// shortcut says "rename whatever is selected" without looking the selection up first.
/// </summary>
/// <remarks>
/// Nothing here branches on rename or delete: every decision comes from
/// <see cref="IContextActionResolver"/> and whichever provider it routes to.
/// </remarks>
public sealed partial class ContextServiceImpl
{
    public override async Task<DiscoverActionsResponse> DiscoverActions(DiscoverActionsRequest request, ServerCallContext context)
    {
        var response = new DiscoverActionsResponse();
        var target = await TryResolveTargetAsync(request.ProjectId, request.WatchId, request.Source, context);
        if (target is null)
        {
            // Unauthorized, unknown, or already gone - all answered the same way, so a
            // caller learns nothing about entries outside what it may already see.
            return response;
        }

        var groups = await _contextActionResolver.DiscoverAsync(target, context.CancellationToken);
        response.Groups.AddRange(groups.Select(ContextMessageMapper.ToProto));
        return response;
    }

    public override async Task<ExecuteActionResponse> ExecuteAction(ExecuteActionRequest request, ServerCallContext context)
    {
        var target = await TryResolveTargetAsync(request.ProjectId, request.WatchId, request.Source, context);
        if (target is null)
        {
            return Rejected(request.Source is null ? "Nothing is selected." : "This item is no longer available.");
        }

        var owner = request.TriggerCase switch
        {
            ExecuteActionRequest.TriggerOneofCase.ActionId =>
                await _contextActionResolver.ResolveByActionIdAsync(target, request.ActionId, context.CancellationToken),
            ExecuteActionRequest.TriggerOneofCase.Shortcut =>
                await _contextActionResolver.ResolveByShortcutAsync(target, FromProto(request.Shortcut), context.CancellationToken),
            _ => null,
        };

        if (owner is null || !owner.Action.Available)
        {
            // Covers an unknown action, a shortcut nothing here answers to, and a shortcut
            // bound to an action currently reported unavailable: none of them do anything.
            return Rejected("That action is not available for this item.");
        }

        var execution = await owner.Provider.ExecuteAsync(target, owner.Action.Id, context.CancellationToken);
        if (execution is ContextExecutionResult.Failed failed)
        {
            return Rejected(failed.Message);
        }

        if (execution is ContextExecutionResult.Completed)
        {
            return new ExecuteActionResponse { Accepted = true };
        }

        var interactionId = (ShortGuid)request.InteractionId;
        _contextInteractionStore.Begin(new ContextInteraction
        {
            Id = interactionId,
            WatchId = request.WatchId,
            Target = target,
            ActionId = owner.Action.Id,
            Provider = owner.Provider,
        });

        var prompt = new ContextPrompt { InteractionId = interactionId };
        switch (execution)
        {
            case ContextExecutionResult.RequiresInput requiresInput:
                prompt.InputDialog = ToProto(requiresInput.Request);
                break;
            case ContextExecutionResult.RequiresConfirmation requiresConfirmation:
                prompt.ConfirmDialog = ToProto(requiresConfirmation.Request);
                break;
        }

        if (!_contextInteractionStore.TryPush(request.WatchId, prompt))
        {
            _contextInteractionStore.Complete(interactionId);
            return Rejected("This connection is no longer watching the project.");
        }

        return new ExecuteActionResponse { Accepted = true };
    }

    /// <summary>
    /// The verdict comes back on this call's own response rather than as a pushed prompt:
    /// pushing would cost a second round trip and let a verdict arrive after the user has
    /// typed on. The request's revision is echoed so a reply that still lands out of order
    /// is recognisable as stale.
    /// </summary>
    public override async Task<ProposeInputResponse> ProposeInput(ProposeInputRequest request, ServerCallContext context)
    {
        var interaction = _contextInteractionStore.Get(request.InteractionId);
        if (interaction is null)
        {
            return new ProposeInputResponse { Revision = request.Revision, Valid = false, Reason = "This dialog is no longer active." };
        }

        interaction.LastRevision = Math.Max(interaction.LastRevision, request.Revision);

        var validation = await interaction.Provider.ValidateAsync(
            interaction.Target, interaction.ActionId, request.Value, context.CancellationToken);

        return new ProposeInputResponse { Revision = request.Revision, Valid = validation.Valid, Reason = validation.Reason };
    }

    public override async Task<SubmitInteractionResponse> SubmitInteraction(SubmitInteractionRequest request, ServerCallContext context)
    {
        var interaction = _contextInteractionStore.Get(request.InteractionId);
        if (interaction is null)
        {
            // Also the second half of a double submit: the first one removed the interaction,
            // so the retry finds nothing to run again.
            return new SubmitInteractionResponse { Completed = false, Error = "This dialog is no longer active." };
        }

        var commit = await interaction.Provider.CommitAsync(
            interaction.Target, interaction.ActionId, request.Value, context.CancellationToken);

        if (!commit.Completed)
        {
            // Left in flight deliberately: the dialog stays open with the user's input so
            // they can adjust the value or cancel, rather than losing what they typed.
            return new SubmitInteractionResponse { Completed = false, Error = commit.Error };
        }

        _contextInteractionStore.Complete(request.InteractionId);

        // No hierarchy change is pushed from here - the connection's own RootFolderWatcher
        // observes what happened on disk and reports it through the existing change feed.
        return new SubmitInteractionResponse { Completed = true };
    }

    public override Task<CancelInteractionResponse> CancelInteraction(CancelInteractionRequest request, ServerCallContext context)
    {
        _contextInteractionStore.Complete(request.InteractionId);
        return Task.FromResult(new CancelInteractionResponse());
    }

    private static ExecuteActionResponse Rejected(string error) => new() { Accepted = false, Error = error };

    /// <summary>
    /// The target an action applies to: the explicitly named source, resolved through this
    /// connection's own resolvers so an id from elsewhere resolves to nothing - or, with no
    /// source given, the innermost level of the connection's current selection.
    /// </summary>
    private async ValueTask<ContextTarget?> TryResolveTargetAsync(
        Contracts.ShortGuid projectId,
        Contracts.ShortGuid watchId,
        ContextSource? source,
        ServerCallContext context)
    {
        var userId = Sessions.SessionContext.GetUserId(context);
        if (!Projects.ProjectRootResolver.TryResolve(_projectStore, userId, projectId, out var rootPath, out _))
        {
            return null;
        }

        if (source is null)
        {
            return _selectionStore.Get(watchId)?.Innermost.Target;
        }

        var resolution = await _selectionResolver.ResolveLevelAsync(
            watchId, rootPath, ContextSelectionSource.Unspecified, source, [], null, context.CancellationToken);

        return resolution is ContextLevelResolution.Resolved resolved ? resolved.Level.Target : null;
    }

    private static ContextShortcutDefinition FromProto(ContextShortcut shortcut) =>
        new(shortcut.Key, shortcut.Ctrl, shortcut.Shift, shortcut.Alt, shortcut.Meta);

    private static InputDialogPrompt ToProto(ContextInputRequest request) => new()
    {
        Title = request.Title,
        Icon = request.Icon,
        FieldLabel = request.FieldLabel,
        InitialValue = request.InitialValue,
        ConfirmLabel = request.ConfirmLabel,
    };

    private static ConfirmDialogPrompt ToProto(ContextConfirmationRequest request) => new()
    {
        Title = request.Title,
        Icon = request.Icon,
        Message = request.Message,
        ConfirmLabel = request.ConfirmLabel,
        Danger = request.Danger,
    };
}
