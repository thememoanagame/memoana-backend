namespace MemoAna.Domain.Sessions;

/// <summary>
/// Represents the authoritative lifecycle state of a match session.
/// </summary>
public enum SessionLifecycle
{
    /// <summary>The session is waiting for both players.</summary>
    WaitingForPlayers,
    /// <summary>Both players are ready and the session can start.</summary>
    Ready,
    /// <summary>The game is accepting gameplay commands.</summary>
    Running,
    /// <summary>The game completed normally.</summary>
    Completed,
    /// <summary>The session ended before normal completion.</summary>
    Aborted,
    /// <summary>The session expired before normal completion.</summary>
    Expired
}

/// <summary>
/// Provides lifecycle-state helper operations.
/// </summary>
public static class SessionLifecycleExtensions
{
    /// <summary>
    /// Determines whether a lifecycle state is terminal.
    /// </summary>
    /// <param name="lifecycle">The lifecycle state to inspect.</param>
    /// <returns><see langword="true"/> when the state cannot transition further; otherwise, <see langword="false"/>.</returns>
    public static bool IsTerminal(this SessionLifecycle lifecycle) =>
        lifecycle is SessionLifecycle.Completed
            or SessionLifecycle.Aborted
            or SessionLifecycle.Expired;
}
