namespace EtAlii.Adp.Client;

public static class LocalDebugger
{
#if DEBUG
    public const bool IsAttached = true;
#else
    public const bool IsAttached = false;
#endif
}