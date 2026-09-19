using MemoAna.Application.Matchmaking;
using MemoAna.Domain.Matchmaking;

namespace MemoAna.Infrastructure.Sessions;

public sealed class SessionRuntimeFactory(SessionRuntimeRegistry registry) : IMatchSessionRuntimeFactory
{
    public Task<IMatchSessionRuntime> CreateAsync(
        SessionIdentity session,
        MatchIdentity match,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMatchSessionRuntime>(
            new SessionRuntimeHandle(registry.Create(session, match)));
    }

    private sealed class SessionRuntimeHandle(SessionRuntime runtime) :
        IMatchSessionRuntime,
        IMatchSessionEventSource
    {
        public Task<MemoAna.Domain.Sessions.SessionCommandResult> SubmitAsync(
            MemoAna.Domain.Sessions.SessionCommand command,
            CancellationToken cancellationToken = default) =>
            runtime.SubmitAsync(command, cancellationToken);

        public ValueTask DisposeAsync() => runtime.DisposeAsync();

        public IAsyncEnumerable<MemoAna.Domain.Sessions.SessionEvent> Subscribe(
            CancellationToken cancellationToken = default) =>
            runtime.Subscribe(cancellationToken);
    }
}
