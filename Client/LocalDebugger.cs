namespace EtAlii.Adp.Client;

public static class LocalDebugger
{
#if DEBUG
    public static bool IsAttached {get; } = true;
#else
    public static bool IsAttached {get; } = false;
#endif
}