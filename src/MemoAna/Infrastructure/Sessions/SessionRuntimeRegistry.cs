using System.Collections.Concurrent;
using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;

namespace MemoAna.Infrastructure.Sessions;

public sealed class SessionRuntimeRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<SessionIdentity, SessionRuntime> _sessions = [];
    private readonly SessionRuntimeOptions _options;
    private readonly CancellationTokenSource _cancellation;
    private int _disposed;

    public SessionRuntimeRegistry(
        SessionRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _options = options ?? new SessionRuntimeOptions();
        _options.Validate();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public int Count => _sessions.Count;

    public SessionRuntime Create(
        SessionIdentity sessionIdentity,
        MatchIdentity? matchIdentity = null)
    {
        ThrowIfDisposed();

        var session = MatchSession.Create(sessionIdentity, matchIdentity);
        var runtime = new SessionRuntime(session, _options, Remove, _cancellation.Token);
        if (!_sessions.TryAdd(sessionIdentity, runtime))
        {
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException(
                $"A runtime for session '{sessionIdentity}' is already registered.");
        }

        return runtime;
    }

    public bool TryGet(SessionIdentity sessionIdentity, out SessionRuntime? runtime) =>
        _sessions.TryGetValue(sessionIdentity, out runtime);

    public SessionRuntime Get(SessionIdentity sessionIdentity) =>
        _sessions.TryGetValue(sessionIdentity, out var runtime)
            ? runtime
            : throw new KeyNotFoundException($"Session '{sessionIdentity}' was not found.");

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellation.Cancel();
        var runtimes = _sessions.Values.ToArray();
        await Task.WhenAll(runtimes.Select(runtime => runtime.DisposeAsync().AsTask()))
            .ConfigureAwait(false);
        _sessions.Clear();
        _cancellation.Dispose();
    }

    private void Remove(SessionRuntime runtime)
    {
        _sessions.TryRemove(new KeyValuePair<SessionIdentity, SessionRuntime>(
            runtime.Session.Session,
            runtime));
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SessionRuntimeRegistry));
        }
    }
}
