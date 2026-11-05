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

    /// <summary>Whether a binding's registration is created the first time the user places an element or changes a setting (FBL 0.7 section 8.4).</summary>
    public static bool CreatesRegistration(FblBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Registration.CreateOnFirstPlacement;
    }
}
