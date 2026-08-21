using EtAlii.Adp.Backend.Authentication;
using Microsoft.Extensions.Options;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

public class LocalAuthenticatorTests
{
    private static LocalAuthenticator CreateAuthenticator(string username, string credential)
    {
        var options = Options.Create(new LocalAuthenticatorOptions { Username = username, Credential = credential });
        return new LocalAuthenticator(options);
    }

    [Fact]
    public void Validate_WithMatchingCredentials_ReturnsTrue()
    {
        var authenticator = CreateAuthenticator("developer", "secret");

        Assert.True(authenticator.Validate("developer", "secret"));
    }

    [Fact]
    public void Validate_WithWrongCredential_ReturnsFalse()
    {
        var authenticator = CreateAuthenticator("developer", "secret");

        Assert.False(authenticator.Validate("developer", "wrong"));
    }

    [Fact]
    public void Validate_WithWrongUsername_ReturnsFalse()
    {
        var authenticator = CreateAuthenticator("developer", "secret");

        Assert.False(authenticator.Validate("someone-else", "secret"));
    }
}
