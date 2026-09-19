using MemoAna.Application.Matchmaking;
using MemoAna.Domain.Matchmaking;

namespace MemoAna.Infrastructure.Sessions;

/// <summary>Creates runtime handles backed by the session runtime registry.</summary>
public sealed class SessionRuntimeFactory(SessionRuntimeRegistry registry) : IMatchSessionRuntimeFactory
{
    /// <summary>Creates a new authoritative runtime for the specified session.</summary>
    /// <param name="session">The session identity.</param>
    /// <param name="match">The match identity.</param>
    /// <param name="cancellationToken">The token used to cancel creation.</param>
    /// <returns>A runtime handle exposing command and event operations.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    public Task<IMatchSessionRuntime> CreateAsync(SessionIdentity session, MatchIdentity match, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMatchSessionRuntime>(new SessionRuntimeHandle(registry.Create(session, match)));
    }

    private sealed class SessionRuntimeHandle(SessionRuntime runtime) : IMatchSessionRuntime, IMatchSessionEventSource
    {
        public Task<MemoAna.Domain.Sessions.SessionCommandResult> SubmitAsync(MemoAna.Domain.Sessions.SessionCommand command, CancellationToken cancellationToken = default) =>
            runtime.SubmitAsync(command, cancellationToken);
        public ValueTask DisposeAsync() => runtime.DisposeAsync();
        public IAsyncEnumerable<MemoAna.Domain.Sessions.SessionEvent> Subscribe(CancellationToken cancellationToken = default) =>
            runtime.Subscribe(cancellationToken);
    }
}
