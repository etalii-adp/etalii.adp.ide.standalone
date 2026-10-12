namespace EtAlii.Adp.TestSupport.Host;

/// <summary>The values every flow test signs in and waits with.</summary>
public static class FlowConstants
{
    /// <summary>The developer account <c>appsettings.developer.json</c> configures.</summary>
    public const string DeveloperUsername = "admin";

    /// <summary>The developer account's checked-in placeholder credential.</summary>
    public const string DeveloperCredential = "changeme";

    /// <summary>The metadata header a signed-in call carries its session token in.</summary>
    public const string SessionTokenHeader = "session-token";

    /// <summary>How long a flow test waits for one message on a stream before it fails.</summary>
    public static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);
}
