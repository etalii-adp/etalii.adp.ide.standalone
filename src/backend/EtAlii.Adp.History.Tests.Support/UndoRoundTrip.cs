namespace EtAlii.Adp.History.Tests.Support;

/// <summary>
/// The undo round trip a diagram's corpus test runs over a sample of its edits: each edit is applied
/// to the file at its original bytes and undone, and, when asked, redone; every edit whose undo does
/// not give those bytes back, or whose redo does not write the edit's, is recorded.
/// </summary>
/// <remarks>
/// An edit the dispatcher refuses, or one with no inverse, is passed over rather than recorded: a
/// sample over every element tries edits not every element can take. <see cref="Applied"/> counts the
/// others, so a test can tell a document that took no edit at all from one whose edits all came back.
/// Each edit gets a dispatcher of its own, so no history is carried from one to the next.
/// </remarks>
/// <param name="path">The file the edits are made to.</param>
/// <param name="original">Its bytes, written back before every edit.</param>
/// <param name="dispatcher">Makes the dispatcher one edit is made, undone and redone through.</param>
/// <param name="andRedo">Whether every undo is redone too, and the redo held to the edit's bytes.</param>
public sealed class UndoRoundTrip(string path, byte[] original, Func<ICommandDispatcher> dispatcher, bool andRedo = false)
{
    private readonly List<string> _failures = [];

    /// <summary>How many of the edits checked so far were applied.</summary>
    public int Applied { get; private set; }

    /// <summary>One line for each applied edit that did not come back, naming the edit and what went wrong.</summary>
    public IReadOnlyList<string> Failures => _failures;

    /// <summary>Makes <paramref name="command"/> on the original bytes, undoes it, and records what did not come back.</summary>
    public async Task CheckAsync(ICommand command, CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(path, original, cancellationToken);
        var edits = dispatcher();
        var done = await edits.DispatchAsync(command, cancellationToken);
        if (done is not { IsSuccess: true, Inverse: not null }) return;

        Applied++;
        var edited = await File.ReadAllBytesAsync(path, cancellationToken);
        var undone = await edits.DispatchAsync(done.Inverse, cancellationToken);
        var afterUndo = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!undone.IsSuccess)
        {
            _failures.Add($"  {command}: the undo was refused: {undone.Error}");
            return;
        }

        if (!afterUndo.AsSpan().SequenceEqual(original))
        {
            _failures.Add($"  {command}: the undo did not give the bytes back ({(edited.AsSpan().SequenceEqual(original) ? "the edit wrote nothing" : "the edit wrote")})");
            return;
        }

        if (!andRedo) return;

        var redone = undone.Inverse is { } redo ? await edits.DispatchAsync(redo, cancellationToken) : null;
        var afterRedo = await File.ReadAllBytesAsync(path, cancellationToken);
        if (redone is not { IsSuccess: true } || !afterRedo.AsSpan().SequenceEqual(edited))
        {
            _failures.Add($"  {command}: the redo {(redone is { IsSuccess: false } ? $"was refused: {redone.Error}" : "did not write the edit's bytes")}");
        }
    }

    /// <summary>The failures, one to a line, for an assertion's message.</summary>
    public override string ToString() => string.Join("\n", _failures);
}
