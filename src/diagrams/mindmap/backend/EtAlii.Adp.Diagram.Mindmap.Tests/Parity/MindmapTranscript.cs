using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests.Parity;

/// <summary>
/// The mind map's parity transcript: what today's hand-written code answers for every document of
/// the corpus, frozen as <c>Parity/mindmap.transcript.json</c> before any of it is derived from the
/// bundled DISL definition. Every later switch-over must reproduce it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>: the genuine Freeplane fixture, the module's examples, their shipped copies under
/// <c>src/examples/diagrams/mindmap/</c> (written as <c>sameAs</c> while identical), and the inline
/// documents of <see cref="MindmapCorpus"/>.
/// </para>
/// <para>
/// <b>Per document</b>: the context menu and the property rows of every node, the empty id and an
/// unknown id, once in a view seeded from the file's folds and once in a view with every branch's fold
/// flipped; the findings; the payload of every mapped element; and a scripted edit sequence, each
/// step's answer and the hunks it made to the <c>.mm</c> file, then everything undone.
/// </para>
/// <para>
/// <b>Random ids are masked</b>: a node the store gives an id on open, and a node an add creates, has
/// a fresh <c>ID_</c> plus a short GUID. Each is written as <c>ID_new1</c>, <c>ID_new2</c>, ... in the
/// order it first appears, everywhere it appears, and the hashes are of the masked text.
/// </para>
/// </remarks>
internal static partial class MindmapTranscript
{
    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "mindmap.transcript.json";

    private const string TestProject = "EtAlii.Adp.Diagram.Mindmap.Tests";
    private const string Unknown = "parity:unknown";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(MindmapReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/mindmap</c>.</summary>
    private static string ModuleFolder { get; } = FindModuleFolder();

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    private static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The documents on disk, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    private static IReadOnlyList<string> Files()
    {
        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.mm");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.mm"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "mindmap")).SelectMany(folder => Directory.GetFiles(folder, "*.mm"));
        return [.. Sorted(fixtures), .. Sorted(examples), .. Sorted(shipped)];

        static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);
    }

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", Diagram.Mindmap.Origin.Key);
            json.WriteString("generator", $"src/diagrams/mindmap/backend/{TestProject}/Parity/MindmapTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in new MindmapToolboxProvider().Items)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var relative in Files())
            {
                await WriteDocumentAsync(json, relative, await File.ReadAllBytesAsync(Path.Combine(SourceFolder, relative), cancellationToken), recorded, cancellationToken);
            }

            foreach ((string name, string text) in MindmapCorpus.Inline)
            {
                await WriteDocumentAsync(json, name, TranscriptText.Encode(text), recorded, cancellationToken);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    private static async Task WriteDocumentAsync(Utf8JsonWriter json, string name, byte[] bytes, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var sha = TranscriptText.Sha256(bytes);

        json.WriteStartObject();
        json.WriteString("document", name);
        json.WriteString("sha256", sha);
        if (recorded.TryGetValue(sha, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[sha] = name;

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.MindmapTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var body = Path.Combine(folder, name[(name.LastIndexOf('/') + 1)..]);
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);

            using var session = new Session(folder, body, TranscriptText.Decode(bytes));
            await WriteReadingsAsync(json, session, cancellationToken);
            await WriteEditsAsync(json, session, cancellationToken);
        }
        finally
        {
            TestFolder.TryDelete(folder);
        }

        json.WriteEndObject();
    }

    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var document = session.TryDocument();
        List<string> targets = [.. document?.Nodes.Select(node => node.Id) ?? [], "", Unknown];

        // The second view folds every branch the file leaves open and opens every one it folds.
        if (document is not null)
        {
            var flipped = session.Views.For(session.FlippedWatch, session.Body, document);
            foreach (var node in document.Nodes.Where(node => node.HasChildren))
            {
                flipped.Toggle(node.Id);
            }
        }

        var menus = new List<IReadOnlyList<string>>();
        var lines = new List<(string Target, int Menu, int MenuFlipped, IReadOnlyList<string> Rows)>();
        foreach (var target in targets)
        {
            var menu = MenuIndex(menus, session.Mask(TranscriptText.MenuLines(await session.Actions.DiscoverAsync(session.Target(target), cancellationToken))));
            var flipped = MenuIndex(menus, session.Mask(TranscriptText.MenuLines(await session.Actions.DiscoverAsync(session.Target(target, flipped: true), cancellationToken))));
            IReadOnlyList<string> rows = session.Mask([.. (await session.Properties.DescribeAsync(session.Target(target), cancellationToken)).Select(row => TranscriptText.RowLine(row, []))]);
            lines.Add((target, menu, flipped, rows));
        }

        json.WriteStartArray("menus");
        foreach (var menu in menus)
        {
            WriteLines(json, menu);
        }

        json.WriteEndArray();

        json.WriteStartArray("targets");
        foreach ((string target, int menu, int flipped, _) in lines)
        {
            json.WriteStringValue($"{JsonSerializer.Serialize(session.Mask(target), TranscriptText.StringOptions)} => menu #{menu}, flipped folds #{flipped}");
        }

        json.WriteEndArray();

        json.WriteStartArray("rows");
        foreach ((string target, _, _, IReadOnlyList<string> rows) in lines)
        {
            if (rows.Count == 0)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteString("target", session.Mask(target));
            json.WritePropertyName("rows");
            WriteLines(json, rows);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        var problems = await new MindmapValidator().ValidateAsync(
            new DiagramValidationRequest(session.Original, Path.GetFileNameWithoutExtension(session.Body), session.Folder, session.Body, Path.ChangeExtension(session.Body, ".adp")),
            cancellationToken);
        json.WritePropertyName("findings");
        WriteLines(json, [.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]);

        json.WritePropertyName("payloads");
        if (document is null)
        {
            WriteLines(json, []);
        }
        else
        {
            var view = session.Views.For(session.Watch, session.Body, document);
            WriteLines(json, session.Mask([.. new MindmapElementMapper(MindmapMetrics.Default).Visible(document, view, DiagramViewport.Unbounded)
                .Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))]));
        }
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        null => "nowhere",
        DiagramProblemElementLocation element => $"element {element.Id}",
        _ => location.ToString() ?? "",
    };

    /// <summary>
    /// The scripted edit sequence: the property rows, the menus' actions and a drag's move, each run
    /// through the real history on a copy of the document, in an order that reaches refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var script = new Script(json, session, cancellationToken);
        await script.StartAsync();

        if (Root() is { } root)
        {
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.TextPropertyId, "Parity root");
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.NotesPropertyId, "First line.\nSecond line.");
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.NotesPropertyId, "Replaced.");
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.NotesPropertyId, "");
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.LinkPropertyId, "https://example.org/parity");
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.LinkPropertyId, "");
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.FoldedPropertyId, "Yes");
            await script.SetAsync(root.Id, MindmapContextPropertyProvider.IdentifierPropertyId, "ID_parity");

            if (await script.ExecuteAsync(root.Id, MindmapContextActionProvider.AddChildActionId) is { } child)
            {
                await script.CommitAsync(child, MindmapContextActionProvider.RenameActionId, "Parity child");
                await script.ExecuteAsync(child, MindmapContextActionProvider.AddChildActionId);
            }

            await script.ExecuteAsync(root.Id, MindmapContextActionProvider.AddSiblingActionId);
            await script.ExecuteAsync(root.Id, MindmapContextActionProvider.DeleteActionId);
            await script.ExecuteAsync(root.Id, MindmapContextActionProvider.UnlinkActionId);
            await script.CommitAsync(root.Id, MindmapContextActionProvider.AddChildActionId, "Committed child");
            await script.CommitAsync(root.Id, MindmapContextActionProvider.AddSiblingActionId, "No sibling");
            await script.CommitAsync(root.Id, MindmapContextActionProvider.DeleteActionId, "");
        }

        if (Leaf() is { } leaf)
        {
            if (await script.ExecuteAsync(leaf.Id, MindmapContextActionProvider.AddSiblingActionId) is { } sibling)
            {
                await script.CommitAsync(sibling, MindmapContextActionProvider.RenameActionId, "");
            }

            await script.CommitAsync(leaf.Id, MindmapContextActionProvider.AddSiblingActionId, "Committed sibling");
            await script.ExecuteAsync(leaf.Id, MindmapContextActionProvider.RenameActionId);
            await script.ExecuteAsync(leaf.Id, MindmapContextActionProvider.EditNotesActionId);
            await script.CommitAsync(leaf.Id, MindmapContextActionProvider.EditNotesActionId, "Leaf notes\nand more");
            await script.ExecuteAsync(leaf.Id, MindmapContextActionProvider.LinkActionId);
            await script.CommitAsync(leaf.Id, MindmapContextActionProvider.LinkActionId, Path.GetFileName(session.Body));
            await script.ExecuteAsync(leaf.Id, MindmapContextActionProvider.UnlinkActionId);
            await script.ExecuteAsync(leaf.Id, MindmapContextActionProvider.ToggleFoldActionId);
            await script.ExecuteAsync(leaf.Id, "mindmap.nonsense");
        }

        if (Branch() is { } branch)
        {
            await script.ExecuteAsync(branch.Id, MindmapContextActionProvider.ToggleFoldActionId);
            await script.ExecuteAsync(branch.Id, MindmapContextActionProvider.ToggleFoldActionId);
            if (Root() is { } top)
            {
                await script.MoveAsync(top.Id, branch.Id, 0);
                await script.MoveAsync(branch.Id, branch.Children[0].Id, 0);
                await script.MoveAsync(branch.Id, top.Id, 0);
                await script.MoveAsync(branch.Id, "ID_nowhere", 0);
            }

            if (Leaf() is { } moved && Root() is { } parent && moved.Parent?.Id != parent.Id)
            {
                await script.MoveAsync(moved.Id, parent.Id, -1);
            }

            if (await script.ExecuteAsync(branch.Id, MindmapContextActionProvider.DeleteActionId) is "confirm")
            {
                await script.CommitAsync(branch.Id, MindmapContextActionProvider.DeleteActionId, "");
            }
        }

        if (Leaf() is { } removed)
        {
            await script.ExecuteAsync(removed.Id, MindmapContextActionProvider.DeleteActionId);
        }

        await script.ExecuteAsync(Unknown, MindmapContextActionProvider.RenameActionId);
        await script.CommitAsync(Unknown, MindmapContextActionProvider.RenameActionId, "Nobody");
        await script.SetAsync(Unknown, MindmapContextPropertyProvider.TextPropertyId, "Nobody");
        await script.EndAsync();
        return;

        MindmapNode? Branch() => Document()?.Nodes.FirstOrDefault(node => node is { IsRoot: false, HasChildren: true });
        MindmapNode? Leaf() => Document()?.Nodes.FirstOrDefault(node => node is { IsRoot: false, HasChildren: false });
        MindmapNode? Root() => Document()?.Root;

        MindmapDocument? Document() => session.TryDocument();
    }

    private static int MenuIndex(List<IReadOnlyList<string>> menus, IReadOnlyList<string> menu)
    {
        var index = menus.FindIndex(known => known.SequenceEqual(menu, StringComparer.Ordinal));
        if (index >= 0)
        {
            return index;
        }

        menus.Add(menu);
        return menus.Count - 1;
    }

    private static void WriteLines(Utf8JsonWriter json, IEnumerable<string> lines)
    {
        json.WriteStartArray();
        foreach (var line in lines)
        {
            json.WriteStringValue(line);
        }

        json.WriteEndArray();
    }

    private static string FindModuleFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "diagrams", "mindmap");
            if (directory.Name == "mindmap" && Directory.Exists(Path.Combine(directory.FullName, "backend", TestProject)))
            {
                return directory.FullName;
            }

            if (Directory.Exists(Path.Combine(candidate, "backend", TestProject)))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No mindmap module folder above {AppContext.BaseDirectory}. That is a failure, not a skip.");
    }

    [GeneratedRegex("ID=\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex IdAttribute();

    /// <summary>One document's store, history, views and providers, with the masks of its random ids.</summary>
    private sealed class Session : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HashSet<string> _written;
        private readonly List<(string Id, string Mask)> _masks = [];

        public Session(string folder, string body, string original)
        {
            Folder = folder;
            Body = body;
            Original = original;
            _written = [.. IdAttribute().Matches(original).Select(match => match.Groups[1].Value)];
            _services = new ServiceCollection()
                .AddSingleton<MindmapViewState>()
                .AddSingleton<MindmapContextActionProvider>()
                .AddSingleton<MindmapContextPropertyProvider>()
                .AddCommands().AddHierarchyCommandHandlers()
                .AddMindmapCommands()
                .BuildServiceProvider();
            Learn();
        }

        public string Folder { get; }

        public string Body { get; }

        public string Original { get; }

        public ShortGuid Watch { get; } = ShortGuid.NewShortGuid();

        public ShortGuid FlippedWatch { get; } = ShortGuid.NewShortGuid();

        public IHistoryStack History => _services.GetRequiredService<IHistoryStackStore>().Get(Folder);

        private IMindmapDocumentStore Documents => _services.GetRequiredService<IMindmapDocumentStore>();

        public MindmapViewState Views => _services.GetRequiredService<MindmapViewState>();

        public MindmapContextActionProvider Actions => _services.GetRequiredService<MindmapContextActionProvider>();

        public MindmapContextPropertyProvider Properties => _services.GetRequiredService<MindmapContextPropertyProvider>();

        public MindmapDocument? TryDocument()
        {
            try
            {
                return Documents.GetOrLoad(Body);
            }
            catch (MindmapFormatException)
            {
                return null;
            }
        }

        public ContextTarget Target(string elementId, bool flipped = false) =>
            new(ContextScope.DiagramElement, Body, TryDocument()?.Find(elementId)?.HasChildren ?? false, SourceId: default, Folder, flipped ? FlippedWatch : Watch, elementId);

        /// <summary>Gives every id the document holds now that the file did not, and that has no mask yet, the next mask.</summary>
        public void Learn()
        {
            foreach (var node in TryDocument()?.Nodes ?? [])
            {
                Learn(node.Id);
            }
        }

        public void Learn(string id)
        {
            if (id.Length > 0 && !_written.Contains(id) && _masks.All(mask => mask.Id != id))
            {
                _masks.Add((id, $"ID_new{_masks.Count + 1}"));
            }
        }

        public string Mask(string text)
        {
            foreach ((string id, string mask) in _masks)
            {
                text = text.Replace(id, mask, StringComparison.Ordinal);
            }

            return text;
        }

        public IReadOnlyList<string> Mask(IReadOnlyList<string> lines) => [.. lines.Select(Mask)];

        public void Dispose() => _services.Dispose();
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private string _text = "";
        private string _original = "";

        public async Task StartAsync()
        {
            _original = _text = await ReadAsync();
            json.WriteStartArray("edits");
        }

        public async Task EndAsync()
        {
            json.WriteEndArray();

            var stack = session.History;
            var undone = 0;
            while (stack.CanUndo && (await stack.UndoAsync(cancellationToken)).IsSuccess)
            {
                undone++;
            }

            var restored = await ReadAsync();
            json.WriteString("undoAll", $"{undone} undone; {(string.Equals(restored, _original, StringComparison.Ordinal) ? "the original text is back" : "the original text is NOT back")}");
        }

        public async Task SetAsync(string target, string propertyId, string value)
        {
            var result = await session.Properties.SetAsync(session.Target(target), propertyId, value, cancellationToken);
            await RecordAsync($"set {target} {propertyId} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}", result.IsSuccess ? "ok" : $"refused: {result.Error}");
        }

        /// <summary>Runs an action; answers "confirm" when it asked for a confirmation, the new node's id when an add asks for its name, else null.</summary>
        public async Task<string?> ExecuteAsync(string target, string actionId)
        {
            var result = await session.Actions.ExecuteAsync(session.Target(target), actionId, cancellationToken);
            if (result is ContextExecutionRequiresInput { Request.CommitActionId.Length: > 0 } added)
            {
                session.Learn(added.Request.InlineLabelElementId);
            }

            await RecordAsync($"execute {actionId} on {target}", Describe(result));
            return result switch
            {
                ContextExecutionRequiresConfirmation => "confirm",
                ContextExecutionRequiresInput { Request.CommitActionId.Length: > 0 } input => input.Request.InlineLabelElementId,
                _ => null,
            };
        }

        public async Task CommitAsync(string target, string actionId, string value)
        {
            var result = await session.Actions.CommitAsync(session.Target(target), actionId, value, "", cancellationToken);
            await RecordAsync(
                $"commit {actionId} on {target} = {JsonSerializer.Serialize(value, TranscriptText.StringOptions)}",
                result.Completed ? "ok" : $"refused: {result.Error}");
        }

        /// <summary>A drag's re-parenting, as the session dispatches it: a move command on the history.</summary>
        public async Task MoveAsync(string node, string newParent, int index)
        {
            var result = await session.History.ExecuteAsync(new MoveNodeCommand(session.Body, node, newParent, index), cancellationToken);
            await RecordAsync($"move {node} under {newParent} at {index}", result.IsSuccess ? "ok" : $"refused: {result.Error}");
        }

        private async Task RecordAsync(string step, string answer)
        {
            session.Learn();
            var after = await ReadAsync();
            json.WriteStartObject();
            json.WriteString("do", session.Mask(step));
            json.WriteString("answer", session.Mask(answer));
            TranscriptText.WriteChange(json, session.Mask(_text), session.Mask(after));
            json.WriteEndObject();
            _text = after;
        }

        private async Task<string> ReadAsync() => TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));

        private static string Describe(ContextExecutionResult result) => result switch
        {
            ContextExecutionCompleted => "ok",
            ContextExecutionFailed failed => $"refused: {failed.Message}",
            ContextExecutionRequiresInput input => $"asks: {input.Request}",
            ContextExecutionRequiresConfirmation confirmation => $"confirms: {confirmation.Request}",
            ContextExecutionRequiresChoice choice => $"chooses: {choice.Request with { Options = [] }} options [{string.Join(", ", Options(choice.Request.Options))}]",
            _ => result.ToString(),
        };

        private static IEnumerable<string> Options(IReadOnlyList<ContextOptionNode> options) =>
            options.SelectMany(option => Options(option.Children ?? []).Prepend($"{option.Id}{(option.Selectable ? "" : " (folder)")}"));
    }
}
