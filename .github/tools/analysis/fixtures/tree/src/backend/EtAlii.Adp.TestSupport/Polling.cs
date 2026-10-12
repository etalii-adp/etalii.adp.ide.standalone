namespace EtAlii.Adp.TestSupport;

/* A test helper: not named *.Tests, and test code all the same. */
public static class Polling
{
    public static void WaitUntil(System.Func<bool> condition)
    {
        while (!condition())
        {
            System.Threading.Thread.Sleep(10);
        }
    }
}
