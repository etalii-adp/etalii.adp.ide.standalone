using Xunit;

namespace EtAlii.Adp.Authentication.Tests;

/// <summary>
/// Which calls need no session (Requirement 1.6). The developer bypass's own entry is asserted in
/// <see cref="DeveloperSessionTests"/>, which compiles only where the bypass does.
/// </summary>
public class SignInExemptionsTests
{
    [Fact]
    public void IsExempt_ForLogin_IsTrue()
    {
        // Arrange.
        var exemptions = new SignInExemptions();

        // Assert: Login is how a token is first obtained.
        Assert.True(exemptions.IsExempt("/etalii.adp.AuthenticationService/Login"));
    }

    [Fact]
    public void IsExempt_ForDescribeProduct_IsTrue()
    {
        // Arrange: the login page names the product version before any session exists
        // (github-build-pipeline R3.2) - the exemption is the wire that makes that possible.
        var exemptions = new SignInExemptions();

        // Assert.
        Assert.True(exemptions.IsExempt("/etalii.adp.AuthenticationService/DescribeProduct"));
    }

    [Fact]
    public void IsExempt_ForAnyOtherCall_IsFalse()
    {
        // Arrange.
        var exemptions = new SignInExemptions();

        // Assert.
        Assert.False(exemptions.IsExempt("/etalii.adp.ProjectService/ListProjects"));
    }
}
