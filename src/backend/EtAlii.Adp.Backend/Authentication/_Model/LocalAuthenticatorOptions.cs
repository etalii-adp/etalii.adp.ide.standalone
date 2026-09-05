namespace EtAlii.Adp.Backend.Authentication;

public sealed class LocalAuthenticatorOptions
{
    public const string SectionName = "LocalAuthenticator";

    public string Username { get; init; } = string.Empty;
    public string Credential { get; init; } = string.Empty;

#if DEBUG
    /// <summary>
    /// Turns the developer sign-in bypass <b>off</b> in a local Debug build
    /// (developer-sign-in-bypass Requirement 2.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is deliberately a disable rather than an enable, and the difference is the point: an
    /// <c>Enable</c> flag would be a configuration value capable of switching a bypass on, and
    /// a config file is copied between environments far more often than a compilation is. This
    /// one can only subtract. Setting it in a production appsettings.json achieves nothing,
    /// because there is no handler there for it to reach.
    /// </para>
    /// <para>
    /// It sits inside the <c>#if</c> with everything else the bypass owns, so a released build
    /// does not carry even the knob.
    /// </para>
    /// </remarks>
    public bool DeveloperSessionDisabled { get; init; }
#endif
}
