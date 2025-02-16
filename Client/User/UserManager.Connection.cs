namespace EtAlii.Adp.Client;

public partial class UserManager
{
    private async Task WaitUntilBackedIsResponsive()
    {
        bool canConnect;
        do
        {
            canConnect = await CanConnectAsync();
            if (!canConnect)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.5f));
            }
        } while (!canConnect);
    }
    private async Task<bool> CanConnectAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, ApplicationApi.WakeUp.Head.Request);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

}