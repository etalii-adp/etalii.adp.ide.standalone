using Microsoft.Azure.Functions.Worker.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    private const string _clientPrincipalHeader = "x-ms-client-principal";

    public static ClaimsPrincipal Parse(HttpRequestData req, ILogger logger)
    {
        logger.LogInformation("Parsing headers");
        if (!req.Headers.TryGetValues(_clientPrincipalHeader, out var headers))
        {
            throw new HttpRequestException($"No header found for {_clientPrincipalHeader}");
            logger.LogInformation("No matching header found");
            return ToClaimsPrincipal(new ClientPrincipal(), logger);
        }
        var header = headers.First();
        var decoded = Convert.FromBase64String(header);
        var json = Encoding.UTF8.GetString(decoded);
        var clientPrincipal = JsonSerializer.Deserialize<ClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return ToClaimsPrincipal(clientPrincipal, logger);
    }

    public static void SetHeader(HttpClient client, ClaimsPrincipal principal, ILogger logger)
    {
        var clientPrincipal = ToClientPrincipal(principal.Identity, logger);

        logger.LogInformation("Setting headers");
        var headers = client.DefaultRequestHeaders;
        var json = JsonSerializer.Serialize(clientPrincipal, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var encoded = Encoding.UTF8.GetBytes(json);
        var header = Convert.ToBase64String(encoded);
        headers.Remove(_clientPrincipalHeader);
        headers.Add(_clientPrincipalHeader, header);
        logger.LogInformation("Finished setting headers");
    }
}