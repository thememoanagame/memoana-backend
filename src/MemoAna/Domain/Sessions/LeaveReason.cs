namespace MemoAna.Domain.Sessions;

/// <summary>
/// Describes why a player left a session.
/// </summary>
public enum LeaveReason
{
    /// <summary>The player left voluntarily.</summary>
    Voluntary,
    /// <summary>The player's connection was disconnected.</summary>
    Disconnected,
    /// <summary>The session terminated the player's participation.</summary>
    SessionTerminated
}
