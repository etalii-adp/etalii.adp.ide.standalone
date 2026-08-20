using Grpc.Core;

namespace EtAlii.Adp.Backend.Sessions;

/// <summary>
/// Single source of truth for stashing/reading the authenticated username on
/// a call's UserState, so SessionInterceptor and service implementations agree
/// on the storage key without duplicating it.
/// </summary>
public static class SessionContext
{
    private const string UserStateKey = "username";

    public static void SetUsername(ServerCallContext context, string username) =>
        context.UserState[UserStateKey] = username;

    public static string GetUsername(ServerCallContext context) =>
        (string)context.UserState[UserStateKey]!;
}
