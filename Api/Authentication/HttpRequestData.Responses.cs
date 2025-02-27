using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public static class HttpRequestDataResponsesExtensions
{
    public static HttpResponseData HandleFailure(this HttpRequestData request, ILogger logger, Exception exception)
    {
        var response = request.CreateResponse(HttpStatusCode.FailedDependency);
        // TODO: Remove - security risk
        response.Headers.TryAddWithoutValidation("Exception", Base64Url.Encode(exception.ToString()));
        // await response.WriteStringAsync();
        return response;
    }
}