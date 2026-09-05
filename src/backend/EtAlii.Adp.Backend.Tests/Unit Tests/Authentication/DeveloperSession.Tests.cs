#if DEBUG
using EtAlii.Adp.Backend.Authentication;
using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The developer sign-in bypass (developer-sign-in-bypass Requirements 1.3, 1.4, 2.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this file can and cannot prove.</b> It compiles Debug, where the bypass exists by
/// design, so it cannot demonstrate that the bypass is absent from a release - a test asserting
/// that here would either assert the wrong thing or assert a default, and would pass whether or
/// not the release were safe. That question is answered by a step in the release job, against a
/// Release-published artifact, which is the only check that reads a different input.
/// </para>
/// <para>
/// What this file does prove is the two things a Debug suite can honestly observe: that the
/// configuration condition really is consulted, and that the token the bypass mints is
/// indistinguishable downstream from one obtained by typing the credential. The second is the
/// load-bearing one - if it fails, every manual check run under the bypass is evidence about
/// the bypass rather than about the product.
/// </para>
/// </remarks>
public class DeveloperSessionTests
{
    private const string Username = "admin";
    private const string Credential = "changeme";
    private const string DeveloperSessionMethod = "/etalii.adp.AuthenticationService/DeveloperSession";
    private const string LoginMethod = "/etalii.adp.AuthenticationService/Login";
    private const string SomeGuardedMethod = "/etalii.adp.ProjectService/ListProjects";

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "EtAlii.Adp.Backend.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static LocalAuthenticatorOptions Options(bool disabled = false) => new()
    {
        Username = Username,
        Credential = Credential,
        DeveloperSessionDisabled = disabled,
    };

    private static Backend.Authentication.AuthenticationService Service(
        ISessionStore store,
        LocalAuthenticatorOptions options,
        string environmentName = "Development") =>
        new(new LocalAuthenticator(MsOptions.Create(options)),
            store,
            MsOptions.Create(options),
            new StubEnvironment(environmentName));

    private static SessionInterceptor Interceptor(ISessionStore store, LocalAuthenticatorOptions options) =>
        new(store, MsOptions.Create(options));

    private static ServerCallContext CreateContext(string method, Metadata? requestHeaders = null) =>
        TestServerCallContext.Create(
            method: method,
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(1),
            requestHeaders: requestHeaders ?? new Metadata(),
            cancellationToken: TestContext.Current.CancellationToken,
            peer: "test-peer",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => WriteOptions.Default,
            writeOptionsSetter: _ => { });

    private static Task<string> Handler(string request, ServerCallContext context) =>
        Task.FromResult(request);

    [Fact]
    public void IsExempt_ForDeveloperSession_IsFalseWhenTheBypassIsTurnedOff()
    {
        // Arrange.
        // Turning the bypass off should close the route, not merely make the route answer
        // nothing - so the configuration condition is asserted at the door, rather than
        // inferred from a handler that happened to refuse.
        var interceptor = Interceptor(new InMemorySessionStore(), Options(disabled: true));

        // Assert.
        Assert.False(interceptor.IsExempt(DeveloperSessionMethod));

        // And the two exemptions that have nothing to do with this spec are untouched by it.
        Assert.True(interceptor.IsExempt(LoginMethod));
        Assert.True(interceptor.IsExempt("/etalii.adp.AuthenticationService/DescribeProduct"));
    }

    [Fact]
    public void IsExempt_ForDeveloperSession_IsTrueInAnOrdinaryDeveloperBuild()
    {
        // Arrange.
        var interceptor = Interceptor(new InMemorySessionStore(), Options());

        // Assert: the companion of the test above. Without it that one would keep passing if
        // the exemption were deleted altogether, and would then be asserting nothing.
        Assert.True(interceptor.IsExempt(DeveloperSessionMethod));
    }

    [Fact]
    public async Task TheBypassToken_IsAcceptedExactlyAsALoginTokenIs()
    {
        // Arrange.
        // One store behind both and one interceptor in front of both: the comparison is only
        // worth something if nothing differs except how the token was obtained.
        var store = new InMemorySessionStore();
        var service = Service(store, Options());
        var interceptor = Interceptor(store, Options());

        // Act.
        var bypassed = await service.DeveloperSession(new DeveloperSessionRequest(), CreateContext(DeveloperSessionMethod));
        var signedIn = await service.Login(
            new LoginRequest { Username = Username, Credential = Credential },
            CreateContext(LoginMethod));

        // Assert.
        Assert.Equal(DeveloperSessionResponse.ResultOneofCase.Session, bypassed.ResultCase);
        Assert.True(bypassed.Bypassed);

        // The point of the whole spec: a guarded call accepts the bypass token, and accepts it
        // for the same reason it accepts a typed-credential one. A token minted through a
        // parallel path would fail here - which is what makes this worth writing rather than
        // reading the two code paths and believing they agree.
        var withBypassToken = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, bypassed.Session.Value } };
        var withLoginToken = new Metadata { { SessionInterceptor.SessionTokenMetadataKey, signedIn.Session.Value } };

        Assert.Equal("ping", await interceptor.UnaryServerHandler("ping", CreateContext(SomeGuardedMethod, withBypassToken), Handler));
        Assert.Equal("ping", await interceptor.UnaryServerHandler("ping", CreateContext(SomeGuardedMethod, withLoginToken), Handler));

        // And it is the same identity, not merely a second valid token: what a check exercises
        // under the bypass has to be what a person would exercise (Requirement 1.4).
        Assert.True(store.TryValidate(bypassed.Session.Value, out var bypassedUser));
        Assert.True(store.TryValidate(signedIn.Session.Value, out var signedInUser));
        Assert.Equal(signedInUser, bypassedUser);
    }

    [Fact]
    public async Task TheBypass_MintsNothing_WhenItIsTurnedOff()
    {
        // Arrange.
        var store = new InMemorySessionStore();
        var service = Service(store, Options(disabled: true));

        // Act.
        var response = await service.DeveloperSession(new DeveloperSessionRequest(), CreateContext(DeveloperSessionMethod));

        // Assert.
        Assert.Equal(DeveloperSessionResponse.ResultOneofCase.None, response.ResultCase);
        Assert.False(response.Bypassed);
    }

    [Fact]
    public async Task TheBypass_MintsNothing_OutsideDevelopment()
    {
        // Arrange.
        // The belt for a Debug build shipped somewhere by accident. It is not the load-bearing
        // condition - the compilation is - but it is what would matter if that guarantee were
        // ever weakened.
        var store = new InMemorySessionStore();
        var service = Service(store, Options(), Environments.Production);

        // Act.
        var response = await service.DeveloperSession(new DeveloperSessionRequest(), CreateContext(DeveloperSessionMethod));

        // Assert.
        Assert.Equal(DeveloperSessionResponse.ResultOneofCase.None, response.ResultCase);
    }
}
#endif
