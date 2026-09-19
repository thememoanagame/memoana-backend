namespace MemoAna.Domain.Sessions;

public enum SessionLifecycle
{
    WaitingForPlayers,
    Ready,
    Running,
    Completed,
    Aborted,
    Expired
}

public static class SessionLifecycleExtensions
{
    public static bool IsTerminal(this SessionLifecycle lifecycle) =>
        lifecycle is SessionLifecycle.Completed
            or SessionLifecycle.Aborted
            or SessionLifecycle.Expired;
}
