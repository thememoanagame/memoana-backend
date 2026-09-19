using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MemoAna.Domain.Sessions;

namespace MemoAna.Infrastructure.Sessions;

/// <summary>Serializes commands for one authoritative session and publishes its resulting events.</summary>\n/// <remarks>The bounded command queue guarantees a single reader, making <see cref="MatchSession"/> mutation deterministic.</remarks>\npublic sealed class SessionRuntime : IAsyncDisposable
{
    private readonly Channel<PendingCommand> _commands;
    private readonly CancellationTokenSource _lifecycleCancellation;
    private readonly SessionRuntimeOptions _options;
    private readonly Action<SessionRuntime> _closed;
    private readonly object _subscriberGate = new();
    private readonly Dictionary<long, Channel<SessionEvent>> _subscribers = [];
    private readonly Task _processor;
    private long _nextSubscriberId;
    private int _shutdownStarted;

    internal SessionRuntime(
        MatchSession session,
        SessionRuntimeOptions options,
        Action<SessionRuntime> closed,
        CancellationToken runtimeCancellation)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _closed = closed ?? throw new ArgumentNullException(nameof(closed));

        _commands = Channel.CreateBounded<PendingCommand>(new BoundedChannelOptions(_options.CommandCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation);
        _processor = ProcessCommandsAsync();
    }

    /// <summary>Gets the authoritative session owned by this runtime.</summary>\n    public MatchSession Session { get; }
    /// <summary>Gets the task representing the runtime processor lifetime.</summary>\n    public Task Completion => _processor;
    /// <summary>Gets whether shutdown has started.</summary>\n    public bool IsClosed => Volatile.Read(ref _shutdownStarted) != 0;

    /// <summary>Queues a command and asynchronously waits for its authoritative result.</summary>\n    /// <param name="command">The command to process.</param>\n    /// <param name="cancellationToken">The token used to cancel queueing or waiting.</param>\n    /// <returns>The authoritative command result.</returns>\n    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>\n    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>\n    /// <exception cref="ObjectDisposedException">Thrown when the runtime is closed.</exception>\n    public async Task<SessionCommandResult> SubmitAsync(
        SessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfClosed();

        var pending = new PendingCommand(command);
        try
        {
            await _commands.Writer.WriteAsync(pending, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException) when (IsClosed)
        {
            throw new ObjectDisposedException(nameof(SessionRuntime));
        }

        return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Subscribes to future authoritative events.</summary>\n    /// <param name="cancellationToken">The token used to stop enumeration.</param>\n    /// <returns>An asynchronous sequence of session events.</returns>\n    public IAsyncEnumerable<SessionEvent> Subscribe(
        CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(_options.EventCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

        var subscriber = AddSubscriber(channel);
        return ReadEventsAsync(subscriber, cancellationToken);
    }

    /// <summary>Stops command processing, completes subscribers, and waits for processor termination.</summary>\n    /// <returns>A value task that completes when shutdown finishes.</returns>\n    public ValueTask DisposeAsync()
    {
        BeginShutdown();
        return DisposeAndWaitAsync();
    }

    private async ValueTask DisposeAndWaitAsync()
    {
        await Completion.ConfigureAwait(false);
        _lifecycleCancellation.Dispose();
    }

    private async IAsyncEnumerable<SessionEvent> ReadEventsAsync(
        Subscriber subscriber,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var @event in subscriber.Channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return @event;
            }
        }
        finally
        {
            RemoveSubscriber(subscriber);
        }
    }

    private Subscriber AddSubscriber(Channel<SessionEvent> channel)
    {
        lock (_subscriberGate)
        {
            if (IsClosed)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                var subscriber = new Subscriber(++_nextSubscriberId, channel);
                _subscribers.Add(subscriber.Id, channel);
                return subscriber;
            }
        }

        return new Subscriber(0, channel);
    }

    private void RemoveSubscriber(Subscriber subscriber)
    {
        lock (_subscriberGate)
        {
            if (subscriber.Id != 0)
            {
                _subscribers.Remove(subscriber.Id);
            }
        }

        subscriber.Channel.Writer.TryComplete();
    }

    /// <remarks>Commands are applied before their events are published to preserve authoritative ordering.</remarks>\n    private async Task ProcessCommandsAsync()
    {
        try
        {
            await foreach (var pending in _commands.Reader.ReadAllAsync(_lifecycleCancellation.Token))
            {
                try
                {
                    var result = Session.Handle(pending.Command);
                    await PublishAsync(result.Events).ConfigureAwait(false);
                    pending.Completion.TrySetResult(result);

                    if (result.Accepted && Session.State.IsTerminal())
                    {
                        BeginShutdown();
                    }
                }
                catch (Exception exception)
                {
                    pending.Completion.TrySetException(exception);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            CompletePendingCommands();
        }
        finally
        {
            CompletePendingCommands();
            CompleteSubscribers();
            Interlocked.Exchange(ref _shutdownStarted, 1);
            _closed(this);
        }
    }

    private async Task PublishAsync(IReadOnlyList<SessionEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        Channel<SessionEvent>[] subscribers;
        lock (_subscriberGate)
        {
            subscribers = _subscribers.Values.ToArray();
        }

        foreach (var @event in events)
        {
            foreach (var subscriber in subscribers)
            {
                try
                {
                    await subscriber.Writer.WriteAsync(@event, _lifecycleCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    RemoveSubscriber(new Subscriber(0, subscriber));
                }
            }
        }
    }

    private void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _commands.Writer.TryComplete();
        _lifecycleCancellation.Cancel();
    }

    private void CompletePendingCommands()
    {
        while (_commands.Reader.TryRead(out var pending))
        {
            pending.Completion.TrySetCanceled();
        }
    }

    private void CompleteSubscribers()
    {
        Channel<SessionEvent>[] subscribers;
        lock (_subscriberGate)
        {
            subscribers = _subscribers.Values.ToArray();
            _subscribers.Clear();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryComplete();
        }
    }

    private void ThrowIfClosed()
    {
        if (IsClosed)
        {
            throw new ObjectDisposedException(nameof(SessionRuntime));
        }
    }

    private sealed class PendingCommand(SessionCommand command)
    {
        public SessionCommand Command { get; } = command;
        public TaskCompletionSource<SessionCommandResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record Subscriber(long Id, Channel<SessionEvent> Channel);
}
