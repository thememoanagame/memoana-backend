namespace MemoAna.Domain.Matchmaking;

/// <summary>
/// Represents a player's server-assigned position and readiness within a session.
/// </summary>
/// <param name="Player">The server-assigned player identity.</param>
/// <param name="Number">The one-based slot number.</param>
/// <param name="IsReady">Whether the player has declared readiness.</param>
public sealed record PlayerSlot(PlayerIdentity Player, int Number, bool IsReady);
