using System.Reflection;
using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Serilog;
#if DEBUG
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
#endif

namespace EtAlii.Adp.Backend.Authentication;

public sealed class AuthenticationService : EtAlii.Adp.AuthenticationService.AuthenticationServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<AuthenticationService>();

    // The one product version, read once: what Nerdbank.GitVersioning stamped into this very
    // assembly (semver plus git height, commit id embedded). Never configuration - a stamped
    // binary cannot disagree with itself (github-build-pipeline Requirement 2.3).
    private static readonly string _productVersion =
        typeof(AuthenticationService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private readonly IAuthenticator _authenticator;
    private readonly ISessionStore _sessionStore;
#if DEBUG
    private readonly LocalAuthenticatorOptions? _localAuthenticator;
    private readonly IHostEnvironment? _environment;
#endif

    public AuthenticationService(
        IAuthenticator authenticator,
        ISessionStore sessionStore
#if DEBUG
        ,
        // Optional, so the tests that construct this service to exercise Login and
        // DescribeProduct keep compiling and keep saying what they said (Requirement 4.3).
        // Unwired, the bypass refuses: the absence of a configured environment is not a
        // development environment.
        IOptions<LocalAuthenticatorOptions>? localAuthenticator = null,
        IHostEnvironment? environment = null
#endif
        )
    {
        _authenticator = authenticator;
        _sessionStore = sessionStore;
#if DEBUG
        _localAuthenticator = localAuthenticator?.Value;
        _environment = environment;
#endif
    }

    /// <summary>
    /// The one place a session is minted, for a username that has already been established as
    /// this caller's by whatever means.
    /// </summary>
    /// <remarks>
    /// Extracted so that <see cref="Login" /> and the developer bypass cannot drift into two
    /// issuance paths. That is not tidiness: a token from a parallel path would make every
    /// check run under the bypass evidence about the bypass rather than about the product
    /// (developer-sign-in-bypass Requirement 1.3). Sharing the method is what makes them the
    /// same token by construction rather than by inspection.
    /// </remarks>
    private (ShortGuid UserId, string Token) IssueSessionFor(string username)
    {
        var userId = ShortGuid.FromName(username);
        return (userId, _sessionStore.Issue(userId));
    }

    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        if (!_authenticator.Validate(request.Username, request.Credential))
        {
            // Warning, not Information: a rejected login is what a brute-force attempt looks
            // like from here. The username is logged, never the credential.
            _logger.Warning("Login rejected for {Username}: the username or credential did not match", request.Username);
            return Task.FromResult(new LoginResponse
            {
                Error = new LoginError { Message = "Invalid username or credential." }
            });
        }

        var (userId, token) = IssueSessionFor(request.Username);
        // The user id, never the token: the token is a bearer credential for the whole session.
        _logger.Information("Login accepted for {Username}, issued a session for {UserId}", request.Username, userId);
        return Task.FromResult(new LoginResponse
        {
            Session = new SessionToken { Value = token }
        });
    }

    public override Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        _sessionStore.Revoke(request.Session.Value);
        _logger.Information("Session revoked for {UserId}", SessionContext.GetUserId(context));
        return Task.FromResult(new LogoutResponse());
    }

#if DEBUG
    /// <summary>
    /// Hands a locally running developer build a real session, so that using the application
    /// requires no credential entry at all (developer-sign-in-bypass Requirement 1.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This method exists only in a Debug build, together with its entry in
    /// <c>SessionInterceptor.IsExempt</c>.</b> The two are what the bypass is, and they are in
    /// the same shape of block in their two files so that neither can be left behind without
    /// the other: an exemption with no handler is inert, a handler with no exemption is
    /// unreachable, and a released build has neither.
    /// </para>
    /// <para>
    /// Three conditions guard it and only the first is load-bearing. The compilation is what
    /// makes the bypass <i>absent</i> from a release rather than present-and-disabled. The
    /// environment check is a belt for a Debug build shipped by accident. The configuration
    /// value can only ever subtract, because a released build has no handler for it to reach.
    /// </para>
    /// <para>
    /// The token comes from <see cref="IssueSessionFor" />, the same method <see cref="Login" />
    /// uses, for the identity in <c>LocalAuthenticator:Username</c> - so nothing downstream can
    /// tell this session from one obtained by typing the credential, which is exactly what makes
    /// a check run under it evidence about the product (Requirement 1.3).
    /// </para>
    /// </remarks>
    /// <summary>Whether this host is running as somebody's development machine.</summary>
    /// <remarks>
    /// <b>Not <c>IsDevelopment()</c> alone, and that is not a preference.</b> That helper matches
    /// the single literal <c>Development</c>, while this repository's only non-default
    /// environment is named <c>developer</c> - it is what <c>launchSettings.json</c> sets and
    /// what <c>appsettings.developer.json</c> is named for. Checking <c>IsDevelopment()</c> by
    /// itself would refuse the bypass in the one environment it exists for, and refuse it
    /// silently: the handler would answer nothing, the client would fall through to the sign-in
    /// form, and it would all look exactly as it does when the bypass is not compiled in at all.
    /// </remarks>
    private static bool IsADevelopmentEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("developer");

    public override Task<DeveloperSessionResponse> DeveloperSession(DeveloperSessionRequest request, ServerCallContext context)
    {
        if (_environment is null || !IsADevelopmentEnvironment(_environment))
        {
            // Worth a Warning rather than silence: a Debug build running outside a development
            // environment is itself the thing somebody should know about, whether or not the
            // bypass was wanted.
            _logger.Warning(
                "Refusing a developer session: this is a Debug build but the environment is {Environment}, which is not a development one",
                _environment?.EnvironmentName ?? "unknown");
            return Task.FromResult(new DeveloperSessionResponse());
        }

        if (_localAuthenticator is null || _localAuthenticator.DeveloperSessionDisabled)
        {
            _logger.Information("Refusing a developer session: LocalAuthenticator:DeveloperSessionDisabled is set");
            return Task.FromResult(new DeveloperSessionResponse());
        }

        var username = _localAuthenticator.Username;
        if (username.Length == 0)
        {
            // No configured developer identity means there is no identity to be, and inventing
            // an anonymous one would make checks exercise a principal no person ever is
            // (Requirement 1.4).
            _logger.Warning("Refusing a developer session: LocalAuthenticator:Username is not configured");
            return Task.FromResult(new DeveloperSessionResponse());
        }

        var (userId, token) = IssueSessionFor(username);
        _logger.Information(
            "Issued a developer session for {Username} as {UserId} without a credential; the sign-in form will not be shown",
            username, userId);
        return Task.FromResult(new DeveloperSessionResponse
        {
            Session = new SessionToken { Value = token },
            Bypassed = true,
        });
    }
#endif

    public override Task<DescribeProductResponse> DescribeProduct(DescribeProductRequest request, ServerCallContext context)
    {
        // Debug: issued on every login-page load, but the one line that correlates a client's
        // session with the exact binary that served it.
        _logger.Debug("Described the product as {Version}", _productVersion);
        return Task.FromResult(new DescribeProductResponse { Version = _productVersion });
    }
}
