using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The Databricks module's parity transcript: what today's hand-written code answers for every document
/// of the corpus, under each of the module's three readings, frozen as <c>Parity/databricks.transcript.json</c>
/// before any provider, reader or writer is changed (databricks-disl-fbl Requirement 1). Every later step
/// must reproduce it byte for byte, or change it only as that specification names.
/// </summary>
/// <remarks>
/// <para>
/// <b>Once</b>: the toolbox of each of the three tool types, the new document each writes for a handful of
/// base names (finding B20), and what a registration's <c>resource:</c> header is read as, for the ways of
/// writing one the module accepts and refuses (finding B21).
/// </para>
/// <para>
/// <b>The corpus</b>: the test project's <c>Fixtures</c> (the module's eleven and one document per finding
/// the specification marks as read, which <c>Fixtures/readme.md</c> names), the lakehouse example and its
/// copy under <c>src/examples/diagrams/databricks/</c>, each with the files of its folder, and last a new
/// document of each tool type, as the module's factory writes it. A document whose bytes and companions
/// equal one recorded earlier is written as <c>sameAs</c> that one.
/// </para>
/// <para>
/// <b>Per document</b>, read in a temporary folder of its own: the registrations beside it; the findings
/// under each tool type's validator, once, with the others named only where they differ (finding A7);
/// then <b>per reading</b>: the bundle, every job the file declares and every pipeline, each selected as a
/// registration's <c>resource:</c> header selects it, with no registration and with a header that names
/// nothing, and under every real registration beside the document. For each reading, the payload of every
/// element the session draws, and the selection text, the context menu and the property rows of every
/// element drawn, of the empty canvas, of a placement, of two connect gestures and of an unknown id, under
/// that reading's origin and registration. A reading that answers exactly as an earlier one is written as <c>sameAs</c> it.
/// </para>
/// <para>
/// <b>An edit script</b> that runs every command once, through the real history on a copy of the document:
/// moves under each reading, a move that a rename then strands (finding B16), the property rows and the
/// menu actions of the first job's first task, its dependencies, connects, removals and adds, the task
/// types only a command reaches (finding B17), the bundle's and the first pipeline's edits, and the
/// refusals. Each step is written with its answer and the hunks it made to the body and to the registration
/// it wrote; almost every step is then undone, and the step says whether the undo restored the text. A
/// value that writes a body nothing can read (finding B9) is followed by what the next edits answer. The
/// script ends by undoing everything left and saying whether the original text is back.
/// </para>
/// <para>
/// <b>Nothing is masked</b>: no id is minted and no clock is read, and no answer names the temporary folder
/// but by the document's own file name.
/// </para>
/// </remarks>
internal static class DatabricksTranscript
{
    /// <summary>The test project's folder name, under the module's <c>backend</c>.</summary>
    private const string TestProject = "EtAlii.Adp.Diagram.Databricks.Tests";

    /// <summary>The checked-in file's name, in this project's <c>Parity</c> folder.</summary>
    private const string FileName = "databricks.transcript.json";

    private const string Unknown = "parity:unknown";

    /// <summary>A resource key no document of the corpus declares.</summary>
    private const string MissingResource = "parity-missing";

    /// <summary>The registration the edit script's moves are written to.</summary>
    private const string ScriptedRegistration = "parity.adp";

    /// <summary>The base name every new document of the corpus is created with.</summary>
    private const string NewBaseName = "parity";

    private static readonly TypeRegistry _payloadTypes = TypeRegistry.FromFiles(DatabricksReflection.Descriptor);

    /// <summary>The module's folder, <c>src/diagrams/databricks</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "databricks");

    /// <summary><c>src/</c>, which every recorded path is relative to.</summary>
    private static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The checked-in transcript.</summary>
    public static string CheckedInPath => Path.Combine(ModuleFolder, "backend", TestProject, "Parity", FileName);

    /// <summary>The three tool types, in the order discovery lists them.</summary>
    private static readonly ToolOrigin[] _origins = [.. Diagram.Definitions.Select(definition => definition.Origin)];

    /// <summary>The base names each type's new document is written for (finding B20).</summary>
    private static readonly string[] _newBaseNames = ["parity", "Nightly Ingest.job", "2024", "Ünïcode \"quoted\"", "null", "--", " "];

    /// <summary>
    /// The ways a registration's <c>resource:</c> header is written that the transcript records the reading
    /// of (finding B21): its case, its spacing, an empty value, and where in the registration it stands.
    /// </summary>
    private static readonly string[] _resourceHeaders =
    [
        "databricks/job\nbody: a.yml\nresource: job_b\n",
        "databricks/job\nbody: a.yml\nRESOURCE:job_b\n",
        "databricks/job\nbody: a.yml\nResource:    job_b   \n",
        "databricks/job\n\n\nresource: job_b\n",
        "databricks/job\nbody: a.yml\nresource:\n",
        "databricks/job\nbody: a.yml\nresources: job_b\n",
        "databricks/job\nbody: a.yml\nlayout:\n  task:x: 1 2\nresource: job_b\n",
        "databricks/job\nh1: 1\nh2: 2\nh3: 3\nh4: 4\nh5: 5\nh6: 6\nh7: 7\nresource: job_b\n",
        "databricks/job\nh1: 1\nh2: 2\nh3: 3\nh4: 4\nh5: 5\nh6: 6\nh7: 7\nh8: 8\nresource: job_b\n",
        "databricks/job\r\nbody: a.yml\r\nresource: job_b\r\n",
    ];

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", "databricks");
            json.WriteString("generator", $"src/diagrams/databricks/backend/{TestProject}/Parity/DatabricksTranscript.cs");
            json.WriteString("regenerate", "ADP_WRITE_PARITY_TRANSCRIPTS=1 dotnet test --project <this test project> (then review the diff)");

            await using (var services = Services())
            {
                // The toolbox does not read the document, so it is written once per tool type.
                json.WriteStartObject("toolbox");
                foreach (var toolbox in services.GetServices<IDiagramToolboxProvider>())
                {
                    json.WritePropertyName(toolbox.Origin.Key);
                    json.WriteLines(toolbox.Items.Select(item => $"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}"));
                }

                json.WriteEndObject();

                json.WriteStartObject("newDocuments");
                foreach (var factory in services.GetServices<IDiagramDocumentFactory>())
                {
                    json.WriteStartObject(factory.Origin.Key);
                    foreach (var baseName in _newBaseNames)
                    {
                        json.WriteString(baseName, NewDocument(factory, baseName));
                    }

                    json.WriteEndObject();
                }

                json.WriteEndObject();
            }

            await WriteResourceHeadersAsync(json, cancellationToken);

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var document in await DocumentsAsync(cancellationToken))
            {
                await WriteDocumentAsync(json, document, recorded, cancellationToken);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    /// <summary>The module's services, with the core ones its commands and history need.</summary>
    private static ServiceProvider Services() =>
        new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands()
            .AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();

    private static string NewDocument(IDiagramDocumentFactory factory, string baseName)
    {
        try
        {
            return factory.CreateEmptyDocument(baseName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Thrown(exception);
        }
    }

    private static string Thrown(Exception exception) => $"throws {exception.GetType().Name}: {exception.Message}";

    /// <summary>What each written form of the <c>resource:</c> header is read as (finding B21).</summary>
    private static async Task WriteResourceHeadersAsync(Utf8JsonWriter json, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.DatabricksTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var lines = new List<string>();
            var registration = Path.Combine(folder, "header.adp");
            foreach (var text in _resourceHeaders)
            {
                await File.WriteAllTextAsync(registration, text, cancellationToken);
                lines.Add($"{TranscriptText.Quoted(text)} => {TranscriptText.Quoted(DatabricksHeaders.ReadResourceKey(registration))}");
            }

            json.WritePropertyName("resourceHeaders");
            json.WriteLines(lines);
        }
        finally
        {
            TestFolder.TryDelete(folder);
        }
    }

    /// <summary>
    /// One document of the corpus: its recorded name, its bytes, and the files that travel with it, each
    /// as its name in the document's folder and its bytes.
    /// </summary>
    private sealed record Document(string Relative, byte[] Bytes, IReadOnlyList<(string Name, byte[] Bytes)> Companions);

    /// <summary>Every document of the corpus, in the order it is recorded.</summary>
    private static async Task<IReadOnlyList<Document>> DocumentsAsync(CancellationToken cancellationToken)
    {
        var documents = new List<Document>();
        var fixtures = Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures");
        foreach (var body in Sorted(Directory.GetFiles(fixtures).Where(IsBody)))
        {
            documents.Add(new Document(RelativeToSource(body), await File.ReadAllBytesAsync(body, cancellationToken), []));
        }

        var examples = Directory.GetFiles(Path.Combine(ModuleFolder, "examples"), "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(SourceFolder, "examples", "diagrams", "databricks"), "*", SearchOption.AllDirectories))
            .Where(IsBody);
        foreach (var body in Sorted(examples))
        {
            var companions = new List<(string, byte[])>();
            foreach (var file in Sorted(Directory.GetFiles(Path.GetDirectoryName(body)!)).Where(file => !string.Equals(file, body, StringComparison.Ordinal)))
            {
                companions.Add((Path.GetFileName(file), await File.ReadAllBytesAsync(file, cancellationToken)));
            }

            documents.Add(new Document(RelativeToSource(body), await File.ReadAllBytesAsync(body, cancellationToken), companions));
        }

        await using var services = Services();
        foreach (var factory in services.GetServices<IDiagramDocumentFactory>())
        {
            var extension = Diagram.Definitions.Single(definition => definition.Origin == factory.Origin).Extension;
            documents.Add(new Document($"new {factory.Origin.Key}/{NewBaseName}{extension}", TranscriptText.Encode(factory.CreateEmptyDocument(NewBaseName)), []));
        }

        return documents;

        static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.Order(StringComparer.Ordinal);
    }

    private static bool IsBody(string path) => Path.GetExtension(path) is ".yml" or ".yaml" or ".json";

    /// <summary>A path as the transcript names it: relative to <c>src/</c>, with forward slashes.</summary>
    private static string RelativeToSource(string path) => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/');

    private static async Task WriteDocumentAsync(Utf8JsonWriter json, Document document, Dictionary<string, string> recorded, CancellationToken cancellationToken)
    {
        var sha = TranscriptText.Sha256(document.Bytes);
        json.WriteStartObject();
        json.WriteString("document", document.Relative);
        json.WriteString("sha256", sha);
        if (document.Companions.Count > 0)
        {
            json.WritePropertyName("beside");
            json.WriteLines(document.Companions.Select(companion => $"{companion.Name} | {TranscriptText.Sha256(companion.Bytes)}"));
        }

        // A copy is the same reading only where everything beside it is the same too.
        var key = string.Join(" ", document.Companions.Select(companion => $"{companion.Name}={TranscriptText.Sha256(companion.Bytes)}").Prepend(sha));
        if (recorded.TryGetValue(key, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[key] = document.Relative;

        var folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.DatabricksTranscript", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A new document is named for its type; every other keeps the name it was read from.
            var name = Path.GetFileName(document.Relative);
            var body = Path.Combine(folder, name);
            await File.WriteAllBytesAsync(body, document.Bytes, cancellationToken);
            foreach ((string companion, byte[] bytes) in document.Companions)
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, companion), bytes, cancellationToken);
            }

            await using var session = new Session(folder, body);
            await session.ReadAsync(cancellationToken);
            await WriteRegistrationsAsync(json, session, cancellationToken);
            await WriteFindingsAsync(json, session, cancellationToken);
            await WriteReadingsAsync(json, session, cancellationToken);
            await WriteEditsAsync(json, session, cancellationToken);
        }
        finally
        {
            TestFolder.TryDelete(folder);
        }

        json.WriteEndObject();
    }

    /// <summary>The registrations beside the document whose <c>body:</c> names it: the tool type each opens and the resource its header selects.</summary>
    private static async Task WriteRegistrationsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach ((string path, string origin) in await session.RegistrationsAsync(cancellationToken))
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            lines.Add($"{Path.GetFileName(path)} | {TranscriptText.Quoted(text)} => {origin}, resource {TranscriptText.Quoted(DatabricksHeaders.ReadResourceKey(path))}");
        }

        if (lines.Count == 0)
        {
            return;
        }

        json.WritePropertyName("registrations");
        json.WriteLines(lines);
    }

    /// <summary>The findings under each tool type's validator: written once, and again for an origin only where it differs (finding A7).</summary>
    private static async Task WriteFindingsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(session.Body, cancellationToken);
        var findings = new List<(string Origin, IReadOnlyList<string> Lines)>();
        foreach (var origin in _origins)
        {
            var problems = await new DatabricksValidator(origin).ValidateAsync(
                new DiagramValidationRequest(text, Path.GetFileNameWithoutExtension(session.Body), ProjectRoot.From(session.Folder), session.Body, null),
                cancellationToken);
            findings.Add((origin.Key, [.. problems.Select(problem => $"{Location(problem.Location)} | {problem.Severity} | {problem.RuleId} | {problem.Message}")]));
        }

        json.WritePropertyName("findings");
        json.WriteLines(findings[0].Lines);
        foreach ((string origin, IReadOnlyList<string> lines) in findings.Skip(1).Where(each => !each.Lines.SequenceEqual(findings[0].Lines, StringComparer.Ordinal)))
        {
            json.WritePropertyName($"findings {origin}");
            json.WriteLines(lines);
        }
    }

    private static string Location(DiagramProblemLocation? location) => location switch
    {
        DiagramProblemLineLocation line => $"line {line.Number}",
        DiagramProblemElementLocation element => $"element {TranscriptText.Quoted(element.Id)}",
        null => "nowhere",
        _ => location.ToString(),
    };

    /// <summary>One way of opening the document: a tool type, and the registration it is opened through, if any.</summary>
    private sealed record Opening(ToolOrigin Origin, string Label, string? Registration);

    /// <summary>
    /// Every reading of the document: per tool type, with no registration, with a header naming each resource
    /// of its kind and one naming nothing, and through every registration beside it; then the tables their
    /// targets refer into.
    /// </summary>
    private static async Task WriteReadingsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var menus = new List<IReadOnlyList<string>>();
        var choices = new List<IReadOnlyList<string>>();
        var rowSets = new List<IReadOnlyList<string>>();
        var readings = new List<(Opening Opening, IReadOnlyList<string> Payloads, IReadOnlyList<string> Targets, string? SameAs)>();
        var openings = await OpeningsAsync(session, cancellationToken);
        foreach (var opening in openings)
        {
            var elements = await session.DrawAsync(opening.Origin, opening.Registration, cancellationToken);
            IReadOnlyList<string> payloads = [.. elements.Select(element => TranscriptText.PayloadLine(element.Id, element.X, element.Y, element.Type, element.PayloadTypeUrl, element.Payload, _payloadTypes))];
            var targets = new List<string>();
            foreach (var target in Targets(elements))
            {
                targets.Add(await TargetLineAsync(session, target, opening, menus, choices, rowSets, cancellationToken));
            }

            var same = readings.FirstOrDefault(earlier => earlier.SameAs is null
                && earlier.Opening.Origin == opening.Origin
                && earlier.Payloads.SequenceEqual(payloads, StringComparer.Ordinal)
                && earlier.Targets.SequenceEqual(targets, StringComparer.Ordinal));
            readings.Add((opening, payloads, targets, same.Opening?.Label));
        }

        json.WriteStartArray("readings");
        foreach ((Opening opening, IReadOnlyList<string> payloads, IReadOnlyList<string> targets, string? sameAs) in readings)
        {
            json.WriteStartObject();
            json.WriteString("reading", opening.Label);
            if (sameAs is not null)
            {
                json.WriteString("sameAs", sameAs);
            }
            else
            {
                json.WritePropertyName("payloads");
                json.WriteLines(payloads);
                json.WritePropertyName("targets");
                json.WriteLines(targets);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("menus");
        foreach (var menu in menus)
        {
            json.WriteLines(menu);
        }

        json.WriteEndArray();

        json.WriteStartArray("choices");
        foreach (var list in choices)
        {
            json.WriteLines(list);
        }

        json.WriteEndArray();

        json.WriteStartArray("rowSets");
        foreach (var rows in rowSets)
        {
            json.WriteLines(rows);
        }

        json.WriteEndArray();
    }

    /// <summary>The ways the document is opened (finding B2): see <see cref="WriteReadingsAsync"/>.</summary>
    private static async Task<IReadOnlyList<Opening>> OpeningsAsync(Session session, CancellationToken cancellationToken)
    {
        var model = session.Model;
        var beside = await session.RegistrationsAsync(cancellationToken);
        var openings = new List<Opening>();
        foreach (var origin in _origins)
        {
            openings.Add(new Opening(origin, $"{origin.Key} | no registration", null));
            IEnumerable<string> keys = origin.Type switch
            {
                "job" => model.Jobs.Select(job => job.Key),
                "pipeline" => model.Pipelines.Select(pipeline => pipeline.Key).Where(key => key.Length > 0),
                _ => [],
            };
            if (origin.Type != "bundle")
            {
                foreach (var key in keys.Append(MissingResource).Distinct(StringComparer.Ordinal))
                {
                    var registration = Path.Combine(session.Folder, "readings", $"{origin.Type}-{openings.Count}.adp");
                    Directory.CreateDirectory(Path.GetDirectoryName(registration)!);
                    await File.WriteAllTextAsync(registration, $"{origin.Key}\nbody: ../{Path.GetFileName(session.Body)}\nresource: {key}\n", cancellationToken);
                    openings.Add(new Opening(origin, $"{origin.Key} | resource: {key}", registration));
                }
            }

            openings.AddRange(beside
                .Where(registration => registration.Origin == origin.Key)
                .Select(registration => new Opening(origin, $"{origin.Key} | {Path.GetFileName(registration.Path)}", registration.Path)));
        }

        return openings;
    }

    /// <summary>
    /// The targets a reading's menus and rows are written for: everything it draws, the empty canvas, a
    /// placement, a connect gesture between the first two boxes it draws and one from a placement, and an
    /// unknown id.
    /// </summary>
    private static IEnumerable<string> Targets(IReadOnlyList<DiagramElement> elements)
    {
        var boxes = elements.Where(element => !element.Type.EndsWith("+edge", StringComparison.Ordinal)).Select(element => element.Id).ToList();
        var placement = GestureIds.Placement(120, 80);
        List<string> gestures =
        [
            GestureIds.Relation(boxes.ElementAtOrDefault(0) ?? "task:a", boxes.ElementAtOrDefault(1) ?? "task:b"),
            GestureIds.Relation(placement, boxes.ElementAtOrDefault(0) ?? "task:a"),
        ];
        return elements.Select(element => element.Id).Append("").Append(placement).Concat(gestures).Append(Unknown).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// One target as a line: what a selection of it reads, and its menu and its rows, each as an index into the
    /// document's tables. The target carries the opening's registration, as the host resolves one through it.
    /// </summary>
    private static async Task<string> TargetLineAsync(
        Session session,
        string target,
        Opening opening,
        List<IReadOnlyList<string>> menus,
        List<IReadOnlyList<string>> choices,
        List<IReadOnlyList<string>> rowSets,
        CancellationToken cancellationToken)
    {
        var selection = DatabricksSelection.Describe(session.Model, target);
        var resolved = session.Target(target, opening.Origin) with { RegistrationPath = opening.Registration };
        var menu = menus.IndexOfOrAdd((await session.Actions.DiscoverAsync(resolved, cancellationToken)).MenuLines());
        IReadOnlyList<string> rows = [.. (await session.Properties.DescribeAsync(resolved, cancellationToken)).Select(row => row.RowLine(choices))];
        return $"{TranscriptText.Quoted(target)} => selects {TranscriptText.Quoted(selection)}, menu #{menu}, {(rows.Count == 0 ? "no rows" : $"rows #{rowSets.IndexOfOrAdd(rows)}")}";
    }

    /// <summary>
    /// The scripted edit sequence: every command of the module once, through the real history on a copy of
    /// the document, in an order that reaches its refusals too.
    /// </summary>
    private static async Task WriteEditsAsync(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        var model = session.Model;
        var script = new Script(json, session, cancellationToken);
        var original = await script.StartAsync();

        await WriteMovesAsync(script, session, model, cancellationToken);
        if (model.Jobs.FirstOrDefault() is { } job)
        {
            await WriteJobEditsAsync(script, model, job);
        }

        await WriteBundleEditsAsync(script, model);
        if (model.Pipelines.FirstOrDefault() is { } pipeline)
        {
            await WritePipelineEditsAsync(script, pipeline);
        }

        // An id nothing in the file carries.
        await script.ExecuteAsync(Unknown, DatabricksContextActionProvider.RenameTaskActionId, Mode.Revert);
        await script.CommitAsync(Unknown, DatabricksContextActionProvider.RenameTaskActionId, "parity_nobody", Mode.Revert);
        await script.SetAsync(Unknown, DatabricksContextPropertyProvider.RunIfProperty, "ALL_DONE", Mode.Revert);

        await script.EndAsync(original);
    }

    /// <summary>
    /// The moves: under each reading, of the first box it draws, with the session's own registration; the
    /// refusals of an edge, of a session opened without a registration and of one opened without the
    /// history; and a move a rename then strands (finding B16).
    /// </summary>
    private static async Task WriteMovesAsync(Script script, Session session, DatabricksModel model, CancellationToken cancellationToken)
    {
        foreach (var origin in _origins)
        {
            var registration = Path.Combine(session.Folder, $"{origin.Type}.{ScriptedRegistration}");
            await script.RegisterAsync(origin, registration);
            var elements = await session.DrawAsync(origin, registration, cancellationToken);
            var boxes = elements.Where(element => !element.Type.EndsWith("+edge", StringComparison.Ordinal)).ToList();
            if (boxes.FirstOrDefault() is { } box)
            {
                await script.MoveAsync(origin, registration, box.Id, 120, 80.5, origin.Type == "job" ? Mode.RevertWithRedo : Mode.Revert);
            }

            // The stub of a missing upstream task is a box of its own (finding A9).
            if (boxes.FirstOrDefault(element => element.Type == DatabricksElementMapper.TaskType
                    && DatabricksTaskPayload.Parser.ParseFrom(element.Payload.Span).Unresolved) is { } stub)
            {
                await script.MoveAsync(origin, registration, stub.Id, 300, 40, Mode.Revert);
            }

            if (elements.FirstOrDefault(element => element.Type.EndsWith("+edge", StringComparison.Ordinal)) is { } edge)
            {
                await script.MoveAsync(origin, registration, edge.Id, 10, 10, Mode.Revert);
            }
        }

        var first = model.Jobs.FirstOrDefault()?.Tasks.FirstOrDefault(task => task.Key.Length > 0);
        var target = first is null ? "bundle" : $"task:{first.Key}";
        var job = ServiceCollectionAddDatabricksExtension.JobOrigin;
        await script.MoveAsync(job, null, target, 10, 10, Mode.Revert);
        await script.MoveReadOnlyAsync(job, Path.Combine(session.Folder, $"job.{ScriptedRegistration}"), target);

        if (first is not null)
        {
            // The position stored under the old key goes stale: the rename does not touch the registration.
            var registration = Path.Combine(session.Folder, $"job.{ScriptedRegistration}");
            await script.MoveAsync(job, registration, target, 200, 100, Mode.Keep);
            await script.CommitAsync(target, DatabricksContextActionProvider.RenameTaskActionId, "parity_moved", Mode.Keep);
            await script.UndoAsync();
            await script.UndoAsync();
        }
    }

    /// <summary>The edits of the first job: its first task's rows and actions, dependencies, connects, removals and adds.</summary>
    private static async Task WriteJobEditsAsync(Script script, DatabricksModel model, JobModel job)
    {
        var keyed = job.Tasks.Where(task => task.Key.Length > 0).ToList();
        var placement = GestureIds.Placement(120, 80);
        if (keyed.FirstOrDefault() is { } first)
        {
            var target = $"task:{first.Key}";
            var cluster = job.Clusters.FirstOrDefault()?.Key ?? "parity_cluster";
            var otherJobKey = model.Jobs.Skip(1).SelectMany(other => other.Tasks).Select(task => task.Key)
                .FirstOrDefault(key => key.Length > 0 && job.Tasks.All(task => task.Key != key));

            await script.SetAsync(target, DatabricksContextPropertyProvider.RunIfProperty, "ALL_DONE", Mode.RevertWithRedo);
            await script.SetAsync(target, DatabricksContextPropertyProvider.RunIfProperty, "", Mode.Revert);
            await script.SetAsync(target, DatabricksContextPropertyProvider.RunIfProperty, "yes", Mode.Revert);
            await script.SetAsync(target, "databricks.cluster", cluster, Mode.Revert);
            await script.SetAsync(target, "databricks.cluster", "parity_missing_cluster", Mode.Revert);
            await script.SetAsync(target, "databricks.cluster", "", Mode.Revert);
            await script.SetAsync(target, DatabricksContextPropertyProvider.TaskKeyProperty, "parity_key", Mode.Revert);
            await script.SetAsync(target, DatabricksContextPropertyProvider.TaskTypeProperty, "python", Mode.Revert);
            await script.SetAsync(target, DatabricksContextPropertyProvider.WorkspaceProperty, "connected", Mode.Revert);

            await script.ExecuteAsync(target, DatabricksContextActionProvider.RenameTaskActionId, Mode.Revert);
            await script.ExecuteAsync(target, DatabricksContextActionProvider.SetRunIfActionId, Mode.Revert);
            await script.ExecuteAsync(target, DatabricksContextActionProvider.AssignClusterActionId, Mode.Revert);
            await script.ValidateAsync(target, DatabricksContextActionProvider.RenameTaskActionId, " ");
            await script.ValidateAsync(target, DatabricksContextActionProvider.RenameTaskActionId, first.Key);
            await script.ValidateAsync(target, DatabricksContextActionProvider.RenameTaskActionId, "parity_new");
            if (otherJobKey is not null)
            {
                // The dialog refuses a key another job uses; the writer does not (finding A13).
                await script.ValidateAsync(target, DatabricksContextActionProvider.RenameTaskActionId, otherJobKey);
                await script.CommitAsync(target, DatabricksContextActionProvider.RenameTaskActionId, otherJobKey, Mode.Revert);
            }

            await script.CommitAsync(target, DatabricksContextActionProvider.RenameTaskActionId, "parity_renamed", Mode.RevertWithRedo);
            await script.CommitAsync(target, DatabricksContextActionProvider.RenameTaskActionId, "123", Mode.Revert);
            await script.CommitAsync(target, DatabricksContextActionProvider.RenameTaskActionId, "", Mode.Revert);
            if (keyed.Skip(1).FirstOrDefault() is { } second)
            {
                await script.CommitAsync(target, DatabricksContextActionProvider.RenameTaskActionId, second.Key, Mode.Revert);
            }

            await script.CommitAsync(target, DatabricksContextActionProvider.SetRunIfActionId, "AT_LEAST_ONE_SUCCESS", Mode.Revert);
            await script.CommitAsync(target, DatabricksContextActionProvider.AssignClusterActionId, cluster, Mode.Revert);
            await script.CommitAsync(target, DatabricksContextActionProvider.AssignClusterActionId, "", Mode.Revert);
            await script.ExecuteAsync(target, DatabricksContextActionProvider.SimulatedRunActionId, Mode.Revert);
            await script.ExecuteAsync(target, "databricks.parity-unknown", Mode.Revert);

            // A value written raw can leave a body nothing can read, and every edit is then withheld until
            // undo (finding B9): an alias of an anchor the file does not have, and an unclosed flow sequence.
            foreach (var unreadable in (string[])["*nothing", "[x"])
            {
                await script.SetAsync(target, DatabricksContextPropertyProvider.RunIfProperty, unreadable, Mode.Keep);
                await script.ExecuteAsync(target, DatabricksContextActionProvider.RenameTaskActionId, Mode.Revert);
                await script.SetAsync(target, DatabricksContextPropertyProvider.RunIfProperty, "ALL_DONE", Mode.Revert);
                await script.UndoAsync();
            }
        }

        // Dependencies: one disconnect per upstream entry, the edge's own removal, and the refusals.
        if (keyed.FirstOrDefault(task => task.DependsOn.Count > 0) is { } dependent)
        {
            var target = $"task:{dependent.Key}";
            var upstream = dependent.DependsOn[0].TaskKey;
            await script.ExecuteAsync(target, $"{DatabricksContextActionProvider.DisconnectActionPrefix}{upstream}", Mode.RevertWithRedo);
            await script.ExecuteAsync(target, $"{DatabricksContextActionProvider.DisconnectActionPrefix}parity_nobody", Mode.Revert);
            await script.ExecuteAsync($"edge:{upstream}->{dependent.Key}", "databricks.remove-edge", Mode.Revert);
            await script.ExecuteAsync(GestureIds.Relation($"task:{upstream}", target), DatabricksContextActionProvider.ConnectActionId, Mode.Revert);
        }

        // Connects: a new one, to itself, from a placement, and to a task that is not there.
        if (keyed.Count >= 2)
        {
            var from = keyed[^1];
            var to = keyed.FirstOrDefault(task => task.Key != from.Key && task.DependsOn.All(dependency => dependency.TaskKey != from.Key));
            if (to is not null)
            {
                await script.ExecuteAsync(GestureIds.Relation($"task:{from.Key}", $"task:{to.Key}"), DatabricksContextActionProvider.ConnectActionId, Mode.RevertWithRedo);
            }
        }

        if (keyed.FirstOrDefault() is { } only)
        {
            await script.ExecuteAsync(GestureIds.Relation($"task:{only.Key}", $"task:{only.Key}"), DatabricksContextActionProvider.ConnectActionId, Mode.Revert);
            await script.ExecuteAsync(GestureIds.Relation(placement, $"task:{only.Key}"), DatabricksContextActionProvider.ConnectActionId, Mode.Revert);
            await script.ExecuteAsync(GestureIds.Relation($"task:{only.Key}", "task:parity_missing"), DatabricksContextActionProvider.ConnectActionId, Mode.Revert);
        }

        // Removals: one nothing touches, asked nothing; one that takes dependencies with it, asked first.
        if (keyed.FirstOrDefault(task => task.DependsOn.Count == 0 && JobWriter.ReferencesTo(job, task.Key).Count == 0) is { } untouched)
        {
            await script.ExecuteAsync($"task:{untouched.Key}", DatabricksContextActionProvider.RemoveTaskActionId, Mode.RevertWithRedo);
        }

        if (keyed.FirstOrDefault(task => task.DependsOn.Count + JobWriter.ReferencesTo(job, task.Key).Count > 0) is { } touched)
        {
            await script.ExecuteAsync($"task:{touched.Key}", DatabricksContextActionProvider.RemoveTaskActionId, Mode.Revert);
            await script.CommitAsync($"task:{touched.Key}", DatabricksContextActionProvider.RemoveTaskActionId, "", Mode.RevertWithRedo);
        }

        // Adds: dropped at a placement (a fresh key, finding A11), through the key dialog, and refused.
        await script.ExecuteAsync(placement, $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", Mode.RevertWithRedo);
        await script.ExecuteAsync(placement, $"{DatabricksContextActionProvider.AddTaskActionPrefix}python", Mode.Revert);
        await script.ExecuteAsync(placement, $"{DatabricksContextActionProvider.AddTaskActionPrefix}condition", Mode.Revert);
        await script.ExecuteAsync(placement, $"{DatabricksContextActionProvider.AddTaskActionPrefix}jar", Mode.Revert);
        await script.ExecuteAsync("", $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", Mode.Revert);
        await script.ValidateAsync("", $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", "");
        await script.CommitAsync("", $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", "parity_task", Mode.Revert);
        if (keyed.FirstOrDefault() is { } taken)
        {
            await script.CommitAsync("", $"{DatabricksContextActionProvider.AddTaskActionPrefix}notebook", taken.Key, Mode.Revert);
        }

        // The skeletons only a command reaches: the writer has seven, the toolbox three (finding B17).
        foreach (var type in (string[])["wheel", "sql", "pipeline", "run-job", "for-each"])
        {
            await script.DispatchAsync($"insert a {type} task", new InsertDatabricksTaskCommand(script.Body, job.Key, $"parity_{type.Replace('-', '_')}", type, $"parity/{type}"));
        }

        // The jobs after the first: which job an edit of a key they share reaches (finding B3).
        foreach (var other in model.Jobs.Skip(1))
        {
            if (other.Tasks.FirstOrDefault(task => task.Key.Length > 0) is { } firstTask)
            {
                await script.SetAsync($"task:{firstTask.Key}", DatabricksContextPropertyProvider.RunIfProperty, "ALL_DONE", Mode.Revert);
            }
        }
    }

    /// <summary>The bundle's edits: its name, a resource added of each kind, and the refusals.</summary>
    private static async Task WriteBundleEditsAsync(Script script, DatabricksModel model)
    {
        await script.SetAsync("bundle", "databricks.bundle-name", "Parity bundle", Mode.RevertWithRedo);
        await script.SetAsync("bundle", "databricks.bundle-name", "", Mode.Revert);
        await script.ExecuteAsync("bundle", DatabricksContextActionProvider.RenameBundleActionId, Mode.Revert);
        await script.CommitAsync("bundle", DatabricksContextActionProvider.RenameBundleActionId, "null", Mode.Revert);
        await script.ExecuteAsync("bundle", $"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs", Mode.Revert);
        await script.CommitAsync("bundle", $"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs", "parity_job", Mode.RevertWithRedo);
        await script.CommitAsync("bundle", $"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs", "yes", Mode.Revert);
        await script.CommitAsync("bundle", $"{DatabricksContextActionProvider.AddResourceActionPrefix}pipelines", "parity_pipeline", Mode.Revert);
        await script.CommitAsync("bundle", $"{DatabricksContextActionProvider.AddResourceActionPrefix}experiments", "parity_experiment", Mode.Revert);
        await script.CommitAsync("bundle", $"{DatabricksContextActionProvider.AddResourceActionPrefix}jobs", "", Mode.Revert);
        if (model.Bundle.Resources.FirstOrDefault() is { } resource)
        {
            await script.CommitAsync("bundle", $"{DatabricksContextActionProvider.AddResourceActionPrefix}{resource.Kind}", resource.Key, Mode.Revert);
        }

        if (model.Bundle.Targets.FirstOrDefault() is { } target)
        {
            await script.ExecuteAsync($"target:{target.Name}", DatabricksContextActionProvider.SimulatedDeployActionId, Mode.Revert);
            await script.SetAsync($"target:{target.Name}", DatabricksContextPropertyProvider.TaskKeyProperty, "parity_target", Mode.Revert);
        }
    }

    /// <summary>The first pipeline's edits: its scalars, libraries added and removed, and the refusals.</summary>
    private static async Task WritePipelineEditsAsync(Script script, PipelineModel pipeline)
    {
        const string prefix = DatabricksContextPropertyProvider.PipelineScalarPrefix;
        await script.SetAsync("pipeline", $"{prefix}name", "Parity pipeline", Mode.RevertWithRedo);
        await script.SetAsync("pipeline", $"{prefix}catalog", "parity_catalog", Mode.Revert);
        await script.SetAsync("pipeline", $"{prefix}schema", "parity_schema", Mode.Revert);
        await script.SetAsync("pipeline", $"{prefix}channel", "PREVIEW", Mode.Revert);
        await script.SetAsync("pipeline", $"{prefix}name", "", Mode.Revert);
        await script.SetAsync("pipeline", $"{prefix}serverless", "false", Mode.Revert);
        await script.SetAsync("pipeline", $"{prefix}edition", "CORE", Mode.Revert);
        await script.ExecuteAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook", Mode.Revert);
        await script.CommitAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook", "parity/notebook", Mode.RevertWithRedo);
        await script.CommitAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}file", "parity/file.py", Mode.Revert);
        await script.CommitAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}glob", "parity/**", Mode.Revert);
        await script.CommitAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}jar", "parity.jar", Mode.Revert);
        await script.CommitAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook", "*star", Mode.Revert);
        await script.CommitAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}notebook", "", Mode.Revert);
        if (pipeline.Libraries.FirstOrDefault() is { } library)
        {
            await script.CommitAsync("pipeline", $"{DatabricksContextActionProvider.AddLibraryActionPrefix}{library.Kind}", library.Path, Mode.Revert);
            await script.ExecuteAsync($"library:{library.Path}", DatabricksContextActionProvider.RemoveLibraryActionId, Mode.RevertWithRedo);
        }

        if (pipeline.Libraries.Count > 1)
        {
            await script.ExecuteAsync($"library:{pipeline.Libraries[^1].Path}", DatabricksContextActionProvider.RemoveLibraryActionId, Mode.Revert);
        }

        await script.ExecuteAsync("pipeline", DatabricksContextActionProvider.SimulatedUpdateActionId, Mode.Revert);
    }

    /// <summary>What a step leaves on the history: kept, or undone at once, or undone, redone and undone again.</summary>
    private enum Mode
    {
        Keep,
        Revert,
        RevertWithRedo,
    }

    /// <summary>One document's services, store, history and providers, over a folder of its own.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();
        private readonly ServiceProvider _services;

        public Session(string folder, string body)
        {
            Folder = folder;
            Body = body;
            _services = Services();
            Documents = _services.GetRequiredService<IDatabricksDocumentStore>();
            Mapper = _services.GetRequiredService<DatabricksElementMapper>();
            var history = _services.GetRequiredService<IHistoryStackStore>();
            Actions = new DatabricksContextActionProvider(history, Documents);
            Properties = new DatabricksContextPropertyProvider(history, Documents);
            Stack = history.Get(ProjectRoot.From(Folder));
        }

        public string Folder { get; }

        public string Body { get; }

        private IDatabricksDocumentStore Documents { get; }

        private DatabricksElementMapper Mapper { get; }

        public DatabricksContextActionProvider Actions { get; }

        public DatabricksContextPropertyProvider Properties { get; }

        public IHistoryStack Stack { get; }

        /// <summary>Opens the document, as the diagram's first open does; the readings and edits read what it holds.</summary>
        public async Task ReadAsync(CancellationToken cancellationToken) => await Documents.GetOrLoadAsync(Body, cancellationToken);

        public DatabricksModel Model => Documents.Get(Body)!.Model;

        public ContextTarget Target(string elementId, ToolOrigin origin) =>
            ContextTarget.ForElement(Body, elementId, ProjectRoot.From(Folder), _watchId, origin);

        /// <summary>A session of <paramref name="origin"/> over the document, as the host opens one through <paramref name="registration"/>.</summary>
        public DatabricksSession Open(ToolOrigin origin, string? registration, bool withHistory = true) =>
            new(Body, registration, origin, DatabricksHeaders.ReadResourceKey(registration), Documents, Mapper, withHistory ? Stack : null);

        /// <summary>What a session of <paramref name="origin"/> opened through <paramref name="registration"/> draws first.</summary>
        public async Task<IReadOnlyList<DiagramElement>> DrawAsync(ToolOrigin origin, string? registration, CancellationToken cancellationToken)
        {
            await using var session = Open(origin, registration);
            return [.. (await session.BaselineAsync(cancellationToken)).OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements)];
        }

        /// <summary>The registrations in the document's folder whose <c>body:</c> header names it, with the tool type on their first line.</summary>
        public async Task<IReadOnlyList<(string Path, string Origin)>> RegistrationsAsync(CancellationToken cancellationToken)
        {
            var registrations = new List<(string, string)>();
            foreach (var registration in Directory.GetFiles(Folder, "*.adp").Order(StringComparer.Ordinal))
            {
                var lines = (await File.ReadAllTextAsync(registration, cancellationToken)).Split('\n').Select(line => line.Trim()).ToList();
                var bodyLine = lines.FirstOrDefault(line => line.StartsWith("body:", StringComparison.OrdinalIgnoreCase));
                if (bodyLine is not null && string.Equals(bodyLine["body:".Length..].Trim(), Path.GetFileName(Body), StringComparison.Ordinal))
                {
                    registrations.Add((registration, lines[0]));
                }
            }

            return registrations;
        }

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }

    /// <summary>Runs the steps of one document's edit sequence and writes each as it goes.</summary>
    private sealed class Script(Utf8JsonWriter json, Session session, CancellationToken cancellationToken)
    {
        private string _text = "";
        private Dictionary<string, string> _registrations = [];

        public string Body => session.Body;

        public async Task<string> StartAsync()
        {
            (_text, _registrations) = await ReadAsync();
            json.WriteStartArray("edits");
            return _text;
        }

        public async Task EndAsync(string original)
        {
            json.WriteEndArray();

            var undone = 0;
            var thrown = "";
            try
            {
                while (session.Stack.CanUndo && (await session.Stack.UndoAsync(cancellationToken)).IsSuccess)
                {
                    undone++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                thrown = $"; the next {Thrown(exception)}";
            }

            (string restored, _) = await ReadAsync();
            var back = string.Equals(restored, original, StringComparison.Ordinal);
            json.WriteString("undoAll", $"{undone} undone{thrown}; {(back ? "the original text is back" : "the original text is NOT back")}");
            if (!back)
            {
                // What undoing everything left different, so a change in it is visible rather than
                // hidden behind the same sentence.
                json.WriteStartObject("undoAllLeaves");
                json.WriteChange(original, restored);
                json.WriteEndObject();
            }
        }

        public Task SetAsync(string target, string propertyId, string value, Mode mode) =>
            StepAsync($"set {target} {propertyId} = {TranscriptText.Quoted(value)}", mode, async () =>
            {
                var result = await session.Properties.SetAsync(session.Target(target, OriginOf(target)), propertyId, value, cancellationToken);
                return result.IsSuccess ? "ok" : $"refused: {result.Error}";
            });

        public Task ExecuteAsync(string target, string actionId, Mode mode) =>
            StepAsync($"execute {actionId} on {target}", mode, async () => Describe(await session.Actions.ExecuteAsync(session.Target(target, OriginOf(target)), actionId, cancellationToken)));

        public Task ValidateAsync(string target, string actionId, string value) =>
            StepAsync($"validate {actionId} on {target} = {TranscriptText.Quoted(value)}", Mode.Revert, async () =>
            {
                var result = await session.Actions.ValidateAsync(session.Target(target, OriginOf(target)), actionId, value, cancellationToken);
                return result.Valid ? "accepted" : $"rejected: {result.Reason}";
            });

        public Task CommitAsync(string target, string actionId, string value, Mode mode) =>
            StepAsync($"commit {actionId} on {target} = {TranscriptText.Quoted(value)}", mode, async () =>
            {
                var result = await session.Actions.CommitAsync(session.Target(target, OriginOf(target)), actionId, value, "", cancellationToken);
                var next = result.Next is { } chained ? $"; then {Describe(chained)}" : "";
                return (result.Completed ? "ok" : $"refused: {result.Error}") + next;
            });

        /// <summary>A command no gesture constructs, straight onto the history.</summary>
        public Task DispatchAsync(string step, ICommand command) =>
            StepAsync(step, Mode.Revert, async () => Result(await session.Stack.ExecuteAsync(command, cancellationToken)));

        /// <summary>Writes the registration the moves of <paramref name="origin"/> go through: its tool type and its body, and nothing else.</summary>
        public async Task RegisterAsync(ToolOrigin origin, string registration)
        {
            await File.WriteAllTextAsync(registration, $"{origin.Key}\nbody: {Path.GetFileName(session.Body)}\n", cancellationToken);
            (_, _registrations) = await ReadAsync();
        }

        /// <summary>A drag on a session of <paramref name="origin"/> opened through <paramref name="registration"/>.</summary>
        public async Task MoveAsync(ToolOrigin origin, string? registration, string target, double x, double y, Mode mode)
        {
            var through = registration is null ? "no registration" : Path.GetFileName(registration);
            await StepAsync($"move {target} on {origin.Key} through {through} to {Number(x)}, {Number(y)}", mode, async () =>
            {
                await using var opened = session.Open(origin, registration);
                var refusal = await opened.MoveElementToAsync(target, x, y, cancellationToken);
                return refusal.Length == 0 ? "ok" : $"refused: {refusal}";
            });
        }

        /// <summary>A drag on a session opened without the project's history.</summary>
        public Task MoveReadOnlyAsync(ToolOrigin origin, string registration, string target) =>
            StepAsync($"move {target} on {origin.Key} through {Path.GetFileName(registration)}, opened without the history", Mode.Revert, async () =>
            {
                await using var opened = session.Open(origin, registration, withHistory: false);
                var refusal = await opened.MoveElementToAsync(target, 10, 10, cancellationToken);
                return refusal.Length == 0 ? "ok" : $"refused: {refusal}";
            });

        /// <summary>An undo of whatever the history holds last, as a step of its own.</summary>
        public Task UndoAsync() =>
            StepAsync("undo", Mode.Keep, async () => Result(await session.Stack.UndoAsync(cancellationToken)));

        /// <summary>
        /// A step, written with its answer and the hunks it made; then, where it landed on the history and the
        /// mode asks for it, its undo (and its redo and undo again), each saying whether it restored what it should.
        /// </summary>
        private async Task StepAsync(string step, Mode mode, Func<Task<string>> run)
        {
            var before = session.Stack.Availability.UndoCount;
            (string beforeText, Dictionary<string, string> beforeRegistrations) = (_text, _registrations);
            json.WriteStartObject();
            json.WriteString("do", step);
            json.WriteString("answer", await AnswerAsync(run));
            await WriteChangeAsync();
            if (mode != Mode.Keep && session.Stack.Availability.UndoCount > before)
            {
                (string afterText, Dictionary<string, string> afterRegistrations) = (_text, _registrations);
                await WriteBackAsync("undo", session.Stack.UndoAsync, beforeText, beforeRegistrations, "restores");
                if (mode == Mode.RevertWithRedo)
                {
                    await WriteBackAsync("redo", session.Stack.RedoAsync, afterText, afterRegistrations, "reapplies");
                    await WriteBackAsync("undoAgain", session.Stack.UndoAsync, beforeText, beforeRegistrations, "restores");
                }
            }

            json.WriteEndObject();
        }

        /// <summary>An undo or a redo, as one line that says whether it brought back <paramref name="expected"/>, and what it left otherwise.</summary>
        private async Task WriteBackAsync(string name, Func<CancellationToken, Task<CommandResult>> back, string expected, Dictionary<string, string> expectedRegistrations, string verb)
        {
            var answer = await AnswerAsync(async () => Result(await back(cancellationToken)));
            (string text, Dictionary<string, string> registrations) = await ReadAsync();
            var same = string.Equals(text, expected, StringComparison.Ordinal) && Same(registrations, expectedRegistrations);
            json.WriteString(name, $"{answer}; {(same ? $"{verb} the text" : $"does NOT {verb[..^1]} the text")}");
            if (!same)
            {
                json.WriteStartObject($"{name}Leaves");
                json.WriteChange(expected, text);
                json.WriteEndObject();
            }

            (_text, _registrations) = (text, registrations);
        }

        private static bool Same(Dictionary<string, string> left, Dictionary<string, string> right) =>
            left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

        private static string Result(CommandResult result) =>
            (result.IsSuccess ? "ok" : $"refused: {result.Error}") + (result.Warning.Length > 0 ? $"; warns: {result.Warning}" : "");

        /// <summary>A step's answer, or what it threw (<see cref="Thrown"/>).</summary>
        private static async Task<string> AnswerAsync(Func<Task<string>> run)
        {
            try
            {
                return await run();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Thrown(exception);
            }
        }

        /// <summary>The hunks a step made to the body, and the registrations it wrote or deleted.</summary>
        private async Task WriteChangeAsync()
        {
            (string text, Dictionary<string, string> registrations) = await ReadAsync();
            json.WriteChange(_text, text);
            foreach ((string name, string content) in registrations.Where(pair => !_registrations.TryGetValue(pair.Key, out var was) || was != pair.Value))
            {
                json.WriteString($"writes {name}", content);
            }

            foreach (var name in _registrations.Keys.Where(name => !registrations.ContainsKey(name)))
            {
                json.WriteString($"deletes {name}", "");
            }

            (_text, _registrations) = (text, registrations);
        }

        /// <summary>The body and the registrations the script writes, beside it.</summary>
        private async Task<(string Text, Dictionary<string, string> Registrations)> ReadAsync()
        {
            var text = TranscriptText.Decode(await File.ReadAllBytesAsync(session.Body, cancellationToken));
            var registrations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var registration in Directory.GetFiles(session.Folder, $"*.{ScriptedRegistration}").Order(StringComparer.Ordinal))
            {
                registrations[Path.GetFileName(registration)] = TranscriptText.Decode(await File.ReadAllBytesAsync(registration, cancellationToken));
            }

            return (text, registrations);
        }

        /// <summary>The tool type whose reading draws <paramref name="target"/>, which a target is resolved through.</summary>
        private static ToolOrigin OriginOf(string target) =>
            target == "bundle" || target.StartsWith("target:", StringComparison.Ordinal) || target.StartsWith("resource:", StringComparison.Ordinal)
                ? ServiceCollectionAddDatabricksExtension.BundleOrigin
                : target == "pipeline" || target.StartsWith("library:", StringComparison.Ordinal)
                    ? ServiceCollectionAddDatabricksExtension.PipelineOrigin
                    : ServiceCollectionAddDatabricksExtension.JobOrigin;

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string Describe(ContextExecutionResult result) => result switch
        {
            ContextExecutionCompleted => "ok",
            ContextExecutionFailed failed => $"refused: {failed.Message}",
            ContextExecutionRequiresInput input => $"asks: {input.Request}",
            ContextExecutionRequiresConfirmation confirmation => $"confirms: {confirmation.Request}",
            _ => result.ToString(),
        };
    }
}
