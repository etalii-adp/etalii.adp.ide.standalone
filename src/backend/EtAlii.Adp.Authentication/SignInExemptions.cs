using EtAlii.Adp.Sessions;
#if DEBUG
using Microsoft.Extensions.Options;
#endif

namespace EtAlii.Adp.Authentication;

/// <summary>
/// The calls that may be made without a session: the ones that hand a session out, and the one
/// the sign-in page needs before anyone has signed in.
/// </summary>
/// <remarks>
/// Here rather than in the <see cref="SessionInterceptor"/> because which calls obtain a session is
/// this project's knowledge, and the interceptor asks through <see cref="ISessionExemptions"/>
/// (code-structure FR-065). It also keeps the developer bypass's exemption in this assembly,
/// beside its handler, where the release job's developer-session guard looks for it.
/// </remarks>
public sealed class SignInExemptions : ISessionExemptions
{
#if DEBUG
    private readonly LocalAuthenticatorOptions _localAuthenticator;

    public SignInExemptions(
        // Optional so the tests that need the exemptions alone construct them without options;
        // absent, the bypass is not disabled, which is its ordinary Debug state.
        IOptions<LocalAuthenticatorOptions>? localAuthenticator = null)
    {
        _localAuthenticator = localAuthenticator?.Value ?? new LocalAuthenticatorOptions();
    }
#endif

    /// <inheritdoc />
    /// <remarks>
    /// Whether a route is reachable without a credential is the single most consequential line
    /// of the sign-in, and it deserves to be asserted rather than inferred from a call that
    /// happened to succeed, so the tests ask it directly.
    /// </remarks>
    public bool IsExempt(string method) =>
        // Login is how a token is first obtained; DescribeProduct is how the login page names
        // the product version before any session exists - a version is not a secret, and the
        // page that shows it renders before anyone can log in (github-build-pipeline R3.2).
        method.EndsWith("/Login", StringComparison.Ordinal)
        || method.EndsWith("/DescribeProduct", StringComparison.Ordinal)
#if DEBUG
        // The developer sign-in bypass (developer-sign-in-bypass Requirement 2.1). This entry
        // and the handler it exempts are the whole of the bypass, and each sits inside a
        // #if DEBUG in its own file so that neither can survive into a release without the
        // other. The configuration check is repeated here rather than left to the handler:
        // turning the bypass off should close the route, not merely make the route answer
        // nothing.
        || (!_localAuthenticator.DeveloperSessionDisabled
            && method.EndsWith("/DeveloperSession", StringComparison.Ordinal))
#endif
        ;
}
