using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Sessions;
using Grpc.Core;

namespace EtAlii.Adp.Context;

/// <summary>
/// The context-action half of <see cref="ContextService"/>: discovering what a target
/// offers, starting an action, judging proposed input, and finishing or abandoning it.
/// An explicit source names the target; without one, the action applies to whatever the
/// connection currently has selected - which is how a ribbon button or a global
/// shortcut says "rename whatever is selected" without looking the selection up first.
/// </summary>
/// <remarks>
/// Nothing here branches on rename or delete: every decision comes from
/// <see cref="IContextActionResolver"/> and whichever provider it routes to.
/// </remarks>
public sealed partial class ContextService
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
        response.Groups.AddRange(groups.Select(ContextActionGroupDefinition.ToProto));
        _logger.Debug(
            "Discovered {GroupCount} action groups for {TargetPath} on watch {WatchId}",
            response.Groups.Count,
            target.ResolvedFullPath,
            request.WatchId);
        return response;
    }

    public override async Task<ExecuteActionResponse> ExecuteAction(ExecuteActionRequest request, ServerCallContext context)
    {
        // The project is resolved once for the whole request: the target is found under it, and
        // the interaction below keeps it (code-structure FR-062).
        var root = TryResolveRoot(request.ProjectId, context);
        var target = root is null ? null : await TryResolveTargetAsync(root.Value, request.WatchId, request.Source, context);
        if (root is null || target is null)
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
                await _contextActionResolver.ResolveByShortcutAsync(target, ContextWire.FromProto(request.Shortcut), context.CancellationToken),
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
        if (execution is ContextExecutionFailed failed)
        {
            _logger.Warning("Action {ActionId} on {TargetPath} failed: {Reason}", owner.Action.Id, target.ResolvedFullPath, failed.Message);
            return Rejected(failed.Message);
        }

        if (execution is ContextExecutionCompleted)
        {
            _logger.Information("Action {ActionId} completed on {TargetPath}", owner.Action.Id, target.ResolvedFullPath);
            // The action may have changed what applies to the very same selection - a collapse
            // must offer Expand next - so its actions are re-derived and pushed.
            _selectionStore.Refresh(request.WatchId);
            return new ExecuteActionResponse { Accepted = true };
        }

        return Prompt(request, root.Value, target, owner, execution);
    }

    /// <summary>
    /// Puts the question an action asks on the connection's watch stream, as an interaction its
    /// answer is submitted to; refused when nothing listens there any more.
    /// </summary>
    private ExecuteActionResponse Prompt(ExecuteActionRequest request, ProjectRoot root, ContextTarget target, ContextActionOwner owner, ContextExecutionResult execution)
    {
        var interactionId = (ShortGuid)request.InteractionId;

        // The root is captured with the interaction because SubmitInteraction carries only an
        // interaction id: without it, a commit that creates something could not report where it
        // landed in the project-relative terms the contract allows.
        var rootPath = root.FullPath;

        // WHAT THE ANSWER WILL BE APPLIED TO, which is not always what the question was asked
        // about. An input request may name an element whose label it edits and an action to
        // commit under; where it does, the interaction remembers those instead.
        //
        // This is what lets a provider create something and then edit it in place: the add runs
        // at execute time, and the prompt that follows belongs to the new element and to the
        // provider's own rename. Without the re-pointing, the commit would run the add again -
        // against the parent, adding a second child - which is exactly the shape of bug that
        // makes "just suppress the dialog" the wrong fix.
        //
        // INERT FOR EVERY PROMPT THAT EXISTED BEFORE IT. Both fields default to empty, and
        // every inline caller in the tree today passes `target.ElementId` as the element - so
        // the re-pointing is provably a no-op for all of them rather than merely believed to be.
        var input = (execution as ContextExecutionRequiresInput)?.Request;
        var commitTarget = string.IsNullOrEmpty(input?.InlineLabelElementId)
            ? target
            : target with { ElementId = input.InlineLabelElementId };
        var commitActionId = string.IsNullOrEmpty(input?.CommitActionId) ? owner.Action.Id : input.CommitActionId;

        var prompt = BeginInteraction(interactionId, request.WatchId, commitTarget, commitActionId, owner.Provider, rootPath, execution);
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

        if (interaction.Offered is { } offered && !offered.Contains(request.Value))
        {
            // Not something the dialog offered: a stale client, or one that made the value up.
            // Left in flight, as a refused commit is, so the user can pick again or cancel.
            _logger.Warning(
                "Refusing {Value} for {ActionId} on {TargetPath}: the dialog did not offer it",
                request.Value,
                interaction.ActionId,
                interaction.Target.ResolvedFullPath);
            return new SubmitInteractionResponse { Completed = false, Error = "That file cannot be chosen here." };
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
        // A commit can change the selection's own actions too - adding a child to a leaf makes
        // it collapsible - and, like the direct completion above, nothing else re-derives them.
        _selectionStore.Refresh(interaction.WatchId);

        // No hierarchy change is pushed from here - the connection's own file events
        // observe what happened on disk and report it through the existing change feed. What
        // the response does carry is where the new thing lives, so the client that asked for it
        // can recognise its own EntryCreated among the ones flowing down that feed.
        var response = new SubmitInteractionResponse { Completed = true };
        if (commit.Next is { } next and not (ContextExecutionCompleted or ContextExecutionFailed))
        {
            // A further question of the same action: an interaction of its own, for the same
            // target and provider, answered under the action id the provider gave it. It rides
            // this response and not the watch stream, so the client can put it where the answered
            // dialog was without the two crossing.
            var nextActionId = commit.NextActionId.Length > 0 ? commit.NextActionId : interaction.ActionId;
            response.NextPrompt = BeginInteraction(ShortGuid.NewShortGuid(), interaction.WatchId, interaction.Target, nextActionId, interaction.Provider, interaction.RootPath, next);
        }

        if (commit.CreatedFullPath.Length > 0)
        {
            response.CreatedPath = ContextWire.ToRelativePath(interaction.RootPath, commit.CreatedFullPath);
        }

        return response;
    }

    public override Task<CancelInteractionResponse> CancelInteraction(CancelInteractionRequest request, ServerCallContext context)
    {
        _contextInteractionStore.Complete(request.InteractionId);
        _logger.Debug("Interaction {InteractionId} was cancelled", request.InteractionId);
        return Task.FromResult(new CancelInteractionResponse());
    }

    /// <summary>
    /// Begins the interaction a prompt asks, and returns the prompt. A file dialog's tree is built
    /// once, here: what the provider's predicate accepts now is both what the dialog shows and the
    /// only thing its submission may be.
    /// </summary>
    private ContextPrompt BeginInteraction(
        ShortGuid interactionId,
        Documents.Wire.ShortGuid watchId,
        ContextTarget target,
        string actionId,
        IContextActionProvider provider,
        string rootPath,
        ContextExecutionResult execution)
    {
        var fileDialog = execution is ContextExecutionRequiresFile requiresFile ? ContextWire.ToProto(requiresFile.Request, rootPath) : null;
        _contextInteractionStore.Begin(new ContextInteraction
        {
            Id = interactionId,
            WatchId = watchId,
            Target = target,
            ActionId = actionId,
            Provider = provider,
            RootPath = rootPath,
            Offered = fileDialog?.Pinned.Concat(fileDialog.Files).SelectMany(ContextWire.Selectable).ToHashSet(StringComparer.Ordinal),
        });
        return ContextWire.PromptOf(interactionId, execution, fileDialog);
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
        Documents.Wire.ShortGuid projectId,
        Documents.Wire.ShortGuid watchId,
        ContextSource? source,
        ServerCallContext context) =>
        TryResolveRoot(projectId, context) is { } root
            ? await TryResolveTargetAsync(root, watchId, source, context)
            : null;

    /// <summary>The root folder of the caller's project, or null when it is not theirs or is gone.</summary>
    private ProjectRoot? TryResolveRoot(Documents.Wire.ShortGuid projectId, ServerCallContext context) =>
        Projects.ProjectRootResolver.TryResolve(_projectStore, SessionContext.GetUserId(context), projectId, out var root, out _)
            ? root
            : null;

    /// <summary>The target an action applies to, as above, within a project already resolved.</summary>
    private async ValueTask<ContextTarget?> TryResolveTargetAsync(
        ProjectRoot root,
        Documents.Wire.ShortGuid watchId,
        ContextSource? source,
        ServerCallContext context)
    {
        if (source is null)
        {
            return _selectionStore.Get(watchId)?.Innermost.Target ?? ContextWatch.RootTarget(root, watchId);
        }

        // An element lives inside a diagram, so a bare element id is resolvable only within
        // the connection's current selection - whose chain names the file the element is in.
        // The canvas sends exactly this: a keystroke against its focused node, which is
        // normally the current innermost already (mindmap-diagram Requirement 8.4). An
        // element of some other file than the selected one resolves to nothing, as it should.
        if (source.SourceCase == ContextSource.SourceOneofCase.ElementId)
        {
            // A canvas names the diagram it draws, and that file wins over the selection's: after a
            // switch of diagram tabs the selection still names the other tab's file, and once the
            // selected element is culled from view the canvas clears it and it names nothing.
            if (source.DiagramEntryId is { } diagramEntryId)
            {
                var diagramResolution = await _selectionResolver.ResolveLevelAsync(
                    watchId, root, new ContextSource { EntryId = diagramEntryId }, [], null, context.CancellationToken);
                if (diagramResolution is not ResolvedContextLevel diagram)
                {
                    return null;
                }

                var inDiagram = await _selectionResolver.ResolveLevelAsync(
                    watchId, root, source, [], diagram.Level, context.CancellationToken);
                return inDiagram is ResolvedContextLevel resolvedInDiagram ? resolvedInDiagram.Level.Target : null;
            }

            var record = _selectionStore.Get(watchId);
            if (record is null)
            {
                return null;
            }

            var innermost = record.Innermost.Target;
            if (innermost.ElementId == source.ElementId.Value)
            {
                return innermost;
            }

            // Focus moved to another node of the same diagram and the keystroke beat the new
            // selection's round trip: resolve the element under the selection's file level.
            var fileLevel = record.Levels.LastOrDefault(level => level.Scope == ContextScope.Hierarchy);
            if (fileLevel is null)
            {
                return null;
            }

            var elementResolution = await _selectionResolver.ResolveLevelAsync(
                watchId, root, source, [], fileLevel, context.CancellationToken);
            return elementResolution is ResolvedContextLevel resolvedElement ? resolvedElement.Level.Target : null;
        }

        var resolution = await _selectionResolver.ResolveLevelAsync(
            watchId, root, source, [], null, context.CancellationToken);

        return resolution is ResolvedContextLevel resolved ? resolved.Level.Target : null;
    }
}
