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
/// <b>One entry, and its questions in the order Notion asks them</b> (knowledge-designer
/// Requirement 7.6): the table to relate to; whether a cell holds one row or any number; whether
/// the other table shows the relation too, and under which name there. Each answer leads to the
/// next question, and nothing is written before the last one is confirmed. What was answered so
/// far travels in the id the next answer is committed under, so nothing is kept between them.
/// </para>
/// <para>
/// <b>The edit is the table's own.</b> The relation is made by the same gesture, the same plan and
/// the same command as an edit made in the table, through the project's history: it is one
/// undoable step, over both files when it is two-way, and an open table shows it at once.
/// </para>
/// </remarks>
internal sealed class KnowledgeContextActionProvider(IHistoryStackStore histories) : IContextActionProvider
{
    /// <summary>The entry, and the id its first answer - the table - is committed under.</summary>
    public const string Relate = "knowledge.relate";

    /// <summary>The id the second answer is committed under, followed by the table chosen.</summary>
    private const string LimitStep = "knowledge.relate.limit";

    /// <summary>The id the third answer is committed under, followed by the limit and the table chosen.</summary>
    private const string SideStep = "knowledge.relate.side";

    public const string One = "one";
    public const string Many = "many";
    public const string OneWay = "one-way";
    public const string TwoWay = "two-way";

    /// <summary>What keeps the parts of a step's id apart: no file name holds it.</summary>
    private const char Part = '|';

    private const string Icon = "mdi-arrow-top-right";

    public const string OtherSideLabel = "Name in the other table";
    public const string OtherSideNeedsName = "The relation needs a name in the other table.";

    /// <summary>What the other side of a two-way relation is called until its author says otherwise: this table's name.</summary>
    private static string OtherSideName(string bodyPath) =>
        KnowledgeDocumentStore.Read(bodyPath).Body?.Table.Name is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(bodyPath);

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
            new ContextActionGroupDefinition([new ContextActionDefinition(Relate, "Add relation…", Icon)]),
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
            "Next",
            IsKnowledgeFile,
            "There is no other table in this project.",
            [new ContextPinnedFile("This table", body)])));
    }

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(actionId);

        // What is typed in the last step is the relation's name in the other table; the choices are checked against what their dialogs offered.
        return actionId.StartsWith(SideStep, StringComparison.Ordinal)
            ? ValueTask.FromResult(value.Trim().Length > 0
                ? ContextValidationResult.Accepted
                : ContextValidationResult.Rejected(OtherSideNeedsName))
            : ValueTask.FromResult(actionId != Relate || IsKnowledgeFile(Chosen(target, value))
                ? ContextValidationResult.Accepted
                : ContextValidationResult.Rejected("That file is not a table."));
    }

    private static string Chosen(ContextTarget target, string value) =>
        Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(target.RootPath, value.Replace('/', Path.DirectorySeparatorChar)));

    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(actionId);

        if (BodyOf(target.ResolvedFullPath) is not { } bodyPath || KnowledgeDocumentStore.Read(bodyPath).Body is not { } body)
        {
            return ContextCommitResult.Failed("That table is no longer there.");
        }

        if (body.ReadOnlyReason.Length > 0)
        {
            return ContextCommitResult.Failed(body.ReadOnlyReason);
        }

        var parts = actionId.Split(Part);
        return parts[0] switch
        {
            // The table is chosen: how many rows may a cell hold?
            Relate => IsKnowledgeFile(Chosen(target, value))
                    ? ContextCommitResult.Asks(
                        new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                            "A cell holds",
                            Icon,
                            "Next",
                            [new ContextOptionNode(Many, "Any number of rows", Selectable: true), new ContextOptionNode(One, "One row", Selectable: true)],
                            "")),
                        string.Join(Part, LimitStep, value))
                    : ContextCommitResult.Failed("That file is not a table."),

            // And is it shown in the other table too, under which name?
            LimitStep when parts.Length == 2 && value is One or Many => ContextCommitResult.Asks(
                    new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                        "Show the relation",
                        Icon,
                        "Add relation",
                        [
                            new ContextOptionNode(OneWay, "In this table only", Selectable: true, NameSuppressedReason: "A relation shown in this table only has no name in the other."),
                            new ContextOptionNode(TwoWay, "In both tables", Selectable: true, SuggestedValue: OtherSideName(bodyPath)),
                        ],
                        "",
                        new ContextTextFieldRequest(OtherSideLabel, OtherSideName(bodyPath)))),
                    string.Join(Part, SideStep, value, parts[1])),

            SideStep when parts.Length == 3 && value is OneWay or TwoWay =>
                await MakeAsync(target, bodyPath, body, Chosen(target, parts[2]), parts[1] == One, value == TwoWay, text, cancellationToken).ConfigureAwait(false),

            _ => ContextCommitResult.Failed("That is not a step of adding a relation."),
        };
    }

    /// <summary>Makes the relation the answers describe, as one step of the project's history.</summary>
    private async ValueTask<ContextCommitResult> MakeAsync(ContextTarget target, string bodyPath, KnowledgeBody body, string chosen, bool one, bool twoWay, string name, CancellationToken cancellationToken)
    {
        var settings = new Dictionary<string, string>
        {
            ["target"] = KnowledgeRelations.TargetName(bodyPath, chosen),
            ["limit"] = one ? "one" : "none",
            // The other side has the name its author gave it; this table's name when none was given.
            ["counterpart"] = !twoWay ? "" : name.Trim().Length > 0 ? name.Trim() : OtherSideName(bodyPath),
        };

        var surroundings = new KnowledgeSurroundings(bodyPath, property => TargetAt(bodyPath, body.Table, property.TargetFile), targetName => TargetAt(bodyPath, body.Table, targetName));
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
    internal static string? BodyOf(string path)
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
