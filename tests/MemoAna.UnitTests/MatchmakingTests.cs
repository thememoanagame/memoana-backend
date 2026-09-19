using MemoAna.Application.Matchmaking;
using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;
using MemoAna.Infrastructure.Sessions;

namespace MemoAna.UnitTests;

public sealed class MatchmakingTests
{
    [Fact]
    public async Task FirstPlayerWaitsAndSecondPlayerReceivesOneMatch()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        var waiting = await matchmaking.JoinAsync(new JoinMatchmakingRequest("A", RequestId: "request-a"), TestContext.Current.CancellationToken);
        var matched = await matchmaking.JoinAsync(new JoinMatchmakingRequest("B", RequestId: "request-b"), TestContext.Current.CancellationToken);
        var firstPlayerResult = await matchmaking.WaitForMatchAsync("A", TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.Waiting, waiting.Status);
        Assert.Equal(MatchmakingStatus.Matched, matched.Status);
        Assert.Equal(MatchmakingStatus.Matched, firstPlayerResult.Status);
        Assert.NotNull(matched.Match);
        Assert.Equal(matched.Match.Match, firstPlayerResult.Match!.Match);
        Assert.Equal(1, runtimeFactory.CreatedCount);
        Assert.Equal(matched.Match.Session, firstPlayerResult.Match.Session);
        Assert.NotEqual(matched.Match.Self.Player, matched.Match.Opponent.Player);
        Assert.Equal(2, runtimeFactory.Runtimes[0].JoinCommands.Count);
    }

    [Fact]
    public async Task PairingUsesFifoOrderAndDistinctServerOwnedSlots()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        await matchmaking.JoinAsync(new JoinMatchmakingRequest("A"), TestContext.Current.CancellationToken);
        await matchmaking.JoinAsync(new JoinMatchmakingRequest("B"), TestContext.Current.CancellationToken);
        await matchmaking.JoinAsync(new JoinMatchmakingRequest("C"), TestContext.Current.CancellationToken);
        await matchmaking.JoinAsync(new JoinMatchmakingRequest("D"), TestContext.Current.CancellationToken);

        var a = await matchmaking.WaitForMatchAsync("A", TestContext.Current.CancellationToken);
        var b = await matchmaking.WaitForMatchAsync("B", TestContext.Current.CancellationToken);
        var c = await matchmaking.WaitForMatchAsync("C", TestContext.Current.CancellationToken);
        var d = await matchmaking.WaitForMatchAsync("D", TestContext.Current.CancellationToken);

        Assert.Equal(a.Match!.Match, b.Match!.Match);
        Assert.Equal(c.Match!.Match, d.Match!.Match);
        Assert.NotEqual(a.Match.Match, c.Match.Match);
        Assert.Equal(1, a.Match.Self.Slot);
        Assert.Equal(2, b.Match.Self.Slot);
        Assert.Equal(1, c.Match.Self.Slot);
        Assert.Equal(2, d.Match.Self.Slot);
        Assert.Equal(2, runtimeFactory.CreatedCount);
    }

    [Fact]
    public async Task DuplicateJoinIsExplicitAndDoesNotCreateAnotherSession()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        var first = await matchmaking.JoinAsync(new JoinMatchmakingRequest("A", RequestId: "one"), TestContext.Current.CancellationToken);
        var duplicate = await matchmaking.JoinAsync(new JoinMatchmakingRequest("A", RequestId: "two"), TestContext.Current.CancellationToken);
        await matchmaking.JoinAsync(new JoinMatchmakingRequest("B"), TestContext.Current.CancellationToken);
        var duplicateAfterMatch = await matchmaking.JoinAsync(new JoinMatchmakingRequest("A", RequestId: "three"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.Waiting, first.Status);
        Assert.Equal(MatchmakingStatus.AlreadyWaiting, duplicate.Status);
        Assert.Equal(MatchmakingStatus.AlreadyMatched, duplicateAfterMatch.Status);
        Assert.Equal(1, runtimeFactory.CreatedCount);
    }

    [Fact]
    public async Task CancellationRemovesWaitingPlayer()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        await matchmaking.JoinAsync(new JoinMatchmakingRequest("A", RequestId: "request-a"), TestContext.Current.CancellationToken);
        var cancelled = await matchmaking.CancelAsync(new CancelMatchmakingRequest("A"), TestContext.Current.CancellationToken);
        var missing = await matchmaking.CancelAsync(new CancelMatchmakingRequest("A"), TestContext.Current.CancellationToken);
        var b = await matchmaking.JoinAsync(new JoinMatchmakingRequest("B"), TestContext.Current.CancellationToken);
        var c = await matchmaking.JoinAsync(new JoinMatchmakingRequest("C"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.Cancelled, cancelled.Status);
        Assert.Equal(MatchmakingStatus.NotFound, missing.Status);
        Assert.Equal(MatchmakingStatus.Waiting, b.Status);
        Assert.Equal(MatchmakingStatus.Matched, c.Status);
        Assert.Equal("B", (await matchmaking.WaitForMatchAsync("B", TestContext.Current.CancellationToken)).PlayerKey);
    }

    [Fact]
    public async Task CancellationTokenCancelsWaitingRequest()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);
        using var cancellation = new CancellationTokenSource();

        await matchmaking.JoinAsync(
            new JoinMatchmakingRequest("A", RequestId: "request-a"),
            cancellation.Token);
        cancellation.Cancel();

        var result = await matchmaking.WaitForMatchAsync("A", TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.NotFound, result.Status);
        Assert.Equal(0, matchmaking.WaitingCount);
    }

    [Fact]
    public async Task IncompatiblePlayersRemainWaiting()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        var first = await matchmaking.JoinAsync(new JoinMatchmakingRequest("A", "red"), TestContext.Current.CancellationToken);
        var second = await matchmaking.JoinAsync(new JoinMatchmakingRequest("B", "blue"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.Waiting, first.Status);
        Assert.Equal(MatchmakingStatus.Waiting, second.Status);
        Assert.Equal(2, matchmaking.WaitingCount);
        Assert.Equal(0, runtimeFactory.CreatedCount);
    }

    [Fact]
    public async Task WaitingRegistryRejectsRequestsBeyondItsBoundedCapacity()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = new InMemoryMatchmaking(
            runtimeFactory,
            new SequentialIdentityGenerator(),
            options: new MatchmakingOptions { MaximumWaitingPlayers = 1 });

        var first = await matchmaking.JoinAsync(new JoinMatchmakingRequest("A"), TestContext.Current.CancellationToken);
        var second = await matchmaking.JoinAsync(new JoinMatchmakingRequest("B"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.Waiting, first.Status);
        Assert.Equal(MatchmakingStatus.Rejected, second.Status);
        Assert.Equal(1, matchmaking.WaitingCount);
    }

    [Fact]
    public void DefaultIdentityGeneratorCreatesDistinctServerOwnedIdentities()
    {
        var generator = new GuidMatchmakingIdentityGenerator();

        var first = generator.Create();
        var second = generator.Create();

        Assert.NotEqual(first.Match, second.Match);
        Assert.NotEqual(first.Session, second.Session);
        Assert.NotEqual(first.FirstPlayer, first.SecondPlayer);
        Assert.True(new MatchmakingResult(
            MatchmakingStatus.Matched,
            "player",
            "request",
            new MatchFound(
                first.Match,
                first.Session,
                new MatchmakingPlayerAssignment("player", first.FirstPlayer, 1),
                new MatchmakingPlayerAssignment("other", first.SecondPlayer, 2))).IsSuccess);
        Assert.False(new MatchmakingResult(
            MatchmakingStatus.Rejected,
            "player",
            "request").IsSuccess);
    }

    [Fact]
    public async Task InvalidRequestsAndDisposedRegistryAreRejectedExplicitly()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => matchmaking.JoinAsync(new JoinMatchmakingRequest(string.Empty), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => matchmaking.CancelAsync(new CancelMatchmakingRequest(string.Empty), TestContext.Current.CancellationToken));

        await matchmaking.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => matchmaking.JoinAsync(new JoinMatchmakingRequest("after-dispose"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => matchmaking.WaitForMatchAsync("after-dispose", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellingMatchedPlayerDoesNotDestroyEstablishedSession()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        await matchmaking.JoinAsync(new JoinMatchmakingRequest("A"), TestContext.Current.CancellationToken);
        await matchmaking.JoinAsync(new JoinMatchmakingRequest("B"), TestContext.Current.CancellationToken);

        var cancelled = await matchmaking.CancelAsync(new CancelMatchmakingRequest("A"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.AlreadyMatched, cancelled.Status);
        Assert.Equal(MatchmakingStatus.AlreadyMatched,
            (await matchmaking.JoinAsync(new JoinMatchmakingRequest("A"), TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task ConcurrentFourPlayerJoinCreatesTwoSessionsWithoutLoss()
    {
        var runtimeFactory = new FakeRuntimeFactory();
        await using var matchmaking = Create(runtimeFactory);

        var results = await Task.WhenAll(
            Task.Run(() => matchmaking.JoinAsync(new JoinMatchmakingRequest("A"))),
            Task.Run(() => matchmaking.JoinAsync(new JoinMatchmakingRequest("B"))),
            Task.Run(() => matchmaking.JoinAsync(new JoinMatchmakingRequest("C"))),
            Task.Run(() => matchmaking.JoinAsync(new JoinMatchmakingRequest("D"))));
        var completed = await Task.WhenAll(
            matchmaking.WaitForMatchAsync("A", TestContext.Current.CancellationToken),
            matchmaking.WaitForMatchAsync("B", TestContext.Current.CancellationToken),
            matchmaking.WaitForMatchAsync("C", TestContext.Current.CancellationToken),
            matchmaking.WaitForMatchAsync("D", TestContext.Current.CancellationToken));

        var matches = completed
            .Where(result => result.Match is not null)
            .Select(result => result.Match!.Match)
            .Distinct()
            .ToArray();

        Assert.Equal(2, matches.Length);
        Assert.Equal(4, completed.Count(result => result.Status == MatchmakingStatus.Matched));
        Assert.Equal(2, runtimeFactory.CreatedCount);
        Assert.Equal(4, completed.Select(result => result.PlayerKey).Distinct().Count());
    }

    [Fact]
    public async Task SessionCreationFailureRollsBackBothPlayers()
    {
        var runtimeFactory = new FakeRuntimeFactory { FailSubmission = true };
        await using var matchmaking = Create(runtimeFactory);

        await matchmaking.JoinAsync(new JoinMatchmakingRequest("A"), TestContext.Current.CancellationToken);
        var failed = await matchmaking.JoinAsync(new JoinMatchmakingRequest("B"), TestContext.Current.CancellationToken);
        runtimeFactory.FailSubmission = false;
        var retry = await matchmaking.JoinAsync(new JoinMatchmakingRequest("C"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.Rejected, failed.Status);
        Assert.Equal(MatchmakingStatus.Matched, retry.Status);
        Assert.Equal(1, matchmaking.WaitingCount);
        Assert.Equal(2, runtimeFactory.CreatedCount);
        Assert.Equal(1, runtimeFactory.DisposedCount);
    }

    [Fact]
    public async Task MatchmakingTransfersOwnershipToThePhaseThreeRuntime()
    {
        await using var registry = new SessionRuntimeRegistry();
        await using var matchmaking = Create(new SessionRuntimeFactory(registry));

        await matchmaking.JoinAsync(new JoinMatchmakingRequest("A"), TestContext.Current.CancellationToken);
        var matched = await matchmaking.JoinAsync(new JoinMatchmakingRequest("B"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchmakingStatus.Matched, matched.Status);
        Assert.Equal(1, registry.Count);
        var runtime = registry.Get(matched.Match!.Session);
        Assert.Equal(2, runtime.Session.Players.Count);
        Assert.Equal(2, runtime.Session.Players[matched.Match.Self.Player].Number);
    }

    private static InMemoryMatchmaking Create(FakeRuntimeFactory runtimeFactory) =>
        new(runtimeFactory, new SequentialIdentityGenerator());

    private static InMemoryMatchmaking Create(IMatchSessionRuntimeFactory runtimeFactory) =>
        new(runtimeFactory, new SequentialIdentityGenerator());

    private sealed class SequentialIdentityGenerator : IServerMatchmakingIdentityGenerator
    {
        private int _sequence;

        public MatchmakingIdentitySet Create()
        {
            var number = Interlocked.Increment(ref _sequence);
            return new(
                new MatchIdentity($"match-{number}"),
                new SessionIdentity($"session-{number}"),
                new PlayerIdentity($"server-player-{number}-1"),
                new PlayerIdentity($"server-player-{number}-2"));
        }
    }

    private sealed class FakeRuntimeFactory : IMatchSessionRuntimeFactory
    {
        public List<FakeRuntime> Runtimes { get; } = [];
        public bool FailSubmission { get; set; }
        public int CreatedCount => Runtimes.Count;
        public int DisposedCount => Runtimes.Count(runtime => runtime.IsDisposed);

        public Task<IMatchSessionRuntime> CreateAsync(
            SessionIdentity session,
            MatchIdentity match,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = new FakeRuntime(FailSubmission);
            Runtimes.Add(runtime);
            return Task.FromResult<IMatchSessionRuntime>(runtime);
        }
    }

    private sealed class FakeRuntime(bool failSubmission) : IMatchSessionRuntime
    {
        public List<SessionCommand> JoinCommands { get; } = [];
        public bool IsDisposed { get; private set; }

        public Task<SessionCommandResult> SubmitAsync(
            SessionCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JoinCommands.Add(command);
            return Task.FromResult(failSubmission
                ? SessionCommandResult.Reject(
                    SessionRejectionCode.InvalidState,
                    "Fake runtime rejected the command.")
                : SessionCommandResult.Accept(Array.Empty<SessionEvent>()));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
