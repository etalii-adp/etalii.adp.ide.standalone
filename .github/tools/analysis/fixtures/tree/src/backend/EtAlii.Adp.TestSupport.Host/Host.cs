namespace EtAlii.Adp.TestSupport.Host;

public sealed class Host
{
    public async System.Threading.Tasks.Task StartAsync()
    {
        await System.Threading.Tasks.Task.Delay(100);
    }
}
