namespace EtAlii.Adp.Sessions;

/// <summary>
/// The calls a caller may make before it holds a session: what the <see cref="SessionInterceptor"/>
/// needs of whoever hands sessions out.
/// </summary>
/// <remarks>
/// Declared here and implemented by the authentication, so that the sign-in session code does not
/// reference the authentication project (code-structure FR-065): which calls are how a session is
/// first obtained is the authentication's knowledge, and the interceptor only asks.
/// </remarks>
public interface ISessionExemptions
{
    /// <summary>Whether a call of <paramref name="method"/>, the full gRPC method path, may proceed without a session token.</summary>
    bool IsExempt(string method);
}
