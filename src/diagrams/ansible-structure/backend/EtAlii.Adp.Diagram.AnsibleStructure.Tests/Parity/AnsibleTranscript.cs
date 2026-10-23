using System.Globalization;
using System.Text;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// The Ansible structure diagram's parity transcript: what today's hand-written code answers for
/// every folder of the corpus, frozen as <c>Parity/ansible-structure.transcript.json</c> before the
/// reader, the rule set or the property provider is changed (ansible-structure-disl-fbl,
/// Requirement 1). Every later step must reproduce it byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus</b>, each part in ordinal order: the four fixture trees under the test project's
/// <c>Fixtures/</c>; one folder per finding of the requirements marked <i>read</i>, under
/// <c>Parity/Folders/</c> or made by <see cref="AnsibleFindingFolders"/> where git cannot hold it;
/// and every registered folder (one holding a <c>.adp</c>) of the module's <c>examples/</c> and of
/// <c>src/examples/diagrams/ansible-structure/</c>. A folder whose files equal one recorded earlier,
/// path for path and byte for byte, is written as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Once</b>: the module's registrations, the toolbox it answers (empty), and the five refusals
/// a user can meet: a property write, a reparent, and a move of a relation, without a history and
/// without a registration.
/// </para>
/// <para>
/// <b>Per folder</b>: the elements a session delivers at baseline with an unbounded viewport, in
/// delivery order, each with its id, wire type, position and payload; the property rows of every
/// node, every relation and an unknown id; the findings with code, severity, file, line and
/// sentence, in order; the selection text and path of every delivered element; and a move of the
/// first node with the project's history, with the registration's text after it.
/// </para>
/// <para>
/// <b>Every folder is read from a copy in a temporary folder</b>, so that the move writes into a
/// registration nobody else reads, so that what git cannot hold can be made, and so that nothing
/// is written into a tree <c>ZeroWrites.Tests</c> holds. Every path the module reports is relative
/// to the folder, so where the copy lies cannot show; <see cref="Mask"/> replaces it all the same,
/// should a sentence ever carry it.
/// </para>
/// <para>
/// <b>Nothing else is masked</b>: nothing the module answers depends on a clock, a random number or
/// the machine. YamlDotNet's sentences stay as they are.
/// </para>
/// </remarks>
public sealed class AnsibleTranscript() : TranscriptAdapter("ansible-structure", AnsibleCorpus.TestProject, "ansible-structure.transcript.json")
{
    private const string Unknown = "parity:unknown";

    /// <summary>The registration a folder without one is given in its copy: the bare MIME line core writes.</summary>
    private const string BareRegistration = "ansible/structure\r\n";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromMessages(Wire.AnsibleElementPayload.Descriptor);

    public override string Origin => Diagram.AnsibleStructure.Origin.Key;

    /// <summary>The toolbox is answered by origin from the providers the module registers, and it registers none.</summary>
    protected override IReadOnlyList<ToolboxItemDefinition> Toolbox
    {
        get
        {
            using var provider = Services();
            return [.. provider.GetServices<IDiagramToolboxProvider>().Where(toolbox => toolbox.Origin == Diagram.AnsibleStructure.Origin).SelectMany(toolbox => toolbox.Items)];
        }
    }

    public override string DocumentsName => "folders";

    public override string DocumentName => "folder";

    /// <summary>Every folder, read as its manifest: so a folder whose files equal one recorded earlier, path for path and byte for byte, is <c>sameAs</c> it.</summary>
    public override IReadOnlyList<TranscriptDocument> Corpus() =>
        [.. AnsibleCorpus.Folders().Select(relative => new TranscriptDocument(relative, cancellationToken => ManifestAsync(Path.Combine(SourceFolder, relative), cancellationToken)))];

    public override async Task WriteBeforeDocumentsAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        json.WritePropertyName("registrations");
        json.WriteLines(Registrations());

        await base.WriteBeforeDocumentsAsync(json, cancellationToken);

        await using var provider = Services();
        json.WritePropertyName("refusals");
        json.WriteLines(await RefusalsAsync(provider.GetRequiredService<IHistoryStackStore>(), cancellationToken));
    }

    /// <summary>A folder that is not there has no hash and is never the same as another; what is made in a folder is part of what it is.</summary>
    public override async Task<string?> WriteIdentityAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, CancellationToken cancellationToken)
    {
        var source = Path.Combine(SourceFolder, document.Name);
        var makings = AnsibleFindingFolders.Makings.GetValueOrDefault(Path.GetFileName(source)) ?? [];
        string? sha = null;
        if (Directory.Exists(source))
        {
            sha = await base.WriteIdentityAsync(json, document, bytes, cancellationToken);
        }
        else
        {
            json.WriteString("tree", "none; made from nothing");
        }

        if (makings.Length > 0)
        {
            json.WritePropertyName("made");
            json.WriteLines(makings.Select(making => making.Line));
        }

        return sha is null ? null : $"{sha}|{string.Join("|", makings.Select(making => making.Line))}";
    }

    public override async Task WriteDocumentAsync(Utf8JsonWriter json, TranscriptDocument document, byte[] bytes, string folder, CancellationToken cancellationToken)
    {
        Make(Path.Combine(SourceFolder, document.Name), folder);

        var registration = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.adp").Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (!Directory.Exists(folder))
        {
            json.WriteString("registration", "none; the folder is not there");
            registration = Path.Combine(folder, AnsibleFindingFolders.RegistrationName);
        }
        else if (registration is null)
        {
            json.WriteString("registration", $"none; the copy is given {TranscriptText.Quoted(BareRegistration)}");
            registration = Path.Combine(folder, AnsibleFindingFolders.RegistrationName);
            await File.WriteAllTextAsync(registration, BareRegistration, cancellationToken);
        }
        else
        {
            json.WriteString("registration", Path.GetFileName(registration));
        }

        // Services of its own: a history is kept by the copy's folder, so no two folders ever shared one.
        await using var provider = Services();
        await WriteReadingAsync(json, folder, registration, provider.GetRequiredService<IHistoryStackStore>(), cancellationToken);
    }

    private static ServiceProvider Services() => new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddAnsibleStructure().BuildServiceProvider();

    /// <summary>Every service the module registers, as <c>service =&gt; implementation</c>, in registration order.</summary>
    private static List<string> Registrations() =>
    [
        .. new ServiceCollection().AddAnsibleStructure().Select(descriptor =>
            $"{descriptor.ServiceType.Name} => {descriptor.ImplementationType?.Name ?? (descriptor.ImplementationFactory is not null ? "factory" : "instance")} ({descriptor.Lifetime})"),
    ];

    /// <summary>
    /// The five sentences a user can be refused with, asked of the <c>infrastructure</c> fixture:
    /// a property write, a reparent, and a move of a relation, without a history and without a
    /// registration. None of them depends on the folder.
    /// </summary>
    private static async Task<List<string>> RefusalsAsync(IHistoryStackStore histories, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.AnsibleTranscript", Guid.NewGuid().ToString("N"));
        try
        {
            TestFolder.CopyTree(Path.Combine(AnsibleCorpus.TestFolderPath, "Fixtures", "infrastructure"), folder);
            var registration = Path.Combine(folder, AnsibleFindingFolders.RegistrationName);
            await File.WriteAllTextAsync(registration, BareRegistration, cancellationToken);

            using var folders = new AnsibleFolders();
            var model = folders.GetOrLoad(folder, registration).Disl.Diagram;
            var node = model.Nodes[0].Id;
            var relation = model.Relations[0].Id;

            var lines = new List<string>();
            var write = await new AnsibleContextPropertyProvider(folders).SetAsync(Target(registration, folder, node), "ansible.name", "Renamed", cancellationToken);
            lines.Add($"write ansible.name of {node} => {(write.IsSuccess ? "accepted" : $"refused: {write.Error}")}");

            await using (var session = new AnsibleSession(folder, folders, new AnsibleElementMapper(), registration, histories.Get(ProjectRoot.From(folder))))
            {
                lines.Add($"reparent {node} under {Unknown} => {Answer(await session.MoveElementAsync(node, Unknown, 0, cancellationToken))}");
                lines.Add($"move relation {relation} => {Answer(await session.MoveElementToAsync(relation, 10.5, -20.25, cancellationToken))}");
            }

            await using (var session = new AnsibleSession(folder, folders, new AnsibleElementMapper(), registration, history: null))
            {
                lines.Add($"move {node} without a history => {Answer(await session.MoveElementToAsync(node, 10.5, -20.25, cancellationToken))}");
            }

            await using (var session = new AnsibleSession(folder, folders, new AnsibleElementMapper(), registrationPath: null, histories.Get(ProjectRoot.From(folder))))
            {
                lines.Add($"move {node} without a registration => {Answer(await session.MoveElementToAsync(node, 10.5, -20.25, cancellationToken))}");
            }

            return [.. lines.Select(line => Mask(line, folder))];
        }
        finally
        {
            TestFolder.TryDelete(folder);
        }
    }

    /// <summary>
    /// The corpus folder <paramref name="relative"/> made in <paramref name="folder"/> as the transcript reads it:
    /// a copy of its files, or an empty folder for one that is not there, and then what <see cref="AnsibleFindingFolders"/>
    /// makes in it, which may take the folder away again.
    /// </summary>
    internal static void MakeCopy(string relative, string folder) => Make(Path.Combine(AnsibleCorpus.SourceFolder, relative), folder);

    private static void Make(string source, string folder)
    {
        if (Directory.Exists(source))
        {
            TestFolder.CopyTree(source, folder);
        }
        else
        {
            Directory.CreateDirectory(folder);
        }

        foreach (var making in AnsibleFindingFolders.Makings.GetValueOrDefault(Path.GetFileName(source)) ?? [])
        {
            making.Make(folder);
        }
    }

    private static async Task WriteReadingAsync(
        Utf8JsonWriter json, string folder, string registration, IHistoryStackStore histories, CancellationToken cancellationToken)
    {
        using var folders = new AnsibleFolders();
        var model = folders.GetOrLoad(folder, registration).Disl.Diagram;
        var mapper = new AnsibleElementMapper();

        // What a session delivers at baseline, in delivery order, with an unbounded viewport over the folder's own stored positions.
        List<DiagramElement> elements;
        await using (var session = new AnsibleSession(folder, folders, mapper, registration))
        {
            elements = [.. (await session.BaselineAsync(cancellationToken)).OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements)];
        }

        json.WritePropertyName("payloads");
        json.WriteLines([.. elements.Select(element => Mask(TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes), folder))]);

        var properties = new AnsibleContextPropertyProvider(folders);
        List<string> targets =
        [
            .. model.Nodes.Select(node => node.Id),
            .. model.Relations.Select(edge => edge.Id),
            Unknown,
        ];

        json.WriteStartArray("rows");
        foreach (var target in targets)
        {
            var rows = await properties.DescribeAsync(Target(registration, folder, target), cancellationToken);
            json.WriteStartObject();
            json.WriteString("target", target);
            json.WritePropertyName("rows");
            json.WriteLines([.. rows.Select(row => Mask(row.RowLine(), folder))]);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        var problems = await new AnsibleValidator().ValidateAsync(
            new DiagramValidationRequest(BareRegistration, Path.GetFileNameWithoutExtension(registration), ProjectRoot.From(folder), registration, registration) { SubjectFolder = folder },
            cancellationToken);
        json.WritePropertyName("findings");
        json.WriteLines([.. problems.Select(problem => Mask(FindingLine(problem), folder))]);

        json.WritePropertyName("selection");
        json.WriteLines(await SelectionAsync(elements, folder, registration, folders, mapper, cancellationToken));

        json.WritePropertyName("move");
        json.WriteLines(await MoveAsync(model, folder, registration, folders, histories, cancellationToken));
    }

    /// <summary>What selecting each delivered element resolves to: its path and its text, or why it is refused.</summary>
    private static async Task<List<string>> SelectionAsync(
        List<DiagramElement> elements, string folder, string registration, AnsibleFolders folders, AnsibleElementMapper mapper, CancellationToken cancellationToken)
    {
        if (!File.Exists(registration))
        {
            return [.. elements.Select(element => $"{element.Id} => not asked: there is no registration to select it in")];
        }

        var resolver = new AnsibleContextSourceResolver(new DiagramFileRouter(new AnsibleTestDiagramDefinitionCatalog(Diagram.AnsibleStructure)), folders, mapper);
        var parent = new ContextResolvedLevel(
            new ContextSource { EntryId = ShortGuid.NewShortGuid() },
            [],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, registration, IsContainer: false, SourceId: default, ProjectRoot.From(folder)),
            new ContextLevelDetail(),
            null!);

        var lines = new List<string>();
        foreach (var element in elements)
        {
            var resolution = await resolver.ResolveAsync(
                ShortGuid.NewShortGuid(), ProjectRoot.From(folder), new ContextSource { ElementId = new ElementId { Value = element.Id } }, [], parent, cancellationToken);
            lines.Add(resolution switch
            {
                ResolvedContextLevel resolved => $"{element.Id} => [{string.Join("/", resolved.Level.RelativePath)}] | {TranscriptText.Quoted(resolved.Level.Detail.Element.Text)}",
                RejectedContextLevel rejected => $"{element.Id} => rejected: {rejected.Reason}",
                _ => $"{element.Id} => {resolution.GetType().Name}",
            });
        }

        return [.. lines.Select(line => Mask(line, folder))];
    }

    /// <summary>The first node moved with the project's history, and the registration's text after it: what a drag stores, and what it keeps.</summary>
    private static async Task<List<string>> MoveAsync(
        DislDiagram model, string folder, string registration, AnsibleFolders folders, IHistoryStackStore histories, CancellationToken cancellationToken)
    {
        if (model.Nodes.Count == 0)
        {
            return ["no node to move"];
        }

        if (!File.Exists(registration))
        {
            return ["not asked: there is no registration to store a position in"];
        }

        var node = model.Nodes[0].Id;
        var lines = new List<string>();
        await using (var session = new AnsibleSession(folder, folders, new AnsibleElementMapper(), registration, histories.Get(ProjectRoot.From(folder))))
        {
            lines.Add($"move {node} to (10.5, -20.25) => {Answer(await session.MoveElementToAsync(node, 10.5, -20.25, cancellationToken))}");
        }

        lines.Add("registration after:");
        lines.AddRange(Lines(await File.ReadAllTextAsync(registration, cancellationToken)).Select(line => $"  {line}"));
        return [.. lines.Select(line => Mask(line, folder))];
    }

    private static string Answer(string answer) => answer.Length == 0 ? "accepted" : $"refused: {answer}";

    private static ContextTarget Target(string registration, string folder, string elementId) =>
        ContextTarget.ForElement(registration, elementId, ProjectRoot.From(folder), origin: Diagram.AnsibleStructure.Origin);

    private static string FindingLine(DiagramProblem problem)
    {
        var where = problem.Location is DiagramProblemFileLocation located
            ? $"{located.RelativePath}:{located.Line.ToString(CultureInfo.InvariantCulture)}"
            : problem.Location?.ToString() ?? "-";
        return $"{problem.RuleId} | {problem.Severity} | {where} | {TranscriptText.Quoted(problem.Message)}";
    }

    /// <summary>The copy's absolute location, should a sentence carry it: the one thing in a reading that differs per run.</summary>
    private static string Mask(string text, string folder) => text.Replace(folder, "{copy}", StringComparison.Ordinal);

    /// <summary>A text's lines without their endings, so a registration written with either convention reads the same.</summary>
    private static IEnumerable<string> Lines(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    /// <summary>A folder's files, path for path and byte for byte, in ordinal order of their relative paths: whose hash makes two folders the same.</summary>
    private static async Task<byte[]> ManifestAsync(string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var manifest = new StringBuilder();
        foreach ((string full, string relative) in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                     .Select(path => (Full: path, Relative: Path.GetRelativePath(folder, path).Replace('\\', '/')))
                     .OrderBy(path => path.Relative, StringComparer.Ordinal))
        {
            manifest.Append(relative).Append(' ').Append(TranscriptText.Sha256(await File.ReadAllBytesAsync(full, cancellationToken))).Append('\n');
        }

        return TranscriptText.Encode(manifest.ToString());
    }
}
