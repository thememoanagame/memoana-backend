namespace MemoAna.Domain.Matchmaking;

public sealed record PlayerSlot(PlayerIdentity Player, int Number, bool IsReady);
