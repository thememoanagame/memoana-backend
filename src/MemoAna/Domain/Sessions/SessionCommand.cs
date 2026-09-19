using MemoAna.Domain.Matchmaking;

namespace MemoAna.Domain.Sessions;

public abstract record SessionCommand(string CommandId, PlayerIdentity? Player, ulong ClientSequence)
{
    public bool IsSystemCommand => Player is null;
}

public sealed record JoinSessionCommand(
    string CommandId,
    PlayerIdentity JoiningPlayer,
    ulong ClientSequence = 1)
    : SessionCommand(CommandId, JoiningPlayer, ClientSequence);

public sealed record ReadyUpCommand(
    string CommandId,
    PlayerIdentity PlayerId,
    bool Ready,
    ulong ClientSequence)
    : SessionCommand(CommandId, PlayerId, ClientSequence);

public sealed record StartGameCommand(string CommandId)
    : SessionCommand(CommandId, null, 0);

public sealed record LeaveSessionCommand(
    string CommandId,
    PlayerIdentity PlayerId,
    LeaveReason Reason,
    ulong ClientSequence)
    : SessionCommand(CommandId, PlayerId, ClientSequence);

public sealed record FlipCardCommand(
    string CommandId,
    PlayerIdentity PlayerId,
    int Position,
    ulong ClientSequence)
    : SessionCommand(CommandId, PlayerId, ClientSequence);

public sealed record AbortSessionCommand(string CommandId)
    : SessionCommand(CommandId, null, 0);

public sealed record ExpireSessionCommand(string CommandId)
    : SessionCommand(CommandId, null, 0);
