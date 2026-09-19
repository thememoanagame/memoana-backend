using System.Collections.Concurrent;
using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;

namespace MemoAna.Infrastructure.Sessions;

/// <summary>Owns the process-local collection of active authoritative session runtimes.</summary>
/// <remarks>Runtime removal is callback-driven so terminal sessions do not remain registered after processing stops.</remarks>
public sealed class SessionRuntimeRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<SessionIdentity, SessionRuntime> _sessions = [];
    private readonly SessionRuntimeOptions _options;
    private readonly CancellationTokenSource _cancellation;
    private int _disposed;

    /// <summary>Initializes the registry.</summary>
    /// <param name="options">Optional runtime capacity configuration.</param>
    /// <param name="cancellationToken">Token that stops all runtimes owned by the registry.</param>
    public SessionRuntimeRegistry(SessionRuntimeOptions? options = null, CancellationToken cancellationToken = default)
    {
        _options = options ?? new SessionRuntimeOptions();
        _options.Validate();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    /// <summary>Gets the number of active runtimes.</summary>
    public int Count => _sessions.Count;

    /// <summary>Creates and registers an authoritative session runtime.</summary>
    /// <param name="sessionIdentity">The unique session identity.</param>
    /// <param name="matchIdentity">The optional match identity.</param>
    /// <returns>The registered runtime.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the registry has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the session identity is already registered.</exception>
    public SessionRuntime Create(SessionIdentity sessionIdentity, MatchIdentity? matchIdentity = null)
    {
        ThrowIfDisposed();
        var session = MatchSession.Create(sessionIdentity, matchIdentity);
        var runtime = new SessionRuntime(session, _options, Remove, _cancellation.Token);
        if (!_sessions.TryAdd(sessionIdentity, runtime))
        {
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException($"A runtime for session '{sessionIdentity}' is already registered.");
        }
        return runtime;
    }

    /// <summary>Attempts to retrieve an active runtime.</summary>
    /// <param name="sessionIdentity">The session identity.</param>
    /// <param name="runtime">The runtime when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a runtime exists.</returns>
    public bool TryGet(SessionIdentity sessionIdentity, out SessionRuntime? runtime) => _sessions.TryGetValue(sessionIdentity, out runtime);

    /// <summary>Retrieves an active runtime.</summary>
    /// <param name="sessionIdentity">The session identity.</param>
    /// <returns>The registered runtime.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no runtime is registered for the identity.</exception>
    public SessionRuntime Get(SessionIdentity sessionIdentity) =>
        _sessions.TryGetValue(sessionIdentity, out var runtime) ? runtime : throw new KeyNotFoundException($"Session '{sessionIdentity}' was not found.");

    /// <summary>Stops and disposes every active runtime.</summary>
    /// <returns>A value task that completes after all runtimes have stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancellation.Cancel();
        var runtimes = _sessions.Values.ToArray();
        await Task.WhenAll(runtimes.Select(runtime => runtime.DisposeAsync().AsTask())).ConfigureAwait(false);
        _sessions.Clear();
        _cancellation.Dispose();
    }

    private void Remove(SessionRuntime runtime) => _sessions.TryRemove(new KeyValuePair<SessionIdentity, SessionRuntime>(runtime.Session.Session, runtime));
    private void ThrowIfDisposed() { if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(SessionRuntimeRegistry)); }
}
