using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;

namespace MemoAna.Application.Matchmaking;

/// <summary>Configures the bounds of the in-memory matchmaking queue.</summary>
public sealed record MatchmakingOptions
{
    /// <summary>Gets the maximum number of active waiting players.</summary>
    public int MaximumWaitingPlayers { get; init; } = 1024;

    internal void Validate()
    {
        if (MaximumWaitingPlayers <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumWaitingPlayers),
                "Maximum waiting players must be positive.");
        }
    }
}

/// <summary>Provides process-local, thread-safe matchmaking and session lookup.</summary>
/// <remarks>Pairing is serialized under a private gate; session command mutation remains serialized by each session runtime.</remarks>
public sealed class InMemoryMatchmaking : IAsyncDisposable, IMatchmakingSessionGateway
{
    private readonly object _gate = new();
    private readonly LinkedList<WaitingEntry> _waiting = [];
    private readonly Dictionary<string, WaitingEntry> _activePlayers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MatchFound> _matchedPlayers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<SessionIdentity, IMatchSessionEventSource> _matchedSessions = [];
    private readonly Dictionary<SessionIdentity, IMatchSessionRuntime> _sessionRuntimes = [];
    private readonly IMatchmakingCompatibilityPolicy _compatibilityPolicy;
    private readonly IServerMatchmakingIdentityGenerator _identityGenerator;
    private readonly IMatchSessionRuntimeFactory _runtimeFactory;
    private readonly MatchmakingOptions _options;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMatchmaking"/> class.
    /// </summary>
    /// <param name="runtimeFactory">Match Session Factory</param>
    /// <param name="identityGenerator">Server Identity Generator</param>
    /// <param name="compatibilityPolicy">Compatibility Policy</param>
    /// <param name="options">Matchmaking Options</param>
    /// <exception cref="ArgumentNullException">thrown when param runtimeFactory null</exception>
    public InMemoryMatchmaking(
        IMatchSessionRuntimeFactory runtimeFactory,
        IServerMatchmakingIdentityGenerator? identityGenerator = null,
        IMatchmakingCompatibilityPolicy? compatibilityPolicy = null,
        MatchmakingOptions? options = null)
    {
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        _identityGenerator = identityGenerator ?? new GuidMatchmakingIdentityGenerator();
        _compatibilityPolicy = compatibilityPolicy ?? new DefaultMatchmakingCompatibilityPolicy();
        _options = options ?? new MatchmakingOptions();
        _options.Validate();
    }

    /// <summary>Gets the number of currently waiting players.</summary>
    public int WaitingCount
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Count(entry => entry.IsWaiting);
            }
        }
    }

    /// <summary>Registers a player and either waits or completes a compatible pair.</summary>
    /// <param name="request">The matchmaking request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The current matchmaking result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the request contains an invalid player key.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when matchmaking has been disposed.</exception>
    public Task<MatchmakingResult> JoinAsync(
        JoinMatchmakingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        WaitingEntry? incoming;
        WaitingEntry? opponent;
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_matchedPlayers.TryGetValue(request.PlayerKey, out var existingMatch))
            {
                return Task.FromResult(new MatchmakingResult(
                    MatchmakingStatus.AlreadyMatched,
                    request.PlayerKey,
                    request.EffectiveRequestId,
                    existingMatch,
                    "Player already owns a matched session."));
            }

            if (_activePlayers.ContainsKey(request.PlayerKey))
            {
                return Task.FromResult(new MatchmakingResult(
                    MatchmakingStatus.AlreadyWaiting,
                    request.PlayerKey,
                    request.EffectiveRequestId,
                    Reason: "Player already has an active matchmaking request."));
            }

            if (_activePlayers.Count >= _options.MaximumWaitingPlayers)
            {
                return Task.FromResult(new MatchmakingResult(
                    MatchmakingStatus.Rejected,
                    request.PlayerKey,
                    request.EffectiveRequestId,
                    Reason: "Matchmaking capacity is full."));
            }

            opponent = FindAndClaimCompatible(request);
            incoming = new WaitingEntry(request);
            _activePlayers.Add(request.PlayerKey, incoming);

            if (opponent is null)
            {
                incoming.WaitingNode = _waiting.AddLast(incoming);
                RegisterCancellation(incoming, cancellationToken);
                return Task.FromResult(new MatchmakingResult(
                    MatchmakingStatus.Waiting,
                    request.PlayerKey,
                    request.EffectiveRequestId));
            }

            incoming.IsWaiting = false;
            incoming.IsClaimed = true;
            RegisterCancellation(incoming, cancellationToken);
        }

        return CompletePairingAsync(opponent, incoming, cancellationToken);
    }

    /// <summary>Cancels a player's active waiting request.</summary>
    /// <param name="request">The cancellation request.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The cancellation outcome.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the player key is invalid.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    public Task<MatchmakingResult> CancelAsync(
        CancelMatchmakingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PlayerKey))
        {
            throw new ArgumentException("Player key is required.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        CancellationTokenRegistration cancellationRegistration;
        MatchmakingResult result;
        lock (_gate)
        {
            if (!_activePlayers.TryGetValue(request.PlayerKey, out var entry))
            {
                var status = _matchedPlayers.ContainsKey(request.PlayerKey)
                    ? MatchmakingStatus.AlreadyMatched
                    : MatchmakingStatus.NotFound;
                return Task.FromResult(new MatchmakingResult(
                    status,
                    request.PlayerKey,
                    entry?.Request.EffectiveRequestId ?? request.PlayerKey));
            }

            if (entry.IsClaimed)
            {
                return Task.FromResult(new MatchmakingResult(
                    MatchmakingStatus.AlreadyMatched,
                    request.PlayerKey,
                    entry.Request.EffectiveRequestId,
                    Reason: "Pairing has already claimed the player."));
            }

            RemoveWaitingEntry(entry);
            result = new MatchmakingResult(
                MatchmakingStatus.Cancelled,
                entry.Request.PlayerKey,
                entry.Request.EffectiveRequestId);
            cancellationRegistration = CompleteEntry(entry, result);
        }

        cancellationRegistration.Dispose();
        return Task.FromResult(result);
    }

    /// <summary>Waits for an active request to become matched.</summary>
    /// <param name="playerKey">The player key to await.</param>
    /// <param name="cancellationToken">The token used to cancel the wait.</param>
    /// <returns>The current or eventual matchmaking result.</returns>
    /// <exception cref="ArgumentException">Thrown when the player key is invalid.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    public async Task<MatchmakingResult> WaitForMatchAsync(
        string playerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerKey))
        {
            throw new ArgumentException("Player key is required.", nameof(playerKey));
        }

        WaitingEntry entry;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_matchedPlayers.TryGetValue(playerKey, out var match))
            {
                return new MatchmakingResult(
                    MatchmakingStatus.Matched,
                    playerKey,
                    playerKey,
                    match);
            }

            if (!_activePlayers.TryGetValue(playerKey, out entry!))
            {
                return new MatchmakingResult(
                    MatchmakingStatus.NotFound,
                    playerKey,
                    playerKey);
            }
        }

        return await entry.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Attempts to subscribe to events from a matched session.</summary>
    /// <param name="session">The session identity.</param>
    /// <param name="cancellationToken">The token used to cancel subscription creation.</param>
    /// <param name="events">The event stream when found.</param>
    /// <returns><see langword="true"/> when the session exists.</returns>
    public bool TrySubscribe(
        SessionIdentity session,
        CancellationToken cancellationToken,
        out IAsyncEnumerable<SessionEvent>? events)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_matchedSessions.TryGetValue(session, out var source))
            {
                events = source.Subscribe(cancellationToken);
                return true;
            }
        }

        events = null;
        return false;
    }

    /// <summary>Attempts to retrieve the runtime associated with a matched session.</summary>
    /// <param name="session">The session identity.</param>
    /// <param name="runtime">The runtime when found.</param>
    /// <returns><see langword="true"/> when the runtime exists.</returns>
    public bool TryGetRuntime(
        SessionIdentity session,
        out IMatchSessionRuntime? runtime)
    {
        lock (_gate)
        {
            return _sessionRuntimes.TryGetValue(session, out runtime);
        }
    }

    /// <summary>Stops matchmaking and completes all active waiting requests.</summary>
    /// <returns>A value task that completes after active waiters are released.</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        WaitingEntry[] entries;
        List<CancellationTokenRegistration> cancellationRegistrations = [];
        lock (_gate)
        {
            entries = _activePlayers.Values.ToArray();
            _activePlayers.Clear();
            _matchedPlayers.Clear();
            _matchedSessions.Clear();
            _sessionRuntimes.Clear();
            _waiting.Clear();
            foreach (var entry in entries)
            {
                entry.IsWaiting = false;
                entry.IsClaimed = false;
                var cancellationRegistration = CompleteEntry(entry, new MatchmakingResult(
                    MatchmakingStatus.Cancelled,
                    entry.Request.PlayerKey,
                    entry.Request.EffectiveRequestId,
                    Reason: "Matchmaking was disposed."));
                cancellationRegistrations.Add(cancellationRegistration);
            }
        }

        foreach (var cancellationRegistration in cancellationRegistrations)
        {
            cancellationRegistration.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<MatchmakingResult> CompletePairingAsync(
        WaitingEntry opponent,
        WaitingEntry incoming,
        CancellationToken cancellationToken)
    {
        IMatchSessionRuntime? runtime = null;
        try
        {
            var identities = _identityGenerator.Create();
            var first = new MatchmakingPlayerAssignment(
                opponent.Request.PlayerKey,
                identities.FirstPlayer,
                1);
            var second = new MatchmakingPlayerAssignment(
                incoming.Request.PlayerKey,
                identities.SecondPlayer,
                2);

            runtime = await _runtimeFactory.CreateAsync(
                identities.Session,
                identities.Match,
                cancellationToken).ConfigureAwait(false);

            var firstJoin = await runtime.SubmitAsync(
                new JoinSessionCommand(
                    $"match-{identities.Match.Value}-join-1",
                    first.Player),
                cancellationToken).ConfigureAwait(false);
            var secondJoin = await runtime.SubmitAsync(
                new JoinSessionCommand(
                    $"match-{identities.Match.Value}-join-2",
                    second.Player),
                cancellationToken).ConfigureAwait(false);

            if (!firstJoin.Accepted || !secondJoin.Accepted)
            {
                throw new InvalidOperationException("Session rejected a matchmaking attachment.");
            }

            var eventSource = runtime as IMatchSessionEventSource;

            var firstMatch = new MatchFound(identities.Match, identities.Session, first, second);
            var secondMatch = new MatchFound(identities.Match, identities.Session, second, first);
            CancellationTokenRegistration opponentCancellation;
            CancellationTokenRegistration incomingCancellation;
            lock (_gate)
            {
                _activePlayers.Remove(opponent.Request.PlayerKey);
                _activePlayers.Remove(incoming.Request.PlayerKey);
                _matchedPlayers[opponent.Request.PlayerKey] = firstMatch;
                _matchedPlayers[incoming.Request.PlayerKey] = secondMatch;
                if (eventSource is not null)
                {
                    _matchedSessions[identities.Session] = eventSource;
                }
                _sessionRuntimes[identities.Session] = runtime;
                opponentCancellation = CompleteEntry(opponent, new MatchmakingResult(
                    MatchmakingStatus.Matched,
                    opponent.Request.PlayerKey,
                    opponent.Request.EffectiveRequestId,
                    firstMatch));
                incomingCancellation = CompleteEntry(incoming, new MatchmakingResult(
                    MatchmakingStatus.Matched,
                    incoming.Request.PlayerKey,
                    incoming.Request.EffectiveRequestId,
                    secondMatch));
            }
            opponentCancellation.Dispose();
            incomingCancellation.Dispose();

            return new MatchmakingResult(
                MatchmakingStatus.Matched,
                incoming.Request.PlayerKey,
                incoming.Request.EffectiveRequestId,
                secondMatch);
        }
        catch (OperationCanceledException)
        {
            await RollbackPairingAsync(
                opponent,
                incoming,
                runtime,
                cancellationToken.IsCancellationRequested).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await RollbackPairingAsync(opponent, incoming, runtime, false).ConfigureAwait(false);
            return new MatchmakingResult(
                MatchmakingStatus.Rejected,
                incoming.Request.PlayerKey,
                incoming.Request.EffectiveRequestId,
                Reason: "The session could not be created.");
        }
    }

    private async Task RollbackPairingAsync(
        WaitingEntry opponent,
        WaitingEntry incoming,
        IMatchSessionRuntime? runtime,
        bool cancelIncoming)
    {
        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }

        CancellationTokenRegistration incomingCancellation = default;
        var hasIncomingCancellation = false;
        lock (_gate)
        {
            _activePlayers.Remove(opponent.Request.PlayerKey);
            _activePlayers.Remove(incoming.Request.PlayerKey);
            RestoreWaiting(opponent);
            if (cancelIncoming)
            {
                incoming.IsWaiting = false;
                incoming.IsClaimed = false;
                incomingCancellation = CompleteEntry(incoming, new MatchmakingResult(
                    MatchmakingStatus.Cancelled,
                    incoming.Request.PlayerKey,
                    incoming.Request.EffectiveRequestId,
                    Reason: "Matchmaking request was cancelled."));
                hasIncomingCancellation = true;
            }
            else
            {
                RestoreWaiting(incoming);
            }
        }

        if (hasIncomingCancellation)
        {
            incomingCancellation.Dispose();
        }
    }

    private WaitingEntry? FindAndClaimCompatible(JoinMatchmakingRequest request)
    {
        for (var node = _waiting.First; node is not null; node = node.Next)
        {
            var candidate = node.Value;
            if (!candidate.IsWaiting ||
                !_compatibilityPolicy.IsCompatible(request, candidate.Request))
            {
                continue;
            }

            candidate.IsWaiting = false;
            candidate.IsClaimed = true;
            _waiting.Remove(node);
            candidate.WaitingNode = null;
            return candidate;
        }

        return null;
    }

    private void RestoreWaiting(WaitingEntry entry)
    {
        entry.IsClaimed = false;
        if (_activePlayers.ContainsKey(entry.Request.PlayerKey))
        {
            return;
        }

        entry.IsWaiting = true;
        _activePlayers.Add(entry.Request.PlayerKey, entry);
        entry.WaitingNode = _waiting.AddLast(entry);
    }

    private void RemoveWaitingEntry(WaitingEntry entry)
    {
        entry.IsWaiting = false;
        entry.IsClaimed = false;
        if (entry.WaitingNode is not null)
        {
            _waiting.Remove(entry.WaitingNode);
            entry.WaitingNode = null;
        }

        _activePlayers.Remove(entry.Request.PlayerKey);
    }

    private static CancellationTokenRegistration CompleteEntry(
        WaitingEntry entry,
        MatchmakingResult result)
    {
        var cancellationRegistration = entry.CancellationRegistration;
        entry.CancellationRegistration = default;
        entry.Completion.TrySetResult(result);
        return cancellationRegistration;
    }

    private void RegisterCancellation(WaitingEntry entry, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return;
        }

        entry.CancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var values = (CancellationState)state!;
                values.Matchmaking.CancelFromToken(values.Entry);
            },
            new CancellationState(this, entry));
    }

    private void CancelFromToken(WaitingEntry entry)
    {
        lock (_gate)
        {
            if (!entry.IsWaiting || !_activePlayers.ContainsKey(entry.Request.PlayerKey))
            {
                return;
            }

            RemoveWaitingEntry(entry);
            CompleteEntry(entry, new MatchmakingResult(
                MatchmakingStatus.Cancelled,
                entry.Request.PlayerKey,
                entry.Request.EffectiveRequestId,
                Reason: "Matchmaking request was cancelled."));
        }
    }

    private void ValidateRequest(JoinMatchmakingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlayerKey))
        {
            throw new ArgumentException("Player key is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.EffectiveRequestId))
        {
            throw new ArgumentException("Request id is required.", nameof(request));
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(InMemoryMatchmaking));
        }
    }

    private sealed class WaitingEntry(JoinMatchmakingRequest request)
    {
        public JoinMatchmakingRequest Request { get; } = request;
        public bool IsWaiting { get; set; } = true;
        public bool IsClaimed { get; set; }
        public LinkedListNode<WaitingEntry>? WaitingNode { get; set; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public TaskCompletionSource<MatchmakingResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record CancellationState(InMemoryMatchmaking Matchmaking, WaitingEntry Entry);
}
