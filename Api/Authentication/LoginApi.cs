using System.Net;
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

            if (!request.TryAuthentication(_logger, out var response, out var principal)) return response;

            var externalIdentifier = $"{principal.UserId}@{principal.IdentityProvider}";

            await using var context = await _dbContextFactory.CreateDbContextAsync();

            var user = await context.Users.SingleOrDefaultAsync(u => u.ExternalIdentifier == externalIdentifier);
            if (user == null)
            {
                user = new User
                {
                    Id = UserIdentifier.NewIdentifier(),
                    Name = principal.UserDetails!,
                    ExternalIdentifier = externalIdentifier,
                    JoinDate = DateTime.UtcNow,
                    Theme = Theme.Light,
                };

                context.Entry(user).State = EntityState.Added;
                await context.SaveChangesAsync();
            }
            response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(user);
            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unable to handle {RequestMethod}", request.Method);
            return request.HandleFailure(_logger, e);
        }
    }
}