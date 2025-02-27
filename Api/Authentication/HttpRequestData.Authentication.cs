#if !DEBUG    
using System.Net;
#endif
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public static class HttpRequestDataAuthenticationExtensions
{
    public static bool TryAuthentication(this HttpRequestData request, ILogger logger, out HttpResponseData response, out ClientPrincipal clientPrincipal)
    {
        clientPrincipal = ClientPrincipal.Parse(request, logger);

#if !DEBUG    
            if (clientPrincipal.UserRoles.Contains("authenticated"))
            {
                response = request.CreateResponse(HttpStatusCode.Unauthorized);
                return false;
            }
#endif

        response = null!;
        return true;
    }
}