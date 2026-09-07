using Grpc.Core;

namespace EtAlii.Adp.Authentication;

/// <summary>
/// Single source of truth for stashing/reading the authenticated user's id on
/// a call's UserState, so SessionInterceptor and service implementations agree
/// on the storage key without duplicating it.
/// </summary>
public static class SessionContext
{
    private const string UserIdStateKey = "user-id";

    public static void SetUserId(ServerCallContext context, ShortGuid userId) =>
        context.UserState[UserIdStateKey] = userId;

    public static ShortGuid GetUserId(ServerCallContext context) => (ShortGuid)context.UserState[UserIdStateKey];
}
