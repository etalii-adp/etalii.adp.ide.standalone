using Microsoft.Azure.Functions.Worker.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace EtAlii.Adp.Api;

public static class StaticWebAppsApiAuth
{
    private const string _clientPrincipalHeader = "x-ms-client-principal";

    public static ClaimsPrincipal Parse(HttpRequestData req)
    {
        var clientPrincipal = new ClientPrincipal();

        if (req.Headers.TryGetValues(_clientPrincipalHeader, out var headers))
        {
            var header = headers.First();
            var decoded = Convert.FromBase64String(header);
            var json = Encoding.UTF8.GetString(decoded);
            clientPrincipal = JsonSerializer.Deserialize<ClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        return AuthenticationHelper.GetClaimsPrincipalFromClientPrincipal(clientPrincipal);
    }
}