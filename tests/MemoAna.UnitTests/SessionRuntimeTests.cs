using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;
using MemoAna.Infrastructure.Sessions;

namespace MemoAna.UnitTests;

public sealed class SessionRuntimeTests
{
    private static readonly SessionIdentity Session = new("session-runtime");
    private static readonly MatchIdentity Match = new("match-runtime");
    private static readonly PlayerIdentity PlayerOne = new("player-1");
    private static readonly PlayerIdentity PlayerTwo = new("player-2");

    [Fact]
    public async Task RegistryCreatesAndFindsOneRuntimePerSession()
    {
        await using var registry = new SessionRuntimeRegistry();

        var runtime = registry.Create(Session, Match);

        Assert.True(registry.TryGet(Session, out var found));
        Assert.Same(runtime, found);
        Assert.Same(runtime, registry.Get(Session));
        Assert.Equal(1, registry.Count);
        Assert.Throws<InvalidOperationException>(() => registry.Create(Session, Match));
    }

    [Fact]
    public async Task RegistryRejectsLookupAndCreationAfterDisposal()
    {
        await using var registry = new SessionRuntimeRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Get(Session));
        await registry.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => registry.Create(Session, Match));
    }

    [Fact]
    public void RuntimeOptionsRequirePositiveQueueCapacities()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionRuntimeRegistry(new SessionRuntimeOptions { CommandCapacity = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionRuntimeRegistry(new SessionRuntimeOptions { EventCapacity = 0 }));
    }

    [Fact]
    public async Task SubmittedCommandsAreProcessedAndPublishedInOrder()
    {
        await using var registry = new SessionRuntimeRegistry();
        var runtime = registry.Create(Session, Match);
        await using var events = runtime.Subscribe().GetAsyncEnumerator();

        var first = await runtime.SubmitAsync(new JoinSessionCommand("join-1", PlayerOne));
        var second = await runtime.SubmitAsync(new JoinSessionCommand("join-2", PlayerTwo));

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.True(await events.MoveNextAsync());
        Assert.True(await events.MoveNextAsync());
        Assert.Equal(2UL, events.Current.Sequence);
        Assert.Equal(2UL, runtime.Session.LastEventSequence);
    }

    [Fact]
    public async Task ConcurrentCommandsUseOneAuthoritativeMutationPipeline()
    {
        await using var registry = new SessionRuntimeRegistry();
        var runtime = registry.Create(Session, Match);
        await runtime.SubmitAsync(new JoinSessionCommand("join-1", PlayerOne));

        var submissions = await Task.WhenAll(
            Task.Run(() => runtime.SubmitAsync(new JoinSessionCommand("join-2", PlayerTwo))),
            Task.Run(() => runtime.SubmitAsync(new JoinSessionCommand("join-3", new("player-3")))),
            Task.Run(() => runtime.SubmitAsync(new JoinSessionCommand("join-4", new("player-4")))));

        Assert.Equal(1, submissions.Count(result => result.Accepted));
        Assert.Equal(2, submissions.Count(result => result.RejectionCode == SessionRejectionCode.IllegalMove));
        Assert.Equal(2, runtime.Session.Players.Count);
        Assert.Equal(runtime.Session.LastEventSequence, runtime.Session.StateVersion);
    }

    [Fact]
    public async Task BoundedCommandQueueAppliesBackpressureUntilProcessorCanRead()
    {
        await using var registry = new SessionRuntimeRegistry(new SessionRuntimeOptions
        {
            CommandCapacity = 1,
            EventCapacity = 1
        });
        var runtime = registry.Create(Session, Match);

        var first = runtime.SubmitAsync(new JoinSessionCommand("join-1", PlayerOne));
        var second = runtime.SubmitAsync(new JoinSessionCommand("join-2", PlayerTwo));

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.True(result.Accepted));
        Assert.Equal(2, runtime.Session.Players.Count);
    }

    [Fact]
    public async Task TerminalSessionIsRemovedAndRejectsFurtherSubmissions()
    {
        await using var registry = new SessionRuntimeRegistry();
        var runtime = registry.Create(Session, Match);

        var result = await runtime.SubmitAsync(new AbortSessionCommand("abort"));
        await runtime.Completion;

        Assert.True(result.Accepted);
        Assert.False(registry.TryGet(Session, out _));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => runtime.SubmitAsync(new StartGameCommand("after-abort")));
    }

    [Fact]
    public async Task CancellationCompletesProcessorAndRemovesSession()
    {
        using var cancellation = new CancellationTokenSource();
        await using var registry = new SessionRuntimeRegistry(cancellationToken: cancellation.Token);
        var runtime = registry.Create(Session, Match);

        cancellation.Cancel();
        await runtime.Completion;

        Assert.True(runtime.IsClosed);
        Assert.False(registry.TryGet(Session, out _));
    }

    [Fact]
    public async Task SubscriberCancellationCompletesOnlyThatSubscriber()
    {
        await using var registry = new SessionRuntimeRegistry();
        var runtime = registry.Create(Session, Match);
        using var cancellation = new CancellationTokenSource();
        await using var events = runtime.Subscribe(cancellation.Token).GetAsyncEnumerator();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await events.MoveNextAsync());
        var result = await runtime.SubmitAsync(new JoinSessionCommand("join-1", PlayerOne));

        Assert.True(result.Accepted);
        Assert.False(runtime.IsClosed);
    }

    [Fact]
    public async Task DisposingRegistryCompletesRuntimeAndRemovesAllSessions()
    {
        var registry = new SessionRuntimeRegistry();
        var runtime = registry.Create(Session, Match);

        await registry.DisposeAsync();
        await runtime.Completion;
        await registry.DisposeAsync();

        Assert.Equal(0, registry.Count);
        Assert.True(runtime.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => registry.Create(new SessionIdentity("after-dispose")));
    }

    [Fact]
    public async Task SlowSubscriberIsBoundedAndBackpressuresInsteadOfBufferingIndefinitely()
    {
        await using var registry = new SessionRuntimeRegistry(new SessionRuntimeOptions
        {
            CommandCapacity = 1,
            EventCapacity = 1
        });
        var runtime = registry.Create(Session, Match);
        await using var events = runtime.Subscribe().GetAsyncEnumerator();

        var first = await runtime.SubmitAsync(new JoinSessionCommand("join-1", PlayerOne));
        var second = runtime.SubmitAsync(new JoinSessionCommand("join-2", PlayerTwo));

        Assert.True(first.Accepted);
        Assert.False(second.IsCompleted);
        Assert.True(await events.MoveNextAsync());
        Assert.True((await second).Accepted);
    }
}
