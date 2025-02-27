using Microsoft.Azure.Functions.Worker.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    private const string _clientPrincipalHeader = "x-ms-client-principal";

    public static ClientPrincipal Parse(HttpRequestData req, ILogger logger)
    {
        logger.LogInformation("Parsing headers");
        if (!req.Headers.TryGetValues(_clientPrincipalHeader, out var headers))
        {
            throw new ApplicationException($"No header found for {_clientPrincipalHeader}");
        }
        var header = headers.First();
        var json = Base64Url.Decode(header);
        var clientPrincipal = JsonSerializer.Deserialize<ClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return clientPrincipal;
    }

    public static void SetHeader(HttpClient client, ClientPrincipal clientPrincipal, ILogger logger)
    {
        logger.LogInformation("Setting headers");
        var headers = client.DefaultRequestHeaders;
        var json = JsonSerializer.Serialize(clientPrincipal, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var header = Base64Url.Encode(json);
        headers.Remove(_clientPrincipalHeader);
        headers.TryAddWithoutValidation(_clientPrincipalHeader, header);
        logger.LogInformation("Finished setting headers {UserId} {ExternalIdentifier}", clientPrincipal.UserId, clientPrincipal.ExternalIdentifier);
    }
}