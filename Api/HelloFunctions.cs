using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace EtAlii.Adp.Api;

public class HelloFunctions
{
    [Function("hello")]
    public HttpResponseData RunHello([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteStringAsync($"Hello from PUBLIC function");

        return response;
    }

    [Function("hello/protected")]
    public HttpResponseData RunProtectedHello([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        var user = StaticWebAppsApiAuth.Parse(req);

        HttpResponseData response;
        if (user.Identity == null || !user.Identity.IsAuthenticated)
        {
            response = req.CreateResponse(HttpStatusCode.Unauthorized);
            response.WriteStringAsync($"User does does not have access to function");
        }
        else
        {
            response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteStringAsync($"Hello '{user.Identity.Name}' from PROTECTED function");
        }

        return response;
    }

    [Function("hello/protected/admin")]
    public HttpResponseData RunProtectedAdminHello([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        var user = StaticWebAppsApiAuth.Parse(req);

        HttpResponseData response;
        if (!user.IsInRole("admin"))
        {
            response = req.CreateResponse(HttpStatusCode.Unauthorized);
            var message = $"Hi {user.Identity!.Name}. You are not authorized to access this function." +
                           "The 'admin' role is required, please contact the administrator.";
            response.WriteStringAsync(message);
        }
        else
        {
            response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteStringAsync($"Hello '{user.Identity!.Name}' from ADMIN PROTECTED function");
        }

        return response;
    }

    [Function("hello/protected/superadmin")]
    public HttpResponseData RunProtectedSuperAdminHello([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        var user = StaticWebAppsApiAuth.Parse(req);

        var isSuperAdmin = user.IsInRole("superadmin");
        var response = isSuperAdmin
            ? req.CreateResponse(HttpStatusCode.OK)
            : req.CreateResponse(HttpStatusCode.Unauthorized);

        var message = isSuperAdmin
            ? $"Hello from SUPER ADMIN PROTECTED function"
            : $"Hi {user.Identity!.Name}. You are not authorized to access this function." +
               "The 'superadmin' role is required, please contact the administrator.";

        response.WriteStringAsync(message);

        return response;
    }
}