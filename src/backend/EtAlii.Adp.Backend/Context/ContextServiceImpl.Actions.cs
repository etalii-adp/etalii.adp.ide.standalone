using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
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
            // caller learns nothing about entries outside what it may already see. The log is
            // where the three are distinguishable, by what else was recorded around it.
            _logger.Debug(
                "No actions for {Source} on watch {WatchId}: it resolved to nothing",
                Describe(request.Source),
                request.WatchId);
            return response;
        }

        var groups = await _contextActionResolver.DiscoverAsync(target, context.CancellationToken);
        response.Groups.AddRange(groups.Select(ContextMessageMapper.ToProto));
        _logger.Debug(
            "Discovered {GroupCount} action groups for {TargetPath} on watch {WatchId}",
            response.Groups.Count,
            target.ResolvedFullPath,
            request.WatchId);
        return response;
    }

    public override async Task<ExecuteActionResponse> ExecuteAction(ExecuteActionRequest request, ServerCallContext context)
    {
        var target = await TryResolveTargetAsync(request.ProjectId, request.WatchId, request.Source, context);
        if (target is null)
        {
            _logger.Warning(
                "Cannot run an action on watch {WatchId}: {Source} resolved to nothing",
                request.WatchId,
                Describe(request.Source));
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
            _logger.Debug(
                "Nothing to run for {Trigger} on {TargetPath}: no available action claims it",
                request.TriggerCase == ExecuteActionRequest.TriggerOneofCase.ActionId ? request.ActionId : request.Shortcut.Key,
                target.ResolvedFullPath);
            return Rejected("That action is not available for this item.");
        }

        var execution = await owner.Provider.ExecuteAsync(target, owner.Action.Id, context.CancellationToken);
        if (execution is ContextExecutionResult.Failed failed)
        {
            _logger.Warning("Action {ActionId} on {TargetPath} failed: {Reason}", owner.Action.Id, target.ResolvedFullPath, failed.Message);
            return Rejected(failed.Message);
        }

        if (execution is ContextExecutionResult.Completed)
        {
            _logger.Information("Action {ActionId} completed on {TargetPath}", owner.Action.Id, target.ResolvedFullPath);
            return new ExecuteActionResponse { Accepted = true };
        }

        var interactionId = (ShortGuid)request.InteractionId;

        // Captured with the interaction because SubmitInteraction carries only an interaction
        // id: without it, a commit that creates something could not report where it landed in
        // the project-relative terms the contract allows.
        Projects.ProjectRootResolver.TryResolve(
            _projectStore, Sessions.SessionContext.GetUserId(context), request.ProjectId, out var rootPath, out _);

        _contextInteractionStore.Begin(new ContextInteraction
        {
            Id = interactionId,
            WatchId = request.WatchId,
            Target = target,
            ActionId = owner.Action.Id,
            Provider = owner.Provider,
            RootPath = rootPath,
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
            case ContextExecutionResult.RequiresChoice requiresChoice:
                prompt.ChoiceDialog = ToProto(requiresChoice.Request);
                break;
        }

        if (!_contextInteractionStore.TryPush(request.WatchId, prompt))
        {
            _contextInteractionStore.Complete(interactionId);
            _logger.Warning(
                "Could not put the {ActionId} dialog on watch {WatchId}: nothing is listening on it any more",
                owner.Action.Id,
                request.WatchId);
            return Rejected("This connection is no longer watching the project.");
        }

        _logger.Debug(
            "Action {ActionId} on {TargetPath} opened a {PromptKind} as interaction {InteractionId}",
            owner.Action.Id,
            target.ResolvedFullPath,
            prompt.PromptCase,
            interactionId);
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
            _logger.Debug("Ignoring input proposed for interaction {InteractionId}, which is no longer active", request.InteractionId);
            return new ProposeInputResponse { Revision = request.Revision, Valid = false, Reason = "This dialog is no longer active." };
        }

        interaction.LastRevision = Math.Max(interaction.LastRevision, request.Revision);

        var validation = await interaction.Provider.ValidateAsync(
            interaction.Target, interaction.ActionId, request.Value, context.CancellationToken);

        // Verbose: one of these per keystroke, once the debounce settles.
        _logger.Verbose(
            "Revision {Revision} of interaction {InteractionId} judged {Verdict}",
            request.Revision,
            request.InteractionId,
            validation.Valid ? "valid" : $"invalid: {validation.Reason}");
        return new ProposeInputResponse { Revision = request.Revision, Valid = validation.Valid, Reason = validation.Reason };
    }

    public override async Task<SubmitInteractionResponse> SubmitInteraction(SubmitInteractionRequest request, ServerCallContext context)
    {
        var interaction = _contextInteractionStore.Get(request.InteractionId);
        if (interaction is null)
        {
            // Also the second half of a double submit: the first one removed the interaction,
            // so the retry finds nothing to run again.
            _logger.Debug("Ignoring a submit for interaction {InteractionId}, which is no longer active", request.InteractionId);
            return new SubmitInteractionResponse { Completed = false, Error = "This dialog is no longer active." };
        }

        var commit = await interaction.Provider.CommitAsync(
            interaction.Target, interaction.ActionId, request.Value, request.Text, context.CancellationToken);

        if (!commit.Completed)
        {
            // Left in flight deliberately: the dialog stays open with the user's input so
            // they can adjust the value or cancel, rather than losing what they typed.
            _logger.Warning(
                "{ActionId} on {TargetPath} did not complete: {Reason}",
                interaction.ActionId,
                interaction.Target.ResolvedFullPath,
                commit.Error);
            return new SubmitInteractionResponse { Completed = false, Error = commit.Error };
        }

        _contextInteractionStore.Complete(request.InteractionId);
        // Information: the action changed something the user will see and expect to persist.
        _logger.Information(
            "{ActionId} completed on {TargetPath}{Created}",
            interaction.ActionId,
            interaction.Target.ResolvedFullPath,
            commit.CreatedFullPath.Length > 0 ? $", creating {commit.CreatedFullPath}" : string.Empty);

        // No hierarchy change is pushed from here - the connection's own RootFolderWatcher
        // observes what happened on disk and reports it through the existing change feed. What
        // the response does carry is where the new thing lives, so the client that asked for it
        // can recognise its own EntryCreated among the ones flowing down that feed.
        var response = new SubmitInteractionResponse { Completed = true };
        if (commit.CreatedFullPath.Length > 0)
        {
            response.CreatedPath = ToRelativePath(interaction.RootPath, commit.CreatedFullPath);
        }

        return response;
    }

    public override Task<CancelInteractionResponse> CancelInteraction(CancelInteractionRequest request, ServerCallContext context)
    {
        _contextInteractionStore.Complete(request.InteractionId);
        _logger.Debug("Interaction {InteractionId} was cancelled", request.InteractionId);
        return Task.FromResult(new CancelInteractionResponse());
    }

    private static ExecuteActionResponse Rejected(string error) => new() { Accepted = false, Error = error };

    /// <summary>Names what a call pointed at, for the log: an explicit source, or the selection.</summary>
    private static string Describe(ContextSource? source) => source?.SourceCase switch
    {
        ContextSource.SourceOneofCase.EntryId => $"entry {source.EntryId}",
        null => "the current selection",
        _ => "an unrecognised source",
    };

    /// <summary>
    /// The target an action applies to: the explicitly named source, resolved through this
    /// connection's own resolvers so an id from elsewhere resolves to nothing - or, with no
    /// source given, the innermost level of the connection's current selection - or, with
    /// nothing selected at all, the project root. "No source and no selection" is how the
    /// explorer's empty space reads, and the root is what that space is.
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
            return _selectionStore.Get(watchId)?.Innermost.Target ?? RootTarget(rootPath, watchId);
        }

        var resolution = await _selectionResolver.ResolveLevelAsync(
            watchId, rootPath, ContextSelectionSource.Unspecified, source, [], null, context.CancellationToken);

        return resolution is ContextLevelResolution.Resolved resolved ? resolved.Level.Target : null;
    }

    /// <summary>
    /// The project root as an action target. It has no entry id - it is the folder the
    /// entries live in, not an entry - so its source id is the empty guid.
    /// </summary>
    /// <summary>
    /// The project-relative form of an absolute path the backend produced - the one shape a
    /// path is ever allowed to take on the wire, and the same one a selection carries.
    /// </summary>
    private static Path ToRelativePath(string rootPath, string fullPath)
    {
        var relative = new Path();
        relative.Segments.AddRange(IoPath.GetRelativePath(rootPath, fullPath)
            .Split([IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
        return relative;
    }

    internal static ContextTarget RootTarget(string rootPath, ShortGuid watchId = default) =>
        new(ContextScope.Hierarchy, rootPath, IsContainer: true, SourceId: default, RootPath: rootPath, WatchId: watchId);

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

    private static ChoiceDialogPrompt ToProto(ContextChoiceRequest request)
    {
        var prompt = new ChoiceDialogPrompt
        {
            Title = request.Title,
            Icon = request.Icon,
            ConfirmLabel = request.ConfirmLabel,
            EmptyMessage = request.EmptyMessage,
        };
        if (request.NameField is { } nameField)
        {
            prompt.NameField = new ContextTextField { Label = nameField.Label, InitialValue = nameField.InitialValue };
        }

        prompt.Options.AddRange(request.Options.Select(ToProto));
        return prompt;
    }

    private static ContextOption ToProto(ContextOptionNode node)
    {
        var option = new ContextOption
        {
            Id = node.Id,
            Label = node.Label,
            Selectable = node.Selectable,
            SuggestedValue = node.SuggestedValue,
        };
        if (node.Children is { Count: > 0 } children)
        {
            option.Children.AddRange(children.Select(ToProto));
        }

        return option;
    }
}
