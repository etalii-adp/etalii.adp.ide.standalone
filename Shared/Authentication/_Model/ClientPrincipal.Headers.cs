using System.Net.Http.Headers;
using Microsoft.Azure.Functions.Worker.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace EtAlii.Adp;

public partial class ClientPrincipal
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

        return ToClaimsPrincipal(clientPrincipal);
    }

    public static void SetHeader(HttpRequestHeaders headers, ClaimsPrincipal principal)
    {
        var clientPrincipal = ToClientPrincipal(principal.Identity);
        
        var json = JsonSerializer.Serialize(clientPrincipal, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var encoded = Encoding.UTF8.GetBytes(json);
        var header = Convert.ToBase64String(encoded);
        headers.Remove(_clientPrincipalHeader);
        headers.Add(_clientPrincipalHeader, header);
    }
}