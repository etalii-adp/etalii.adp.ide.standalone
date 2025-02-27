using System.Net.Http.Json;

namespace EtAlii.Adp.Client;

public static class HttpClientLoggingExtensions
{
    public static async Task<TItem?> GetFromJsonWithLoggingAsync<TItem>(this HttpClient client, string requestUri, ILogger logger)
        where TItem : class
    {
        var response = await client.GetAsync(requestUri);
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("GET request to {RequestUri} succeeded", requestUri);
            var result = await response.Content.ReadFromJsonAsync<TItem>();
            return result!;
        }

        logger.LogInformation("GET request to {RequestUri} failed", requestUri);

        // TODO: Remove - security risk
        if (!response.Headers.TryGetValues("Exception", out var values)) return null!;
        
        var base64EncodedException = values.First();
        var exception = Base64Url.Decode(base64EncodedException);
        logger.LogError("Diagnostics message: {Exception}", exception);

        return null!;
    }
}