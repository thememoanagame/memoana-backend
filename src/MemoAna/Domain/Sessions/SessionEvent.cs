using MemoAna.Domain.Matchmaking;

namespace MemoAna.Domain.Sessions;

public abstract record SessionEvent(ulong Sequence, ulong StateVersion);

public sealed record PlayerJoinedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    int Slot,
    int ConnectedPlayers)
    : SessionEvent(Sequence, StateVersion);

public sealed record PlayerReadyChangedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    bool IsReady)
    : SessionEvent(Sequence, StateVersion);

public sealed record SessionStateChangedEvent(
    ulong Sequence,
    ulong StateVersion,
    SessionLifecycle State)
    : SessionEvent(Sequence, StateVersion);

public sealed record GameStartedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity FirstPlayer)
    : SessionEvent(Sequence, StateVersion);

public sealed record CardFlipAcceptedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    int Position,
    int Score)
    : SessionEvent(Sequence, StateVersion);

public sealed record PlayerLeftEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    LeaveReason Reason)
    : SessionEvent(Sequence, StateVersion);

public sealed record SessionFinishedEvent(
    ulong Sequence,
    ulong StateVersion,
    SessionLifecycle State,
    MatchResult Result,
    PlayerIdentity? Winner)
    : SessionEvent(Sequence, StateVersion);
