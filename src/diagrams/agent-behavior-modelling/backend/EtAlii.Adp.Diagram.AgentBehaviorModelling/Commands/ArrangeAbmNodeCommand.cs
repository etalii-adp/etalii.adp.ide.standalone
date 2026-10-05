using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>A node dropped with its top-left at (<paramref name="X"/>, <paramref name="Y"/>): its row moves, and its place among its siblings may change.</summary>
/// <param name="BodyPath">The Markdown.</param>
/// <param name="RegistrationPath">The <c>.adp</c> whose <c>layout:</c> block keeps the rows' heights.</param>
/// <param name="NodeId">The node that was dragged.</param>
/// <param name="X">Where its left edge was dropped.</param>
/// <param name="Y">Where its top edge was dropped.</param>
public sealed record ArrangeAbmNodeCommand(string BodyPath, string RegistrationPath, string NodeId, double X, double Y) : ICommand;

/// <summary>Puts back the Markdown (when <paramref name="Text"/> is not null) and every stored position, as they were before a drag.</summary>
public sealed record RestoreAbmArrangementCommand(
    string BodyPath,
    string RegistrationPath,
    string? Text,
    IReadOnlyDictionary<string, RegistrationPosition> Positions) : ICommand;

/// <summary>What one drop means for a node's row: its new place among its siblings, and how far the row moves down.</summary>
/// <param name="From">Its place among its siblings before the drop.</param>
/// <param name="To">Its place among them after it.</param>
/// <param name="Dy">How far its row, and everything beneath it, moves down; negative is up.</param>
public readonly record struct AbmArrangement(int From, int To, double Dy)
{
    /// <summary>Whether the drop changes nothing: the same place, and the same height.</summary>
    public bool IsNothing => From == To && Math.Abs(Dy) < 0.5;

    /// <summary>
    /// What dropping <paramref name="node"/> with its top-left at (<paramref name="x"/>, <paramref name="y"/>) means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Across, the order.</b> The node goes before the first other sibling whose middle is right of
    /// the drop's middle - the snap the Sankey diagram makes in a column, turned on its side.
    /// </para>
    /// <para>
    /// <b>Down, the row.</b> Every child of one parent stays at one height, so the whole row moves by
    /// what the node moved, never closer to the parent than <see cref="AbmLayout.MinimumGap"/>.
    /// </para>
    /// </remarks>
    public static AbmArrangement Of(AbmModel model, IReadOnlyDictionary<string, RegistrationPosition> stored, AbmNode node, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(node);

        var positions = AbmLayout.Arrange(model, stored);
        var siblings = SiblingsOf(model, node);
        var from = siblings.ToList().FindIndex(sibling => sibling.Id == node.Id);
        var middle = x + (AbmLayout.NodeWidth / 2);
        var to = siblings.Count(sibling => sibling.Id != node.Id && positions[sibling.Id].X + (AbmLayout.NodeWidth / 2) < middle);

        var floor = node.ParentId is { } parentId
            ? positions[parentId].Y + AbmLayout.NodeHeight + AbmLayout.MinimumGap
            : double.NegativeInfinity;
        var dy = Math.Max(y, floor) - positions[node.Id].Y;
        return new AbmArrangement(from, to, dy);
    }

    /// <summary>The node's siblings, itself included, in order: its parent's children, or the roots.</summary>
    public static IReadOnlyList<AbmNode> SiblingsOf(AbmModel model, AbmNode node)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(node);

        return node.ParentId is { } parentId ? model.ChildrenOf(model.NodeOf(parentId)!) : model.Roots;
    }

    /// <summary>
    /// The stored positions with their ids following the nodes after the move from <see cref="From"/>
    /// to <see cref="To"/>: an id is a place in the tree, so a reorder renames every id in the
    /// siblings' subtrees, and a position kept under the old name would land on another node.
    /// </summary>
    public IReadOnlyDictionary<string, RegistrationPosition> Renamed(string? parentId, int siblingCount, IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var from = From;
        var order = Enumerable.Range(0, siblingCount).Where(index => index != from).ToList();
        order.Insert(To, from);

        var renamed = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        foreach ((string id, RegistrationPosition position) in stored)
        {
            var key = id;
            for (var newIndex = 0; newIndex < order.Count; newIndex++)
            {
                var old = PlaceOf(parentId, order[newIndex]);
                if (AbmModel.IsWithin(id, old))
                {
                    key = PlaceOf(parentId, newIndex) + id[old.Length..];
                    break;
                }
            }

            renamed[key] = position;
        }

        return renamed;
    }

    /// <summary>The <c>Index</c> a <see cref="MoveAbmNodeCommand"/> takes for this move: before the child now there, the node itself still counted.</summary>
    public int MoveIndex => To <= From ? To : To + 1;

    private static string PlaceOf(string? parentId, int index) =>
        parentId is null ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{parentId}.{index + 1}";
}

/// <summary>
/// Carries out a drop: rewrites the sibling order in the Markdown when it changed, and stores the
/// row's new height for every node in it and beneath it - one history entry for both files.
/// </summary>
public sealed class ArrangeAbmNodeCommandHandler(IAbmDocumentStore documents) : ICommandHandler<ArrangeAbmNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ArrangeAbmNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                $"{Path.GetFileName(command.BodyPath)} could not be read, so nothing can be moved until it can: {entry.Unreadable}"));
        }

        if (!File.Exists(command.RegistrationPath))
        {
            return Task.FromResult(CommandResult.Failure("The registration file is no longer there."));
        }

        var model = entry.Model;
        if (model.NodeOf(command.NodeId) is not { } node)
        {
            return Task.FromResult(CommandResult.Failure("That node is no longer in this behavior model."));
        }

        var stored = RegistrationLayout.Read(command.RegistrationPath);
        var arrangement = AbmArrangement.Of(model, stored, node, command.X, command.Y);
        if (arrangement.IsNothing)
        {
            return Task.FromResult(CommandResult.Failure("That node is already there."));
        }

        var before = entry.Document.Text;
        var siblingCount = AbmArrangement.SiblingsOf(model, node).Count;
        var parent = node.ParentId is { } parentId ? model.NodeOf(parentId) : null;
        LineDocument? reordered = null;
        var renamed = stored;
        if (arrangement.From != arrangement.To)
        {
            reordered = LineDocument.Parse(before);
            var edit = AbmWriter.Move(reordered, model, node, parent, arrangement.MoveIndex);
            if (!edit.WasApplied)
            {
                return Task.FromResult(CommandResult.Failure(edit.Refusal!));
            }

            model = AbmParser.Parse(reordered);
            renamed = arrangement.Renamed(node.ParentId, siblingCount, stored);
        }

        // Every node in the row and beneath it is written out, at where it is drawn now plus the
        // row's move: so a row below one that only hung from its parent keeps its place under it.
        var arranged = AbmLayout.Arrange(model, renamed);
        var row = parent is null ? model.Roots : model.ChildrenOf(model.NodeOf(parent.Id)!);
        var positions = new Dictionary<string, RegistrationPosition>(renamed, StringComparer.Ordinal);
        foreach (var moved in model.Nodes.Where(candidate => row.Any(member => AbmModel.IsWithin(candidate.Id, member.Id))))
        {
            var at = arranged[moved.Id];
            positions[moved.Id] = new RegistrationPosition(at.X, at.Y + arrangement.Dy);
        }

        // The positions first: the Markdown's save is what redraws the diagram, and it must find the
        // positions already under their new ids, or the rows would jump for one frame.
        try
        {
            RegistrationLayout.Replace(command.RegistrationPath, positions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not store the positions: {exception.Message}"));
        }

        var warning = "";
        if (reordered is not null)
        {
            var saved = documents.Save(command.BodyPath, reordered);
            if (saved.Failed)
            {
                // Half a drop is worse than none: the positions would follow an order the Markdown does not have.
                RegistrationLayout.Replace(command.RegistrationPath, stored);
                return Task.FromResult(CommandResult.Failure(saved.Error));
            }

            warning = saved.Warning;
        }

        var undo = new RestoreAbmArrangementCommand(command.BodyPath, command.RegistrationPath, reordered is not null ? before : null, stored);
        return Task.FromResult(warning.Length > 0 ? CommandResult.Success(undo, warning) : CommandResult.Success(undo));
    }
}

/// <summary>Puts a drop's two files back, and answers with the command that puts them forward again.</summary>
public sealed class RestoreAbmArrangementCommandHandler(IAbmDocumentStore documents) : ICommandHandler<RestoreAbmArrangementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RestoreAbmArrangementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(command.RegistrationPath))
        {
            return Task.FromResult(CommandResult.Failure("The registration file is no longer there."));
        }

        var positions = RegistrationLayout.Read(command.RegistrationPath);
        var text = command.Text is not null ? documents.GetOrLoad(command.BodyPath).Document.Text : null;
        try
        {
            // The positions first, for the same reason the drop writes them first.
            RegistrationLayout.Replace(command.RegistrationPath, command.Positions);
            if (command.Text is not null)
            {
                AdpFileWriter.Save(command.BodyPath, command.Text);
                documents.Reload(command.BodyPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResult.Failure($"Could not put the drop back: {exception.Message}"));
        }

        return Task.FromResult(CommandResult.Success(new RestoreAbmArrangementCommand(command.BodyPath, command.RegistrationPath, text, positions)));
    }
}
