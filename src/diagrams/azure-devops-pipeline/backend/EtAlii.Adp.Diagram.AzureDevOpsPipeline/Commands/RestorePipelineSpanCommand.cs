using System.Security.Cryptography;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Puts back what one edit of a pipeline replaced: the undo of every pipeline command, and the redo of that undo.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries the library's inverse of the edit's splices</b> (<see cref="Edit.Inverse"/>): each the span the edit
/// wrote, in the file as the edit left it, and the bytes it replaced, under the edit's own operation, so the file is
/// byte for byte what it was and every splice still says truthfully what it does (amendment 2 of
/// azure-devops-pipeline-disl-fbl). The edit of one command is one span where it touched one place, and one span per
/// place where it touched several, as a move does.
/// </para>
/// <para>
/// <b>It is refused when the file has changed since</b> (FBL 7.2), told by the SHA-256 of the bytes it expects: the
/// splices are offsets in those bytes and nowhere else, and putting them anywhere else would write over somebody's
/// change. The refusal is the library's sentence.
/// </para>
/// <para>
/// <b>Its own inverse is its redo</b>, the same kind of command over the bytes it restored: so a redo writes the
/// edit's bytes again, the same element removed or the same value written, and an undo of that redo is this again.
/// It lives on the host's history, so it outlives a reload of the file that leaves the bytes as they were.
/// </para>
/// </remarks>
/// <param name="Root">The project.</param>
/// <param name="BodyPath">The pipeline file.</param>
/// <param name="Splices">What to put back, as offsets in the bytes of <paramref name="Sha256"/>.</param>
/// <param name="Sha256">The SHA-256 of the bytes the splices are offsets in, in upper-case hex.</param>
/// <param name="IsRedo">Whether this puts back an edit an undo took away, which its refusal says.</param>
public sealed record RestorePipelineSpanCommand(
    ProjectRoot Root,
    string BodyPath,
    IReadOnlyList<Splice> Splices,
    string Sha256,
    bool IsRedo = false) : IPipelineEditCommand
{
    /// <summary>The command that undoes the edit <paramref name="edit"/>, which turned <paramref name="before"/> into <paramref name="after"/>.</summary>
    /// <param name="command">The command that made the edit, for its project and file.</param>
    /// <param name="before">The bytes before the edit.</param>
    /// <param name="after">The bytes the edit left.</param>
    /// <param name="edit">The edit, its offsets in <paramref name="before"/>.</param>
    /// <param name="isRedo">Whether the command undoes an undo, which makes it a redo.</param>
    internal static RestorePipelineSpanCommand Undoing(IPipelineEditCommand command, byte[] before, byte[] after, Edit edit, bool isRedo = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(edit);
        return new RestorePipelineSpanCommand(command.Root, command.BodyPath, Edit.Inverse(before, edit.Splices), DigestOf(after), isRedo);
    }

    /// <summary>The SHA-256 of <paramref name="bytes"/>, as <see cref="Sha256"/> holds it.</summary>
    internal static string DigestOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

internal sealed class RestorePipelineSpanCommandHandler(IPipelineDocumentStore documents)
    : PipelineEditHandler<RestorePipelineSpanCommand>(documents)
{
    protected override EditOutcome Apply(PipelineBody body, RestorePipelineSpanCommand command)
    {
        if (!string.Equals(RestorePipelineSpanCommand.DigestOf(body.Bytes), command.Sha256, StringComparison.Ordinal))
        {
            // Another edit or another program changed the file since: the splices are offsets in bytes it no longer holds.
            return EditOutcome.Refused(command.IsRedo ? SplicedFile.DriftRedo : SplicedFile.DriftUndo);
        }

        body.Restore(command.Splices);
        return EditOutcome.Applied;
    }
}
