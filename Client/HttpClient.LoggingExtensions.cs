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
        else
        {
            logger.LogInformation("GET request to {RequestUri} failed", requestUri);
            // TODO: Remove - security risk
            if (response.TrailingHeaders.TryGetValues("Diagnostics", out var values))
            {
                var diagnostics = string.Join(',', values);
                logger.LogError("Diagnostics message: {Diagnostics}", diagnostics);
            }

            var content = await response.Content.ReadAsStringAsync();
            logger.LogError("Content returned: {Content}", content);
            return null!;
        }
    }
}