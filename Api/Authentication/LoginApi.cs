using System.Net;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EtAlii.Adp.Api;

public class LoginApi
{
    private readonly ILogger _logger;
    
    private const AuthorizationLevel _authorizationLevel = AuthorizationLevel.Anonymous;
    
    private readonly IDbContextFactory<AdpDbContext> _dbContextFactory;

    public LoginApi(ILoggerFactory loggerFactory, IDbContextFactory<AdpDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;

        _logger = loggerFactory.CreateLogger<LoginApi>();
        _logger.LogInformation("Initialized {FunctionApi}", nameof(LoginApi));
    }
    
    [Function(ApplicationApi.Authentication.Get.Function)]
    public async Task<HttpResponseData> LoginUser([HttpTrigger(_authorizationLevel, HttpMethodName.Get, Route = ApplicationApi.Authentication.Get.Function)] HttpRequestData request)
    {
        try
        {
            _logger.LogInformation("Handling {FunctionName}", request.FunctionContext.FunctionDefinition.Name);

            var claims = ClientPrincipal.Parse(request, _logger);

            var userName = claims.Identity!.Name!;
            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ApplicationException("User name cannot be empty");
            }
            var externalIdentifier = claims.FindFirst(c => c.Type == ClaimTypes.Sid)!.Value;
            if (string.IsNullOrWhiteSpace(externalIdentifier))
            {
                throw new ApplicationException("ExternalIdentifier cannot be empty");
            }
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var user = await context.Users.SingleOrDefaultAsync(u => u.ExternalIdentifier == externalIdentifier);
            if (user == null)
            {
                user = new User
                {
                    Id = UserIdentifier.NewIdentifier(),
                    Name = userName,
                    ExternalIdentifier = externalIdentifier,
                    JoinDate = DateTime.UtcNow,
                };

                context.Entry(user).State = EntityState.Added;
                await context.SaveChangesAsync();
            }
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(user);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {RequestMethod}", request.Method);
            var response = request.CreateResponse(HttpStatusCode.FailedDependency);
            // TODO: Remove - security risk
            response.Headers.Add("Diagnostics", e.ToString());
            return response;
        }
    }

}