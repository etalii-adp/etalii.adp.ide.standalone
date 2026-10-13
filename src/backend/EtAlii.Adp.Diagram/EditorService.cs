using EtAlii.Adp.Authentication;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Editor.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Serilog;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProblemSeverity = EtAlii.Adp.Documents.Wire.ProblemSeverity;
using WireCompletion = EtAlii.Adp.Editor.Wire.TextCompletion;
using WireFinding = EtAlii.Adp.Editor.Wire.TextFinding;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The editor family's own unary calls: the save, and asking an editor's module about the text
/// as typed (xml-editor Requirement 9, ruling A1). An editor tab's text arrives on
/// the shared stream (<see cref="DiagramService"/>'s Open, through the editor session adapter);
/// what only an editor does is asked here, so the diagram service carries no editor call
/// (spec 002, naming convention alignment).
/// </summary>
public sealed class EditorService : Editor.Wire.EditorService.EditorServiceBase
{
    private readonly ILogger _logger;

    private readonly IProjectStore _projectStore;
    private readonly IHistoryStackStore _historyStacks;
    private readonly EditorAnalyzers _analyzers;

    /// <summary>
    /// The most text an analysis or a completion is asked about. A request carries what a client
    /// sent, so it is bounded as the save's text is by the buffer it lands in (xml-editor
    /// Security). A million characters is far past any document the editors are for.
    /// </summary>
    public const int MaximumAnalyzedLength = 1_000_000;

    private const string TooLarge = "Too large to check.";
    public const string NoFile = "The file no longer exists.";
    private const string NoAnalysis = "This editor has no analysis.";

    public EditorService(IProjectStore projectStore, IHistoryStackStore historyStacks, EditorAnalyzers analyzers)
        : this(projectStore, historyStacks, analyzers, Log.ForContext<EditorService>())
    {
    }

    /// <summary>
    /// The logger is injectable, as <c>EditorResolver</c>'s is, so the once-with-the-path
    /// contract of a failed analysis stays assertable: an integration test that builds a real
    /// host replaces the shared pipeline, and a static field would bind to whichever pipeline
    /// was current when it was first touched.
    /// </summary>
    internal EditorService(IProjectStore projectStore, IHistoryStackStore historyStacks, EditorAnalyzers analyzers, ILogger logger)
    {
        _logger = logger;
        _projectStore = projectStore;
        _historyStacks = historyStacks;
        _analyzers = analyzers;
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

    public override async Task<AnalyzeTextResponse> Analyze(AnalyzeTextRequest request, ServerCallContext context)
    {
        if (!TryAnalyzer(request.ProjectId, request.Path, request.EditorId, request.Content, context, out var rootPath, out var fullPath, out var analyzer, out var refusal))
        {
            return new AnalyzeTextResponse { Error = refusal };
        }

        try
        {
            var analysis = await analyzer.AnalyzeAsync(rootPath, fullPath, request.Content, context.CancellationToken);
            var response = new AnalyzeTextResponse { Status = analysis.Status };
            response.Findings.AddRange(analysis.Findings.Select(ToWire));
            response.DependsOn.AddRange(analysis.DependsOn
                .Where(path => PathContainment.IsInside(rootPath, path))
                .Select(path => ToProjectPath(rootPath, path)));
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AnalyzeTextResponse { Error = Stopped(exception, request.Path) };
        }
    }

    public override async Task<CompleteTextResponse> Complete(CompleteTextRequest request, ServerCallContext context)
    {
        if (!TryAnalyzer(request.ProjectId, request.Path, request.EditorId, request.Content, context, out var rootPath, out var fullPath, out var analyzer, out var refusal))
        {
            return new CompleteTextResponse { Error = refusal };
        }

        if (request.Offset > request.Content.Length)
        {
            return new CompleteTextResponse { Error = "The position is outside the text." };
        }

        try
        {
            var items = await analyzer.CompleteAsync(rootPath, fullPath, request.Content, (int)request.Offset, request.Explicit, context.CancellationToken);
            var response = new CompleteTextResponse();
            response.Items.AddRange(items.Select(item => new WireCompletion
            {
                Label = item.Label,
                Detail = item.Detail,
                Info = item.Info,
                From = (uint)item.From,
                To = (uint)item.To,
                Insert = item.Insert,
                Boost = item.Boost,
            }));
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new CompleteTextResponse { Error = Stopped(exception, request.Path) };
        }
    }

    /// <summary>
    /// What both calls check before a module is asked: the text's size, the file inside the
    /// project, and an analyser for the editor. The refusal is the sentence the client shows.
    /// </summary>
    private bool TryAnalyzer(
        Documents.Wire.ShortGuid projectId,
        Path path,
        string editorId,
        string content,
        ServerCallContext context,
        out string rootPath,
        out string fullPath,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out IEditorAnalyzer? analyzer,
        out string refusal)
    {
        analyzer = null;
        rootPath = "";
        fullPath = "";

        if (content.Length > MaximumAnalyzedLength)
        {
            refusal = TooLarge;
            return false;
        }

        if (!ProjectTextFile.TryResolve(_projectStore, projectId, path, SessionContext.GetUserId(context), out rootPath, out fullPath))
        {
            refusal = NoFile;
            return false;
        }

        analyzer = _analyzers.Find(editorId);
        refusal = analyzer is null ? NoAnalysis : "";
        return analyzer is not null;
    }

    /// <summary>
    /// An analyser that throws is answered with its sentence and logged once, naming the file by
    /// its project-relative path and never logging the text (xml-editor Reliability).
    /// </summary>
    private string Stopped(Exception exception, Path path)
    {
        _logger.Error(exception, "Checking {Path} stopped", string.Join('/', path.Segments));
        return $"Checking stopped: {exception.Message}";
    }

    private static WireFinding ToWire(Editor.TextFinding finding) => new()
    {
        From = (uint)finding.From,
        To = (uint)finding.To,
        Severity = finding.Severity switch
        {
            TextFindingSeverity.Error => ProblemSeverity.Error,
            TextFindingSeverity.Warning => ProblemSeverity.Warning,
            _ => ProblemSeverity.Info,
        },
        Message = finding.Message,
        Source = finding.Source,
    };

    private static Path ToProjectPath(string rootPath, string fullPath)
    {
        var path = new Path();
        path.Segments.AddRange(System.IO.Path.GetRelativePath(rootPath, fullPath).Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        return path;
    }
}
