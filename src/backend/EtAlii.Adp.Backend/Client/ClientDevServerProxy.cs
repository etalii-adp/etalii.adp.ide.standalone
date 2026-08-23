using Microsoft.AspNetCore.Http;
using Serilog;

namespace EtAlii.Adp.Backend.Client;

/// <summary>
/// Forwards requests that don't match a gRPC endpoint to the Vite dev server, so the browser
/// can talk to a single port during local F5 development while Vite still owns hot-module-reload
/// (per tech.md's Development tools section). Vite's injected HMR client connects its websocket
/// straight to the dev server's own port, so only plain HTTP request/response needs proxying here.
/// </summary>
public static class ClientDevServerProxy
{
    public const string HttpClientName = "ClientDevServerProxy";

    private static readonly string[] ExcludedRequestHeaders = ["Host"];
    private static readonly string[] ExcludedResponseHeaders = ["Transfer-Encoding"];
    private static readonly ILogger _logger = Log.ForContext(typeof(ClientDevServerProxy));

    public static async Task ProxyAsync(HttpContext context, HttpClient httpClient, Uri devServerBaseUri)
    {
        var targetUri = new Uri(devServerBaseUri, context.Request.Path + context.Request.QueryString);

        using var proxyRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            proxyRequest.Content = new StreamContent(context.Request.Body);
        }

        foreach (var header in context.Request.Headers)
        {
            if (ExcludedRequestHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!proxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                proxyRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        HttpResponseMessage proxyResponse;
        try
        {
            proxyResponse = await httpClient.SendAsync(
                proxyRequest, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }
        catch (HttpRequestException exception)
        {
            // Almost always the dev server simply not running, which otherwise shows up as a
            // blank page and an exception several layers away from the cause. Rethrown so the
            // request still fails exactly as it did before.
            _logger.Error(
                exception,
                "Could not reach the client dev server at {DevServerUri}; is it running?",
                devServerBaseUri);
            throw;
        }

        using (proxyResponse)
        {
            await CopyBackAsync(context, proxyResponse);
        }
    }

    private static async Task CopyBackAsync(HttpContext context, HttpResponseMessage proxyResponse)
    {
        context.Response.StatusCode = (int)proxyResponse.StatusCode;
        foreach (var header in proxyResponse.Headers.Concat(proxyResponse.Content.Headers))
        {
            if (ExcludedResponseHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("Transfer-Encoding");

        await proxyResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
}
