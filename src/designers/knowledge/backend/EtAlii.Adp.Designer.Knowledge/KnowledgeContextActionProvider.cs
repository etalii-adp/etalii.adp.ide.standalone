using EtAlii.Adp.Context;
using EtAlii.Adp.Designer.TableModel;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using ContextScope = EtAlii.Adp.Documents.Wire.ContextScope;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// What a knowledge file offers in the explorer: adding a relation to another table, or to itself.
/// The table it relates to is chosen in the file dialog, which offers the knowledge files of the
/// project and nothing else, with this table first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four entries rather than four questions.</b> An action asks one thing of its author, and
/// what this one asks is the file. How many rows a relation holds and whether the other table
/// shows it too are therefore chosen with the entry: one row or any number, one-way or two-way.
/// The names it starts with - the target's for this side, this table's for the other - are
/// changed afterwards like any property's.
/// </para>
/// <para>
/// <b>The edit is the table's own.</b> The relation is made by the same gesture, the same plan and
/// the same command as an edit made in the table, through the project's history: it is one
/// undoable step, over both files when it is two-way, and an open table shows it at once.
/// </para>
/// </remarks>
internal sealed class KnowledgeContextActionProvider(IHistoryStackStore histories) : IContextActionProvider
{
    public const string ManyOneWay = "knowledge.relate.many";
    public const string OneOneWay = "knowledge.relate.one";
    public const string ManyTwoWay = "knowledge.relate.many.two-way";
    public const string OneTwoWay = "knowledge.relate.one.two-way";

    private const string Icon = "mdi-arrow-top-right";

    private static readonly string[] Extensions = [".yaml", ".yml", ".json", ".xml"];

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.IsContainer || BodyOf(target.ResolvedFullPath) is null)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(
                    "knowledge.relate",
                    "Add relation",
                    Icon,
                    Children:
                    [
                        new ContextActionGroupDefinition(
                        [
                            new ContextActionDefinition(ManyOneWay, "To any number of rows…", Icon),
                            new ContextActionDefinition(OneOneWay, "To one row…", Icon),
                        ]),
                        new ContextActionGroupDefinition(
                        [
                            new ContextActionDefinition(ManyTwoWay, "To any number of rows, shown in both tables…", "mdi-swap-horizontal"),
                            new ContextActionDefinition(OneTwoWay, "To one row, shown in both tables…", "mdi-swap-horizontal"),
                        ]),
                    ]),
            ]),
        ]);
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (BodyOf(target.ResolvedFullPath) is not { } body)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed("That table is no longer there."));
        }

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresFile(new ContextFileRequest(
            "Relate to",
            Icon,
            "Relate",
            IsKnowledgeFile,
            "There is no other table in this project.",
            [new ContextPinnedFile("This table", body)])));
    }

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IsKnowledgeFile(value) ? ContextValidationResult.Accepted : ContextValidationResult.Rejected("That file is not a table."));

    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (BodyOf(target.ResolvedFullPath) is not { } bodyPath || KnowledgeDocumentStore.Read(bodyPath).Body is not { } body)
        {
            return ContextCommitResult.Failed("That table is no longer there.");
        }

        if (body.ReadOnlyReason.Length > 0)
        {
            return ContextCommitResult.Failed(body.ReadOnlyReason);
        }

        var twoWay = actionId is ManyTwoWay or OneTwoWay;
        var settings = new Dictionary<string, string>
        {
            ["target"] = KnowledgeRelations.TargetName(bodyPath, value),
            ["limit"] = actionId is OneOneWay or OneTwoWay ? "one" : "none",
            // The other side starts under this table's name, as this side starts under the target's.
            ["counterpart"] = twoWay ? body.Table.Name.Length > 0 ? body.Table.Name : Path.GetFileNameWithoutExtension(bodyPath) : "",
        };

        var surroundings = new KnowledgeSurroundings(bodyPath, property => TargetAt(bodyPath, body.Table, property.TargetFile), name => TargetAt(bodyPath, body.Table, name));
        var edit = KnowledgeEdits.Plan(
            body.Table,
            "",
            new TableGesture("addRelation", Settings: settings),
            () => ShortGuid.NewShortGuid().ToString(),
            KnowledgeDefinition.AddsBesideItsRule(Path.GetExtension(bodyPath)),
            surroundings);
        if (edit.IsRefused)
        {
            return ContextCommitResult.Failed(edit.Refusal);
        }

        var result = await histories.Get(target.RootPath).ExecuteAsync(new KnowledgeEditCommand(bodyPath, edit.Changes, edit.Others), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    private static KnowledgeTarget TargetAt(string bodyPath, KnowledgeTable own, string targetFile)
    {
        var path = KnowledgeRelations.TargetPath(bodyPath, targetFile);
        if (string.Equals(path, Path.GetFullPath(bodyPath), StringComparison.OrdinalIgnoreCase))
        {
            return new KnowledgeTarget(path, own);
        }

        var read = KnowledgeDocumentStore.Read(path);
        return read.Body is null
            ? new KnowledgeTarget(path, null, $"'{targetFile}' is not there.")
            : read.Body.Unreadable.Length > 0
                ? new KnowledgeTarget(path, null, $"'{targetFile}' cannot be read as a knowledge file.")
                : new KnowledgeTarget(path, read.Body.Table);
    }

    /// <summary>Whether a file is a knowledge file's data file: one of the three formats, and readable as a table.</summary>
    private static bool IsKnowledgeFile(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && KnowledgeDocumentStore.Read(path).Body is { Unreadable.Length: 0 };

    /// <summary>
    /// The data file an explorer entry stands for: the entry itself when it is one, or the file its
    /// registration names - by a <c>body:</c> line, or as the file of its own name in one of the formats.
    /// Null for an entry that is neither.
    /// </summary>
    private static string? BodyOf(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (!string.Equals(Path.GetExtension(path), ".adp", StringComparison.OrdinalIgnoreCase))
        {
            return IsKnowledgeFile(path) ? path : null;
        }

        string[] lines;
        try
        {
            lines = [.. SharedDocumentReader.ReadAllText(path).Split('\n').Select(line => line.Trim())];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (lines.Length == 0 || lines[0] != Designer.Origin)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(path) ?? "";
        var header = lines.Skip(1).FirstOrDefault(line => line.StartsWith("body:", StringComparison.Ordinal));
        var named = header is null ? "" : header["body:".Length..].Trim();
        var candidates = named.Length > 0
            ? [Path.Combine(folder, named)]
            : Extensions.Select(extension => Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + extension));
        return candidates.FirstOrDefault(IsKnowledgeFile);
    }
}
