using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Fbl.Routing;

/// <summary>
/// What a host asks of a binding to route a body by its content (FBL sections 8.4, 12.1 and 12.3): whether the binding
/// claims a file, and whether it creates its registration on first placement. The answers are the library's own routing.
/// </summary>
public static class BodyClaim
{
    /// <summary>Whether <paramref name="binding"/> claims a file named <paramref name="fileName"/> whose first bytes are <paramref name="head"/> (FBL section 12.3).</summary>
    public static bool Claims(FblBinding binding, string fileName, byte[] head) => Router.Candidates(fileName, head, [binding]).Count > 0;

    /// <summary>Whether any of <paramref name="bindings"/> claims a file named <paramref name="fileName"/> whose first bytes are <paramref name="head"/> (FBL section 12.3).</summary>
    public static bool Claims(IEnumerable<FblBinding> bindings, string fileName, byte[] head) => Router.Candidates(fileName, head, bindings).Count > 0;

    /// <summary>
    /// Whether <paramref name="binding"/> takes a file named <paramref name="fileName"/> by its name alone (FBL section 12.1): by
    /// its <c>names</c> or its <c>extensions</c>, before its marker is looked at. Never true of a <c>registrationOnly</c> binding.
    /// </summary>
    public static bool Takes(FblBinding binding, string fileName)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(fileName);
        var name = Path.GetFileName(fileName);
        return Router.Takes(binding.Claims, name, Path.GetExtension(name));
    }

    /// <summary>
    /// The mark a file needs before <paramref name="binding"/> claims it (FBL section 12.2), as a person reads it: a root key
    /// with its value, or the text a first line begins with. Null for a binding without a marker, or with a pattern, which is
    /// an expression rather than text to copy.
    /// </summary>
    public static string? Mark(FblBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Claims.Marker switch
        {
            { RootKey: { } key, RootValue: { } value } => $"the root key '{key}: {BindingReader.ScalarText(value)}'",
            { RootKey: { } key } => $"the root key '{key}'",
            { FirstLine: { } prefix } => $"a first line that begins with '{prefix}'",
            _ => null,
        };
    }

    /// <summary>Whether a binding's registration is created the first time the user places an element or changes a setting (FBL 0.7 section 8.4).</summary>
    public static bool CreatesRegistration(FblBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Registration.CreateOnFirstPlacement;
    }
}
