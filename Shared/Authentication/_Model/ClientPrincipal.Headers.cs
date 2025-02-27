using Microsoft.Azure.Functions.Worker.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp;

public partial class ClientPrincipal
{
    private const string _clientPrincipalHeader = "x-ms-client-principal";
    private const string _clientPrincipalIdHeader = "X-MS-CLIENT-PRINCIPAL-ID";
    private const string _clientPrincipalNameHeader = "X-MS-CLIENT-PRINCIPAL-NAME";
    private const string _clientPrincipalIdentityProviderHeader = "X-MS-CLIENT-PRINCIPAL-IDP";

        
    public static ClientPrincipal Parse(HttpRequestData req, ILogger logger)
    {
        logger.LogInformation("Parsing headers");
        if (!req.Headers.TryGetValues(_clientPrincipalHeader, out var headers))
        {
            throw new ApplicationException($"No header found for {_clientPrincipalHeader}");
        }

        var principalHeaders = headers!.ToArray();
        if (principalHeaders.Length > 1)
        {
            var result = new List<string>();
            foreach (var h in principalHeaders)
            {
                var j = Base64Url.Decode(h);
                result.Add(j);
            }
            throw new ApplicationException($"Multiple headers found for {_clientPrincipalHeader}:\r\n\r\n{string.Join("\r\n", result)}");
        }
        var header = principalHeaders.First();
        var json = Base64Url.Decode(header);
        var principal = JsonSerializer.Deserialize<ClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        req.Headers.TryGetValues(_clientPrincipalIdHeader, out var ids);
        req.Headers.TryGetValues(_clientPrincipalNameHeader, out var names);
        req.Headers.TryGetValues(_clientPrincipalIdentityProviderHeader, out var providers);
        
        throw new InvalidOperationException($"Json check on header:\r\n\r\nName: {names.Single()}\r\nId: {ids.Single()}\r\nProvider: {providers.Single()}\r\n\r\n{json}");
        
        if (string.IsNullOrWhiteSpace(principal.UserDetails))
        {
            throw new ApplicationException($"Empty user details were found when parsing the {_clientPrincipalHeader} header");
        }
        if (string.IsNullOrWhiteSpace(principal.ExternalIdentifier))
        {
            throw new ApplicationException($"Empty external identifier was found when parsing the {_clientPrincipalHeader} header");
        }

        return principal;
    }

    public static void SetHeader(HttpClient client, ClientPrincipal clientPrincipal, ILogger logger)
    {
        logger.LogInformation("Setting headers");

        if (string.IsNullOrEmpty(clientPrincipal.UserDetails))
        {
            throw new ApplicationException($"No user details found to set in the {_clientPrincipalHeader} header");
        }
        if (string.IsNullOrEmpty(clientPrincipal.ExternalIdentifier))
        {
            throw new ApplicationException($"No external identifier found to set in the {_clientPrincipalHeader} header");
        }
        
        var headers = client.DefaultRequestHeaders;
        var json = JsonSerializer.Serialize(clientPrincipal, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var header = Base64Url.Encode(json);
        headers.Remove(_clientPrincipalHeader);
        headers.TryAddWithoutValidation(_clientPrincipalHeader, header);
        logger.LogInformation("Finished setting headers {UserDetails} {ExternalIdentifier}", clientPrincipal.UserDetails, clientPrincipal.ExternalIdentifier);
    }
}