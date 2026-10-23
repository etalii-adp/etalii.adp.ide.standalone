using EtAlii.Adp.History;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// What adding a designer's document comes down to (spec 029 R5): a command that writes it, or a document that is
/// already there and opens as it is, or a refusal.
/// </summary>
/// <param name="Command">What writes the document; null when nothing is written.</param>
/// <param name="Destination">The file that is created, or the existing document that opens.</param>
/// <param name="Error">Why nothing is added, or empty.</param>
public sealed record DesignerAddPlan(ICommand? Command, string Destination, string Error)
{
    /// <summary>Whether the plan opens a document that is there, writing nothing.</summary>
    public bool OpensExisting => Command is null && Error.Length == 0;

    public static DesignerAddPlan Refused(string error) => new(null, "", error);
}
