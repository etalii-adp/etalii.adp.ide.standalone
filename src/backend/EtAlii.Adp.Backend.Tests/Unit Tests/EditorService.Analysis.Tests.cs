using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Authentication;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Editor.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Workspace;
using Grpc.Core;
using Grpc.Core.Testing;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using EditorService = EtAlii.Adp.Workspace.EditorService;
using Path = EtAlii.Adp.Documents.Wire.Path;
using ProblemSeverity = EtAlii.Adp.Documents.Wire.ProblemSeverity;
using TextCompletion = EtAlii.Adp.Editor.TextCompletion;
using TextFinding = EtAlii.Adp.Editor.TextFinding;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The editor family's two analysis calls, answered through a fake analyser: what the service
/// checks before a module is asked, what it does when a module throws, and that the seam names
/// no format (ruling A1 of the text editors' design, xml-editor task 2).
/// </summary>
public sealed class EditorServiceAnalysisTests : IDisposable
{
    private static readonly ShortGuid UserId = ShortGuid.NewShortGuid();
    private static readonly ShortGuid ProjectId = ShortGuid.NewShortGuid();

    private readonly TestFolder _root = TestFolder.Create("editor-analysis");

    public EditorServiceAnalysisTests()
    {
        File.WriteAllText(_root.Combine("notes.txt"), "text");
        File.WriteAllText(_root.Combine("rules.txt"), "rules");
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task Analyze_AsksTheEditorsAnalyzer_AndAnswersWithProjectRelativeDependencies()
    {
        // Arrange.
        var analyzer = new FakeAnalyzer("fake")
        {
            Analysis = new TextAnalysis(
                [new TextFinding(1, 3, TextFindingSeverity.Warning, "Careful.", "fake")],
                "All read.",
                [_root.Combine("rules.txt"), "/elsewhere/outside.txt"]),
        };
        var service = Service(analyzer);

        // Act.
        var response = await service.Analyze(AnalyzeRequest("fake", "abcd"), Context());

        // Assert: the module was handed the text as sent, and its answer crosses unchanged -
        // apart from a dependency outside the project, which is never named to the client.
        Assert.Equal("abcd", analyzer.LastContent);
        Assert.Equal("", response.Error);
        Assert.Equal("All read.", response.Status);
        var finding = Assert.Single(response.Findings);
        Assert.Equal((1u, 3u, ProblemSeverity.Warning, "Careful.", "fake"), (finding.From, finding.To, finding.Severity, finding.Message, finding.Source));
        Assert.Equal(["rules.txt"], Assert.Single(response.DependsOn).Segments);
    }

    [Fact]
    public async Task Complete_AsksTheEditorsAnalyzer_AtTheOffset()
    {
        // Arrange.
        var analyzer = new FakeAnalyzer("fake") { Items = [new TextCompletion("item", "note", "doc", 2, 2, "<item/>", 5)] };
        var service = Service(analyzer);

        // Act.
        var response = await service.Complete(CompleteRequest("fake", "abcd", 2), Context());

        // Assert.
        Assert.Equal(2, analyzer.LastOffset);
        var item = Assert.Single(response.Items);
        Assert.Equal(("item", "note", "doc", 2u, 2u, "<item/>", 5), (item.Label, item.Detail, item.Info, item.From, item.To, item.Insert, item.Boost));
    }

    [Fact]
    public async Task APathOutsideTheProject_IsRefused_AndNoModuleIsAsked()
    {
        // Arrange.
        var analyzer = new FakeAnalyzer("fake");
        var service = Service(analyzer);
        var request = AnalyzeRequest("fake", "x");
        request.Path = new Path { Segments = { "..", "outside.txt" } };

        // Act.
        var analyzed = await service.Analyze(request, Context());
        var completed = await service.Complete(new CompleteTextRequest { ProjectId = ProjectId, Path = request.Path, EditorId = "fake", Content = "x" }, Context());

        // Assert.
        Assert.Equal(EditorService.NoFile, analyzed.Error);
        Assert.Equal(EditorService.NoFile, completed.Error);
        Assert.Null(analyzer.LastContent);
    }

    [Fact]
    public async Task AnEditorWithoutAnAnalyzer_IsAnsweredWithASentence()
    {
        // Arrange.
        var service = Service(new FakeAnalyzer("fake"));

        // Act.
        var analyzed = await service.Analyze(AnalyzeRequest("other", "x"), Context());
        var completed = await service.Complete(CompleteRequest("other", "x", 0), Context());

        // Assert.
        Assert.Equal("This editor has no analysis.", analyzed.Error);
        Assert.Equal("This editor has no analysis.", completed.Error);
    }

    [Fact]
    public async Task ATextOverTheBound_IsRefused_AndNoModuleIsAsked()
    {
        // Arrange.
        var analyzer = new FakeAnalyzer("fake");
        var service = Service(analyzer);
        var tooLarge = new string('x', EditorService.MaximumAnalyzedLength + 1);

        // Act.
        var analyzed = await service.Analyze(AnalyzeRequest("fake", tooLarge), Context());
        var completed = await service.Complete(CompleteRequest("fake", tooLarge, 0), Context());
        var atTheBound = await service.Analyze(AnalyzeRequest("fake", tooLarge[1..]), Context());

        // Assert: refused above the bound, asked at it.
        Assert.Equal("Too large to check.", analyzed.Error);
        Assert.Equal("Too large to check.", completed.Error);
        Assert.Equal("", atTheBound.Error);
        Assert.Equal(EditorService.MaximumAnalyzedLength, analyzer.LastContent?.Length);
    }

    [Fact]
    public async Task AnAnalyzerThatThrows_IsAnsweredWithItsSentence_AndLoggedOnceWithThePathAndNeverTheText()
    {
        // Arrange.
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger();
        var service = Service(logger, new FakeAnalyzer("fake") { Throws = new InvalidOperationException("The reader broke.") });

        // Act.
        var response = await service.Analyze(AnalyzeRequest("fake", "secret text"), Context());

        // Assert.
        Assert.Equal("Checking stopped: The reader broke.", response.Error);
        var logged = Assert.Single(events, logEvent => logEvent.Level == LogEventLevel.Error).RenderMessage(CultureInfo.InvariantCulture);
        Assert.Contains("notes.txt", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("secret text", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSeam_NamesNoFormat()
    {
        // Arrange: every file the seam is made of - the wire, the interface, its records and the
        // registry. EditorService itself is deliberately not read: its comments may say which
        // specification it serves, and what it does is held by the tests above.
        string[] seam =
        [
            RepositoryPaths.Under("src", "api", "editors.proto"),
            RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Editor", "IEditorAnalyzer.cs"),
            RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Editor", "_Model", "TextAnalysis.cs"),
            RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Editor", "_Model", "TextCompletion.cs"),
            RepositoryPaths.Under("src", "backend", "EtAlii.Adp.Workspace", "EditorAnalyzers.cs"),
        ];

        foreach (var file in seam)
        {
            // Act.
            var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);

            // Assert.
            var named = Regex.Matches(text, "xml|json|yaml", RegexOptions.IgnoreCase).Select(match => match.Value).ToArray();
            Assert.True(named.Length == 0, $"{file} names a format: {string.Join(", ", named)}");
        }
    }

    private EditorService Service(params IEditorAnalyzer[] analyzers) => Service(Logger.None, analyzers);

    private EditorService Service(ILogger logger, params IEditorAnalyzer[] analyzers) =>
        new(
            new OneProjectStore(new ProjectRecord(ProjectId, "project", new PathRecord(_root.Path.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)))),
            new UnusedHistory(),
            new EditorAnalyzers(analyzers),
            logger);

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

    private static AnalyzeTextRequest AnalyzeRequest(string editorId, string content) =>
        new() { ProjectId = ProjectId, Path = new Path { Segments = { "notes.txt" } }, EditorId = editorId, Content = content };

    private static CompleteTextRequest CompleteRequest(string editorId, string content, uint offset) =>
        new() { ProjectId = ProjectId, Path = new Path { Segments = { "notes.txt" } }, EditorId = editorId, Content = content, Offset = offset };

    private static ServerCallContext Context()
    {
        var context = TestServerCallContext.Create(
            method: "EditorService",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(5),
            requestHeaders: new Metadata(),
            cancellationToken: TestContext.Current.CancellationToken,
            peer: "test-peer",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => WriteOptions.Default,
            writeOptionsSetter: _ => { });
        SessionContext.SetUserId(context, UserId);
        return context;
    }

    private sealed class FakeAnalyzer(string editorId) : IEditorAnalyzer
    {
        public TextAnalysis Analysis { get; init; } = TextAnalysis.Empty;

        public IReadOnlyList<TextCompletion> Items { get; init; } = [];

        public Exception? Throws { get; init; }

        public string? LastContent { get; private set; }

        public int LastOffset { get; private set; } = -1;

        public string EditorId => editorId;

        public Task<TextAnalysis> AnalyzeAsync(string rootPath, string filePath, string content, CancellationToken cancellationToken)
        {
            LastContent = content;
            return Throws is null ? Task.FromResult(Analysis) : Task.FromException<TextAnalysis>(Throws);
        }

        public Task<IReadOnlyList<TextCompletion>> CompleteAsync(string rootPath, string filePath, string content, int offset, bool isExplicit, CancellationToken cancellationToken)
        {
            LastContent = content;
            LastOffset = offset;
            return Throws is null ? Task.FromResult(Items) : Task.FromException<IReadOnlyList<TextCompletion>>(Throws);
        }
    }

    private sealed class OneProjectStore(ProjectRecord project) : IProjectStore
    {
        public IReadOnlyList<ProjectRecord> List(ShortGuid userId) => [project];

        public ProjectRecord Add(ShortGuid userId, string name, PathRecord path) => throw new NotSupportedException();

        public void Remove(ShortGuid userId, ShortGuid projectId) => throw new NotSupportedException();
    }

    /// <summary>The analysis calls never touch the history; a call that did would throw here.</summary>
    private sealed class UnusedHistory : IHistoryStackStore
    {
#pragma warning disable CS0067 // Never raised: nothing here changes a history.
        public event EventHandler<HistoryChangedEventArgs>? Changed;
#pragma warning restore CS0067

        public IHistoryStack Get(string rootPath) => throw new NotSupportedException();

        public void Retain(string rootPath) => throw new NotSupportedException();

        public void Release(string rootPath) => throw new NotSupportedException();
    }
}
