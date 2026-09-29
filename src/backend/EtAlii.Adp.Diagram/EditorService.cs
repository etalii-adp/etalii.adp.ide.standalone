using EtAlii.Adp.Authentication;
using EtAlii.Adp.Editor.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The editor family's own unary calls - today the one save. An editor tab's text arrives on
/// the shared stream (<see cref="DiagramService"/>'s Open, through the editor session adapter);
/// what only an editor does is asked here, so the diagram service carries no editor call
/// (spec 002, naming convention alignment).
/// </summary>
public sealed class EditorService : Editor.Wire.EditorService.EditorServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<EditorService>();

    private readonly IProjectStore _projectStore;
    private readonly IHistoryStackStore _historyStacks;

    public EditorService(IProjectStore projectStore, IHistoryStackStore historyStacks)
    {
        _projectStore = projectStore;
        _historyStacks = historyStacks;
    }

    public override async Task<SaveTextResponse> SaveText(SaveTextRequest request, ServerCallContext context)
    {
        if (!ProjectTextFile.TryResolve(_projectStore, request.ProjectId, request.Path, SessionContext.GetUserId(context), out var rootPath, out var fullPath))
        {
            // The content is deliberately not logged - only which file the save asked for.
            _logger.Warning("Refused to save {Path}: it does not resolve inside the project any more", string.Join('/', request.Path.Segments));
            return new SaveTextResponse { Error = "The file no longer exists." };
        }

        // Through the project's history, not straight to disk: a save is one undo away like
        // every other change (Requirement 6.2). The write comes back to every open session of
        // the file as an ordinary pushed change - each text session through its own watcher,
        // each diagram through the reload bridge and its store - so the file on disk is the
        // tie-breaker by construction (Requirements 5.3, 5.5).
        var result = await _historyStacks.Get(rootPath).ExecuteAsync(
            new SaveTextFileCommand(fullPath, request.Content), context.CancellationToken);

        _logger.Information("Saved {FullPath} through the history: {Outcome}", fullPath, result.IsSuccess ? "ok" : result.Error);
        return new SaveTextResponse { Error = result.IsSuccess ? "" : result.Error };
    }
}
