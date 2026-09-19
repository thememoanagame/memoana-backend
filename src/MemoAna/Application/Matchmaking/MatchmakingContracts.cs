using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;

namespace MemoAna.Application.Matchmaking;

/// <summary>Describes a request to enter matchmaking.</summary>
/// <param name="PlayerKey">The caller-owned key used by the matchmaking boundary.</param>
/// <param name="CompatibilityKey">An optional key restricting compatible opponents.</param>
/// <param name="RequestId">An optional caller request identifier.</param>
public sealed record JoinMatchmakingRequest(string PlayerKey, string? CompatibilityKey = null, string? RequestId = null)
{
    /// <summary>Gets the explicit request identifier, or <see cref="PlayerKey"/> when omitted.</summary>
    public string EffectiveRequestId => RequestId ?? PlayerKey;
    /// <summary>Gets the explicit compatibility key, or the default compatibility group.</summary>
    public string EffectiveCompatibilityKey => CompatibilityKey ?? "default";
}

/// <summary>Describes a request to cancel active matchmaking.</summary>
/// <param name="PlayerKey">The caller-owned matchmaking key.</param>
public sealed record CancelMatchmakingRequest(string PlayerKey);

/// <summary>Represents the server-assigned identifier for a lobby room.</summary>
public readonly record struct RoomIdentity
{
    /// <summary>Initializes a room identity.</summary>
    /// <param name="value">The room identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when the room identifier is empty.</exception>
    public RoomIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Room identity is required.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the room identifier value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Represents the room lifecycle used by the lobby flow.</summary>
public enum RoomState
{
    /// <summary>The room is waiting for a second player.</summary>
    WaitingForOpponent,
    /// <summary>The room is ready for the authoritative game to start.</summary>
    Ready,
    /// <summary>The game is actively running.</summary>
    Running,
    /// <summary>The game completed normally.</summary>
    Completed,
    /// <summary>The room ended because a player left or the server aborted it.</summary>
    Aborted,
    /// <summary>The room expired before a second player joined.</summary>
    Expired
}

/// <summary>Represents the authoritative game configuration assigned to a session.</summary>
/// <param name="Theme">The room theme selected by the host.</param>
/// <param name="Difficulty">The room difficulty selected by the host.</param>
/// <param name="BoardSeed">The deterministic seed used to build the board on both clients.</param>
public sealed record GameConfiguration(string Theme, string Difficulty, string BoardSeed);

/// <summary>Represents a server-owned room in the lobby.</summary>
public sealed record GameRoom(
    RoomIdentity RoomId,
    SessionIdentity SessionId,
    PlayerIdentity HostPlayer,
    string HostNickname,
    string Theme,
    string Difficulty,
    string BoardSeed,
    RoomState State,
    DateTimeOffset CreatedAt,
    PlayerIdentity? GuestPlayer = null,
    string? GuestNickname = null);

/// <summary>Describes a listing item for a discoverable room.</summary>
public sealed record RoomListItem(
    RoomIdentity RoomId,
    string HostNickname,
    string Theme,
    string Difficulty,
    DateTimeOffset CreatedAt,
    int ConnectedPlayers,
    int MaximumPlayers);

/// <summary>Creates a new room/lobby for a host.</summary>
/// <param name="Nickname">The host nickname shown to guests.</param>
/// <param name="Theme">The theme for the authoritative match configuration.</param>
/// <param name="Difficulty">The difficulty for the authoritative match configuration.</param>
public sealed record CreateRoomRequest(string Nickname, string Theme, string Difficulty);

/// <summary>Contains the result of creating a room.</summary>
public sealed record CreateRoomResult(GameRoom Room, GameConfiguration Configuration, PlayerIdentity PlayerId, string? Reason = null)
{
    /// <summary>Gets whether the room was created successfully.</summary>
    public bool IsSuccess => Reason is null;
}

/// <summary>Requests that a guest enter a specific waiting room.</summary>
/// <param name="RoomId">The room to join.</param>
/// <param name="Nickname">The guest nickname shown to the host.</param>
public sealed record JoinRoomRequest(RoomIdentity RoomId, string Nickname);

/// <summary>Indicates the outcome of a join-room request.</summary>
public enum RoomJoinStatus
{
    /// <summary>The room was joined successfully.</summary>
    Success,
    /// <summary>The room could not be located.</summary>
    RoomNotFound,
    /// <summary>The room is not accepting a second player.</summary>
    RoomNotAvailable,
    /// <summary>The guest nickname is invalid.</summary>
    InvalidNickname,
    /// <summary>The provided room identifier is invalid.</summary>
    InvalidRoomId,
    /// <summary>The room already has both players assigned.</summary>
    AlreadyFull,
    /// <summary>The server rejected the room join request.</summary>
    Rejected
}

/// <summary>Contains the result of joining a room.</summary>
public sealed record JoinRoomResult(
    RoomJoinStatus Status,
    RoomIdentity RoomId,
    SessionIdentity SessionId,
    PlayerIdentity? PlayerId = null,
    GameConfiguration? Configuration = null,
    GameRoom? Room = null,
    string? Reason = null)
{
    /// <summary>Gets whether the join succeeded.</summary>
    public bool IsSuccess => Status == RoomJoinStatus.Success;
}

/// <summary>Describes the outcome state of a matchmaking request.</summary>
public enum MatchmakingStatus
{
    /// <summary>The player is waiting for an opponent.</summary>
    Waiting,
    /// <summary>The player has been paired.</summary>
    Matched,
    /// <summary>The player already has an active waiting request.</summary>
    AlreadyWaiting,
    /// <summary>The player already owns a match.</summary>
    AlreadyMatched,
    /// <summary>The waiting request was cancelled.</summary>
    Cancelled,
    /// <summary>No active request or match was found.</summary>
    NotFound,
    /// <summary>The request could not be fulfilled.</summary>
    Rejected
}

/// <summary>Describes a server-assigned player position in a match.</summary>
/// <param name="PlayerKey">The matchmaking key associated with the player.</param>
/// <param name="Player">The server-generated player identity.</param>
/// <param name="Slot">The one-based session slot.</param>
public sealed record MatchmakingPlayerAssignment(string PlayerKey, PlayerIdentity Player, int Slot);

/// <summary>Describes a complete match assignment from one player's perspective.</summary>
/// <param name="Match">The server-generated match identity.</param>
/// <param name="Session">The server-generated session identity.</param>
/// <param name="Self">The assignment belonging to the requesting player.</param>
/// <param name="Opponent">The opponent assignment.</param>
public sealed record MatchFound(MatchIdentity Match, SessionIdentity Session, MatchmakingPlayerAssignment Self, MatchmakingPlayerAssignment Opponent);

/// <summary>Contains the result of a matchmaking operation.</summary>
/// <param name="Status">The matchmaking outcome.</param>
/// <param name="PlayerKey">The associated matchmaking key.</param>
/// <param name="RequestId">The effective request identifier.</param>
/// <param name="Match">The match assignment when pairing succeeds.</param>
/// <param name="Reason">An optional human-readable explanation for a non-success outcome.</param>
public sealed record MatchmakingResult(MatchmakingStatus Status, string PlayerKey, string RequestId, MatchFound? Match = null, string? Reason = null)
{
    /// <summary>Gets whether the operation is in a normal waiting or matched state.</summary>
    public bool IsSuccess => Status is MatchmakingStatus.Waiting or MatchmakingStatus.Matched;
}

/// <summary>Determines whether two matchmaking requests may be paired.</summary>
public interface IMatchmakingCompatibilityPolicy
{
    /// <summary>Determines whether the incoming request is compatible with a waiting request.</summary>
    /// <param name="incoming">The incoming request.</param>
    /// <param name="waiting">The waiting request.</param>
    /// <returns><see langword="true"/> when the requests may be paired.</returns>
    /// <exception cref="ArgumentNullException">Thrown by an implementation when a request is null.</exception>
    bool IsCompatible(JoinMatchmakingRequest incoming, JoinMatchmakingRequest waiting);
}

/// <summary>Matches requests whose effective compatibility keys are equal.</summary>
public sealed class DefaultMatchmakingCompatibilityPolicy : IMatchmakingCompatibilityPolicy
{
    /// <inheritdoc />
    public bool IsCompatible(JoinMatchmakingRequest incoming, JoinMatchmakingRequest waiting) =>
        string.Equals(incoming.EffectiveCompatibilityKey, waiting.EffectiveCompatibilityKey, StringComparison.Ordinal);
}

/// <summary>Contains server-generated identities required to create a match and its players.</summary>
/// <param name="Match">The match identity.</param>
/// <param name="Session">The session identity.</param>
/// <param name="FirstPlayer">The first player identity.</param>
/// <param name="SecondPlayer">The second player identity.</param>
public sealed record MatchmakingIdentitySet(MatchIdentity Match, SessionIdentity Session, PlayerIdentity FirstPlayer, PlayerIdentity SecondPlayer);

/// <summary>Generates identities used by server-side matchmaking.</summary>
public interface IServerMatchmakingIdentityGenerator
{
    /// <summary>Creates a new identity set.</summary>
    /// <returns>A set of server-generated identities.</returns>
    MatchmakingIdentitySet Create();
}

/// <summary>Generates match, session, and player identities using GUID values.</summary>
public sealed class GuidMatchmakingIdentityGenerator : IServerMatchmakingIdentityGenerator
{
    /// <inheritdoc />
    public MatchmakingIdentitySet Create() =>
        new(new MatchIdentity(Guid.NewGuid().ToString("N")), new SessionIdentity(Guid.NewGuid().ToString("N")),
            new PlayerIdentity(Guid.NewGuid().ToString("N")), new PlayerIdentity(Guid.NewGuid().ToString("N")));
}

/// <summary>Provides authoritative command submission and lifecycle ownership for one session runtime.</summary>
public interface IMatchSessionRuntime
{
    /// <summary>Submits a command to the serialized session processor.</summary>
    /// <param name="command">The command to process.</param>
    /// <param name="cancellationToken">The token used to cancel queueing or awaiting the result.</param>
    /// <returns>The authoritative command result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the runtime is closed.</exception>
    Task<SessionCommandResult> SubmitAsync(SessionCommand command, CancellationToken cancellationToken = default);

    /// <summary>Disposes the runtime and waits for its processor to finish.</summary>
    /// <returns>A value task that completes when the runtime has stopped.</returns>
    ValueTask DisposeAsync();
}

/// <summary>Exposes authoritative events produced by a session runtime.</summary>
public interface IMatchSessionEventSource
{
    /// <summary>Subscribes to future session events.</summary>
    /// <param name="cancellationToken">The token used to stop enumeration.</param>
    /// <returns>An asynchronous sequence of authoritative events.</returns>
    IAsyncEnumerable<SessionEvent> Subscribe(CancellationToken cancellationToken = default);
}

/// <summary>Coordinates matchmaking requests and access to matched session runtimes.</summary>
public interface IMatchmakingSessionGateway
{
    /// <summary>Enters the specified player into matchmaking.</summary>
    /// <param name="request">The matchmaking request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The current matchmaking result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when required request fields are invalid.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    Task<MatchmakingResult> JoinAsync(JoinMatchmakingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Waits until a player is matched or the operation is cancelled.</summary>
    /// <param name="playerKey">The matchmaking key to await.</param>
    /// <param name="cancellationToken">The token used to cancel the wait.</param>
    /// <returns>The resulting matchmaking state.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="playerKey"/> is invalid.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    Task<MatchmakingResult> WaitForMatchAsync(string playerKey, CancellationToken cancellationToken = default);

    /// <summary>Attempts to subscribe to events for a matched session.</summary>
    /// <param name="session">The session identity.</param>
    /// <param name="cancellationToken">The token used to cancel subscription creation.</param>
    /// <param name="events">The event stream when the session exists; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the session exists.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    bool TrySubscribe(SessionIdentity session, CancellationToken cancellationToken, out IAsyncEnumerable<SessionEvent>? events);

    /// <summary>Attempts to retrieve a matched session runtime.</summary>
    /// <param name="session">The session identity.</param>
    /// <param name="runtime">The runtime when registered; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the runtime exists.</returns>
    bool TryGetRuntime(SessionIdentity session, out IMatchSessionRuntime? runtime);

    /// <summary>Creates a lobby room owned by the host.</summary>
    /// <param name="request">The room creation request.</param>
    /// <param name="cancellationToken">The token used to cancel creation.</param>
    /// <returns>The created room and its authoritative configuration.</returns>
    Task<CreateRoomResult> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists rooms available to receive a second player.</summary>
    /// <param name="cancellationToken">The token used to cancel the request.</param>
    /// <returns>A read-only snapshot of discoverable rooms.</returns>
    Task<IReadOnlyList<RoomListItem>> ListRoomsAsync(CancellationToken cancellationToken = default);

    /// <summary>Joins a guest to a waiting room and preserves the host configuration.</summary>
    /// <param name="request">The room join request.</param>
    /// <param name="cancellationToken">The token used to cancel the join.</param>
    /// <returns>The result of the join attempt.</returns>
    Task<JoinRoomResult> JoinRoomAsync(JoinRoomRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Creates authoritative session runtimes.</summary>
public interface IMatchSessionRuntimeFactory
{
    /// <summary>Creates a runtime for the specified session and match.</summary>
    /// <param name="session">The session identity.</param>
    /// <param name="match">The match identity.</param>
    /// <param name="cancellationToken">The token used to cancel creation.</param>
    /// <returns>The created runtime.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    Task<IMatchSessionRuntime> CreateAsync(SessionIdentity session, MatchIdentity match, CancellationToken cancellationToken = default);
}
