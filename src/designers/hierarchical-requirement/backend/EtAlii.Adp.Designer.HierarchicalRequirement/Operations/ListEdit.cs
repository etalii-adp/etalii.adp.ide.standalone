namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// What an operation on a list the registration keeps writes - the tags, the categories, the states or the filters: the
/// body, and the registration with what it held before; null where it writes nothing (spec 029 R17).
/// </summary>
/// <param name="Body">The body's bytes after the operation, or null when they do not change.</param>
/// <param name="RegistrationBefore">The registration as the step starts from it: as it is, or as it is created first, outside the step.</param>
/// <param name="RegistrationAfter">The registration's bytes after the operation, or null when it is not written.</param>
/// <param name="Refusal">Why nothing was written, or empty.</param>
internal sealed record ListEdit(byte[]? Body, byte[]? RegistrationBefore, byte[]? RegistrationAfter, string Refusal)
{
    public static ListEdit Refused(string refusal) => new(null, null, null, refusal);
}
