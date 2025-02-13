using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public class WakeUpApi
{
    private readonly ILogger _logger;
    
    private const AuthorizationLevel _authorizationLevel = AuthorizationLevel.Anonymous;

    public WakeUpApi(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DiagramsApi>();
        _logger.LogInformation("Initialized {FunctionApi}", nameof(WakeUpApi));
    }
    
    [Function(ApplicationApi.WakeUp.Get.Function)]
    public async Task<HttpResponseData> Wakeup([HttpTrigger(_authorizationLevel, HttpMethodName.Get, Route = ApplicationApi.WakeUp.Get.Function)] HttpRequestData request)
    {
        try
        {
            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(String.Empty);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {RequestMethod}", request.Method);
            var response = request.CreateResponse(HttpStatusCode.FailedDependency);
            return response;
        }
    }

}