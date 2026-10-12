namespace EtAlii.Adp.Diagram.Alpha.Tests;

public sealed class AlphaTests
{
    public async System.Threading.Tasks.Task Waits()
    {
        await System.Threading.Tasks.Task.Delay(500);
        System.Threading.Thread.Sleep(10);
        // Task.Delay(1) in a comment does not count.
        await Eventually(() => true);
    }

    private static System.Threading.Tasks.Task Eventually(System.Func<bool> condition)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
