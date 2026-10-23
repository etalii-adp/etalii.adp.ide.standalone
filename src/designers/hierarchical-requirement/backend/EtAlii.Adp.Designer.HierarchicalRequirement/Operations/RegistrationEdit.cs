using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// One change of a list the registration keeps, the tags, categories or states, made in the same step as the body's
/// (spec 029 R17; FBL 0.7 section 8.4). A registration the body does not have yet is created first, outside the edit, as
/// the binding's <c>createOnFirstPlacement</c> says; the first change of a list its block does not hold writes the whole
/// list, its members as the document has them with the change applied (DESL 0.2 sections 5.5 and 7.7.2).
/// </summary>
internal static class RegistrationEdit
{
    /// <summary>
    /// The registration's bytes before and after <paramref name="setting"/>: before is the registration as it is, or as
    /// it is created when <paramref name="registration"/> is null; or the sentence saying why it cannot be written.
    /// </summary>
    /// <param name="body">The body's bytes, which a created registration takes its line ending from.</param>
    /// <param name="members">The members of each list as the document has them now, implicit ones included, by block.</param>
    public static (byte[]? Before, byte[]? After, string Refusal) Apply(
        byte[] body, string bodyName, byte[]? registration, string registrationName, Func<string, IReadOnlyList<KeyValuePair<string, string>>> members, ModelChange.Setting setting)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(setting);
        var created = RegistrationCreation.Create(HierarchicalRequirementDefinition.Binding, HierarchicalRequirementDefinition.Origin, registrationName, bodyName, body, registration);
        if (created.Bytes is null) return (null, null, created.Refusal ?? "");

        var open = OpenRegistration.Open(created.Bytes, HierarchicalRequirementDefinition.Binding);
        open.AbsentMembers = block => members(block) is { Count: > 0 } listed ? listed : null;
        return open.Change(setting) switch
        {
            PlanResult.Refused refused => (null, null, refused.Reason),
            _ => (created.Bytes, open.Bytes, ""),
        };
    }
}
