using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;

namespace MemoAna.Application.Matchmaking;

public sealed record JoinMatchmakingRequest(
    string PlayerKey,
    string? CompatibilityKey = null,
    string? RequestId = null)
{
    public string EffectiveRequestId => RequestId ?? PlayerKey;
    public string EffectiveCompatibilityKey => CompatibilityKey ?? "default";
}

public sealed record CancelMatchmakingRequest(string PlayerKey);

public enum MatchmakingStatus
{
    Waiting,
    Matched,
    AlreadyWaiting,
    AlreadyMatched,
    Cancelled,
    NotFound,
    Rejected
}

public sealed record MatchmakingPlayerAssignment(
    string PlayerKey,
    PlayerIdentity Player,
    int Slot);

public sealed record MatchFound(
    MatchIdentity Match,
    SessionIdentity Session,
    MatchmakingPlayerAssignment Self,
    MatchmakingPlayerAssignment Opponent);

public sealed record MatchmakingResult(
    MatchmakingStatus Status,
    string PlayerKey,
    string RequestId,
    MatchFound? Match = null,
    string? Reason = null)
{
    public bool IsSuccess => Status is MatchmakingStatus.Waiting or MatchmakingStatus.Matched;
}

public interface IMatchmakingCompatibilityPolicy
{
    bool IsCompatible(JoinMatchmakingRequest incoming, JoinMatchmakingRequest waiting);
}

public sealed class DefaultMatchmakingCompatibilityPolicy : IMatchmakingCompatibilityPolicy
{
    public bool IsCompatible(JoinMatchmakingRequest incoming, JoinMatchmakingRequest waiting) =>
        string.Equals(
            incoming.EffectiveCompatibilityKey,
            waiting.EffectiveCompatibilityKey,
            StringComparison.Ordinal);
}

public sealed record MatchmakingIdentitySet(
    MatchIdentity Match,
    SessionIdentity Session,
    PlayerIdentity FirstPlayer,
    PlayerIdentity SecondPlayer);

public interface IServerMatchmakingIdentityGenerator
{
    MatchmakingIdentitySet Create();
}

public sealed class GuidMatchmakingIdentityGenerator : IServerMatchmakingIdentityGenerator
{
    public MatchmakingIdentitySet Create() =>
        new(
            new MatchIdentity(Guid.NewGuid().ToString("N")),
            new SessionIdentity(Guid.NewGuid().ToString("N")),
            new PlayerIdentity(Guid.NewGuid().ToString("N")),
            new PlayerIdentity(Guid.NewGuid().ToString("N")));
}

public interface IMatchSessionRuntime
{
    Task<SessionCommandResult> SubmitAsync(
        SessionCommand command,
        CancellationToken cancellationToken = default);

    ValueTask DisposeAsync();
}

public interface IMatchSessionRuntimeFactory
{
    Task<IMatchSessionRuntime> CreateAsync(
        SessionIdentity session,
        MatchIdentity match,
        CancellationToken cancellationToken = default);
}
