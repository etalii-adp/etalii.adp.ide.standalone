using System.Security.Claims;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    public static ClaimsPrincipal ToClaimsPrincipal(ClientPrincipal clientPrincipal, ILogger logger)
    {
        if (string.IsNullOrEmpty(clientPrincipal.UserId))
        {
            logger.LogError("UserId is empty whilst converting to ClaimsPrincipal");
        }
        if (string.IsNullOrEmpty(clientPrincipal.ExternalIdentifier))
        {
            logger.LogError("ExternalIdentifier is empty whilst converting to ClaimsPrincipal");
        }

        clientPrincipal.UserRoles = clientPrincipal.UserRoles
            .Except(["anonymous"], StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (!clientPrincipal.UserRoles.Any())
        {
            throw new ApplicationException("ClientPrincipal.UserRoles is empty whilst converting to ClaimsPrincipal");
        }

        var identity = new ClaimsIdentity(clientPrincipal.IdentityProvider);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, clientPrincipal.UserId!));
        identity.AddClaim(new Claim(ClaimTypes.Name, clientPrincipal.UserDetails!));
        identity.AddClaim(new Claim(ClaimTypes.Sid, clientPrincipal.ExternalIdentifier));
        identity.AddClaims(clientPrincipal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(identity);
    }
    
    public static ClientPrincipal ToClientPrincipal(IIdentity identity, ILogger logger) => ToClientPrincipal((ClaimsIdentity)identity, logger);

    private static ClientPrincipal ToClientPrincipal(ClaimsIdentity? claimsIdentity, ILogger logger)
    {
        logger.LogInformation("Checking claims");
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (claimsIdentity is null)
        {
            logger.LogInformation("No identity found");
            return null!;
        }
        if (!(claimsIdentity.Claims ?? []).Any())
        {
            logger.LogInformation("No claims found");
            return null!;
        }
        var claims = claimsIdentity.Claims!
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray();

        logger.LogInformation("Checking userId");
        var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        logger.LogInformation("Checking userDetails");
        var userDetails = claimsIdentity.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

        var principal = new ClientPrincipal
        {
            IdentityProvider = claimsIdentity.AuthenticationType,
            UserRoles = claims,
            UserId = userId,
            UserDetails = userDetails,
            ExternalIdentifier = claimsIdentity.FindFirst(ClaimTypes.Sid)?.Value ?? string.Empty,
        };
        if (string.IsNullOrEmpty(principal.UserId))
        {
            logger.LogError("ClaimsIdentity.UserId is empty whilst converting to ClientPrincipal");
        }
        if (string.IsNullOrEmpty(principal.ExternalIdentifier))
        {
            logger.LogError("ClaimsIdentity.ExternalIdentifier is empty whilst converting to ClientPrincipal");
        }
        return principal;
    }
}