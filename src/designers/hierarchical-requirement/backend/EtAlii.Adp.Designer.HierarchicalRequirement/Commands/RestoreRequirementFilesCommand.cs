using EtAlii.Adp.History;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// Puts the files of one step back as they were before it: the body, or the body and its registration when the step
/// wrote both (spec 029 R17), after Knowledge's <c>RestoreKnowledgeFilesCommand</c>. Every file is restored, or none.
/// </summary>
/// <param name="Files">Each file with its bytes from before the step and after it; the body first.</param>
/// <param name="Redo">The original command, so redoing the undo makes the same edit again.</param>
internal sealed record RestoreRequirementFilesCommand(IReadOnlyList<RequirementFileState> Files, ICommand Redo) : IDocumentBoundCommand
{
    public string BodyPath => Files[0].Path;
}
