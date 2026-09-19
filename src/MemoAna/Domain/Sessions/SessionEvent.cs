using MemoAna.Domain.Matchmaking;

namespace MemoAna.Domain.Sessions;

/// <summary>
/// Represents an authoritative fact emitted by a match session.
/// </summary>
/// <param name="Sequence">The monotonically increasing session event sequence.</param>
/// <param name="StateVersion">The authoritative state version after the associated transition.</param>
/// <remarks>
/// Events are immutable projections of state transitions and are intended for transport/application publication.
/// </remarks>
public abstract record SessionEvent(ulong Sequence, ulong StateVersion);

/// <summary>
/// Indicates that a player joined the session.
/// </summary>
public sealed record PlayerJoinedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    int Slot,
    int ConnectedPlayers)
    : SessionEvent(Sequence, StateVersion);

/// <summary>
/// Indicates that a player's readiness changed.
/// </summary>
public sealed record PlayerReadyChangedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    bool IsReady)
    : SessionEvent(Sequence, StateVersion);

/// <summary>
/// Indicates that the session lifecycle changed.
/// </summary>
public sealed record SessionStateChangedEvent(
    ulong Sequence,
    ulong StateVersion,
    SessionLifecycle State)
    : SessionEvent(Sequence, StateVersion);

/// <summary>
/// Indicates that gameplay has started.
/// </summary>
public sealed record GameStartedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity FirstPlayer)
    : SessionEvent(Sequence, StateVersion);

/// <summary>
/// Indicates that a card flip was accepted by the authoritative game state.
/// </summary>
public sealed record CardFlipAcceptedEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    int Position,
    int Score)
    : SessionEvent(Sequence, StateVersion);

/// <summary>
/// Indicates that a player left the session.
/// </summary>
public sealed record PlayerLeftEvent(
    ulong Sequence,
    ulong StateVersion,
    PlayerIdentity Player,
    LeaveReason Reason)
    : SessionEvent(Sequence, StateVersion);

/// <summary>
/// Indicates that the session reached a terminal state.
/// </summary>
public sealed record SessionFinishedEvent(
    ulong Sequence,
    ulong StateVersion,
    SessionLifecycle State,
    MatchResult Result,
    PlayerIdentity? Winner)
    : SessionEvent(Sequence, StateVersion);
