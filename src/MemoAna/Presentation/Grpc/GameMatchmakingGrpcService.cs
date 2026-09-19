using System.Threading.Channels;
using Grpc.Core;
using MemoAna.Application.Matchmaking;
using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;
using MemoAna.Proto.GameMatchmaking.V1;
using DomainLeaveReason = MemoAna.Domain.Sessions.LeaveReason;
using DomainMatchResult = MemoAna.Domain.Sessions.MatchResult;
using ProtoLeaveReason = MemoAna.Proto.GameMatchmaking.V1.LeaveReason;
using ProtoMatchResult = MemoAna.Proto.GameMatchmaking.V1.MatchResult;
using ProtoPlayerSlot = MemoAna.Proto.GameMatchmaking.V1.PlayerSlot;

namespace MemoAna.Presentation.Grpc;

public sealed class GameMatchmakingGrpcService(
    IMatchmakingSessionGateway matchmaking)
    : GameMatchmakingService.GameMatchmakingServiceBase
{
    private const int MaximumCommandIdLength = 128;
    private const int MaximumIdentityLength = 256;
    private const int MaximumReasonLength = 512;

    public override async Task Connect(
        IAsyncStreamReader<ClientCommand> requestStream,
        IServerStreamWriter<ServerEvent> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        var token = cancellation.Token;
        var output = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        var writer = WriteEventsAsync(output.Reader, responseStream, token);
        var backgroundTasks = new List<Task>();
        try
        {
            await ReadCommandsAsync(requestStream, output.Writer, token, backgroundTasks)
                .ConfigureAwait(false);
        }
        finally
        {
            cancellation.Cancel();
            output.Writer.TryComplete();
            try
            {
                await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
            }
            try
            {
                await writer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ReadCommandsAsync(
        IAsyncStreamReader<ClientCommand> requestStream,
        ChannelWriter<ServerEvent> output,
        CancellationToken cancellationToken,
        ICollection<Task> backgroundTasks)
    {
        string? playerKey = null;
        MatchFound? match = null;
        var bound = false;
        Task? sessionEvents = null;

        while (await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                var command = requestStream.Current;
                if (!TryValidate(command, out var protocolError))
                {
                    await output.WriteAsync(protocolError!, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (command.CommandCase == ClientCommand.CommandOneofCase.Join)
                {
                    if (bound || playerKey is not null)
                    {
                        await output.WriteAsync(ProtocolError(
                            ProtocolErrorCode.MalformedMessage,
                            "Only one JoinSession command is allowed per stream."), cancellationToken);
                        continue;
                    }

                    // The protocol identity is correlation data until the server binds
                    // the stream. It must never select an existing player.
                    playerKey = $"connection-{Guid.NewGuid():N}";

                    var joined = await matchmaking.JoinAsync(
                        new JoinMatchmakingRequest(
                            playerKey,
                            RequestId: command.Metadata.CommandId),
                        cancellationToken).ConfigureAwait(false);

                    if (joined.Status == MatchmakingStatus.Waiting)
                    {
                        joined = await matchmaking.WaitForMatchAsync(playerKey, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!joined.IsSuccess || joined.Match is null)
                    {
                        await output.WriteAsync(ProtocolError(
                            ProtocolErrorCode.MalformedMessage,
                            joined.Reason ?? "The matchmaking request was rejected."),
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    match = joined.Match;
                    bound = true;
                    await PublishMatchAcceptedAsync(output, match, cancellationToken)
                        .ConfigureAwait(false);
                    sessionEvents = PublishSessionEventsAsync(match, output, cancellationToken);
                    backgroundTasks.Add(sessionEvents);
                    continue;
                }

                if (!bound || match is null || !MatchesIdentity(command.Metadata.Identity, match))
                {
                    await output.WriteAsync(ProtocolError(
                        ProtocolErrorCode.MissingIdentity,
                        "The command identity is not bound to this stream."),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var runtime = GetRuntimeEvents(match, cancellationToken);
                if (runtime is null)
                {
                    await output.WriteAsync(ProtocolError(
                        ProtocolErrorCode.MalformedMessage,
                        "The session is no longer available."),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var applicationCommand = ToDomainCommand(command, match);
                if (applicationCommand is null)
                {
                    await output.WriteAsync(ProtocolError(
                        ProtocolErrorCode.MalformedMessage,
                        "The command payload is not supported."),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var result = await runtime.SubmitAsync(applicationCommand, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Accepted)
                {
                    await output.WriteAsync(ToRejected(result, match), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                await output.WriteAsync(ToAccepted(applicationCommand.CommandId, match), cancellationToken)
                    .ConfigureAwait(false);

                if (applicationCommand is ReadyUpCommand)
                {
                    var start = await runtime.SubmitAsync(
                        new StartGameCommand($"start-{applicationCommand.CommandId}"),
                        cancellationToken).ConfigureAwait(false);
                    if (!start.Accepted &&
                        start.RejectionCode is not SessionRejectionCode.InvalidState)
                    {
                        await output.WriteAsync(ToRejected(start, match), cancellationToken)
                            .ConfigureAwait(false);
                }
            }
        }
    }

    private IMatchSessionRuntime? GetRuntimeEvents(
        MatchFound match,
        CancellationToken cancellationToken)
    {
        return matchmaking.TryGetRuntime(match.Session, out var runtime)
            ? runtime
            : null;
    }

    private async Task PublishSessionEventsAsync(
        MatchFound match,
        ChannelWriter<ServerEvent> output,
        CancellationToken cancellationToken)
    {
        if (!matchmaking.TrySubscribe(match.Session, cancellationToken, out var events) ||
            events is null)
        {
            return;
        }

        await foreach (var @event in events.WithCancellation(cancellationToken))
        {
            await output.WriteAsync(ToServerEvent(@event, match), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteEventsAsync(
        ChannelReader<ServerEvent> input,
        IServerStreamWriter<ServerEvent> response,
        CancellationToken cancellationToken)
    {
        await foreach (var @event in input.ReadAllAsync(cancellationToken))
        {
            await response.WriteAsync(@event, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PublishMatchAcceptedAsync(
        ChannelWriter<ServerEvent> output,
        MatchFound match,
        CancellationToken cancellationToken)
    {
        await output.WriteAsync(new ServerEvent
        {
            Metadata = Metadata(match, 0, 0),
            SessionCreated = new SessionCreated()
        }, cancellationToken);

        await output.WriteAsync(new ServerEvent
        {
            Metadata = Metadata(match, (ulong)match.Self.Slot, (ulong)match.Self.Slot),
            JoinAccepted = new JoinAccepted
            {
                Player = new ProtoPlayerSlot
                {
                    PlayerId = match.Self.Player.Value,
                    Slot = (uint)match.Self.Slot
                },
                ConnectedPlayers = 2
            }
        }, cancellationToken);
    }

    private static SessionCommand? ToDomainCommand(ClientCommand command, MatchFound match) =>
        command.CommandCase switch
        {
            ClientCommand.CommandOneofCase.Ready => new ReadyUpCommand(
                command.Metadata.CommandId,
                match.Self.Player,
                command.Ready.Ready,
                command.Metadata.ClientSequence),
            ClientCommand.CommandOneofCase.Leave => new LeaveSessionCommand(
                command.Metadata.CommandId,
                match.Self.Player,
                ToDomainLeaveReason(command.Leave.Reason),
                command.Metadata.ClientSequence),
            ClientCommand.CommandOneofCase.Gameplay
                when command.Gameplay.ActionCase == GameplayIntent.ActionOneofCase.FlipCard =>
                new FlipCardCommand(
                    command.Metadata.CommandId,
                    match.Self.Player,
                    checked((int)command.Gameplay.FlipCard.Position),
                    command.Metadata.ClientSequence),
            _ => null
        };

    private static ServerEvent ToServerEvent(SessionEvent @event, MatchFound match)
    {
        var metadata = Metadata(match, @event.Sequence, @event.StateVersion);
        return @event switch
        {
            PlayerJoinedEvent joined => new ServerEvent
            {
                Metadata = metadata,
                JoinAccepted = new JoinAccepted
                {
                    Player = new ProtoPlayerSlot
                    {
                        PlayerId = joined.Player.Value,
                        Slot = (uint)joined.Slot
                    },
                    ConnectedPlayers = (uint)joined.ConnectedPlayers
                }
            },
            PlayerReadyChangedEvent ready => new ServerEvent
            {
                Metadata = metadata,
                ReadyStateChanged = new ReadyStateChanged
                {
                    PlayerId = ready.Player.Value,
                    Ready = ready.IsReady
                }
            },
            SessionStateChangedEvent state => new ServerEvent
            {
                Metadata = metadata,
                SessionStateChanged = new SessionStateChanged
                {
                    State = ToProtoState(state.State)
                }
            },
            GameStartedEvent started => new ServerEvent
            {
                Metadata = metadata,
                GameStarted = new GameStarted
                {
                    FirstPlayerId = started.FirstPlayer.Value
                }
            },
            CardFlipAcceptedEvent flip => new ServerEvent
            {
                Metadata = metadata,
                GameplayUpdate = new GameplayUpdate
                {
                    CardFlipAccepted = new CardFlipAccepted { Position = (uint)flip.Position }
                }
            },
            PlayerLeftEvent left => new ServerEvent
            {
                Metadata = metadata,
                PlayerLeft = new PlayerLeft
                {
                    PlayerId = left.Player.Value,
                    Reason = ToProtoLeaveReason(left.Reason)
                }
            },
            SessionFinishedEvent finished => new ServerEvent
            {
                Metadata = metadata,
                SessionFinished = new SessionFinished
                {
                    TerminalState = ToProtoState(finished.State),
                    Result = ToProtoResult(finished.Result),
                    WinnerPlayerId = finished.Winner?.Value ?? string.Empty
                }
            },
            _ => throw new InvalidOperationException(
                $"Unsupported session event '{@event.GetType().Name}'.")
        };
    }

    private static ServerEvent ToRejected(SessionCommandResult result, MatchFound match) =>
        new()
        {
            Metadata = Metadata(match, 0, 0),
            CommandRejected = new CommandRejected
            {
                Code = ToProtoRejection(result.RejectionCode),
                Reason = result.Reason ?? string.Empty
            }
        };

    private static ServerEvent ToAccepted(string commandId, MatchFound match) =>
        new()
        {
            Metadata = new ServerEventMetadata
            {
                Identity = Metadata(match, 0, 0).Identity,
                CorrelatedCommandId = commandId
            },
            CommandAccepted = new CommandAccepted { CommandId = commandId }
        };

    private static ServerEvent ProtocolError(ProtocolErrorCode code, string message) =>
        new() { ProtocolError = new ProtocolError { Code = code, Message = message } };

    private static bool TryValidate(ClientCommand command, out ServerEvent? error)
    {
        error = null;
        var metadata = command.Metadata;
        if (metadata is null ||
            string.IsNullOrWhiteSpace(metadata.CommandId) ||
            metadata.CommandId.Length > MaximumCommandIdLength ||
            command.CommandCase == ClientCommand.CommandOneofCase.None)
        {
            error = ProtocolError(
                ProtocolErrorCode.MalformedMessage,
                "Metadata and one command payload are required.");
            return false;
        }

        if (metadata.Identity is not null &&
            (metadata.Identity.MatchId.Length > MaximumIdentityLength ||
             metadata.Identity.SessionId.Length > MaximumIdentityLength ||
             metadata.Identity.PlayerId.Length > MaximumIdentityLength))
        {
            error = ProtocolError(
                ProtocolErrorCode.MalformedMessage,
                "Identity fields exceed the supported length.");
            return false;
        }

        if (command.CommandCase == ClientCommand.CommandOneofCase.Gameplay &&
            (command.Gameplay.ActionCase != GameplayIntent.ActionOneofCase.FlipCard ||
             command.Gameplay.FlipCard.Position >= MatchSession.BoardSize))
        {
            error = ProtocolError(
                ProtocolErrorCode.MalformedMessage,
                "The gameplay payload is invalid.");
            return false;
        }

        return true;
    }

    private static bool MatchesIdentity(
        MemoAna.Proto.GameMatchmaking.V1.SessionIdentity? identity,
        MatchFound match) =>
        identity is not null &&
        identity.MatchId == match.Match.Value &&
        identity.SessionId == match.Session.Value &&
        identity.PlayerId == match.Self.Player.Value;

    private static ServerEventMetadata Metadata(
        MatchFound match,
        ulong sequence,
        ulong stateVersion) =>
        new()
        {
            ServerEventSequence = sequence,
            StateVersion = stateVersion,
            Identity = new Proto.GameMatchmaking.V1.SessionIdentity
            {
                MatchId = match.Match.Value,
                SessionId = match.Session.Value,
                PlayerId = match.Self.Player.Value
            }
        };

    private static SessionState ToProtoState(SessionLifecycle state) =>
        (SessionState)((int)state + 1);

    private static MemoAna.Proto.GameMatchmaking.V1.MatchResult ToProtoResult(
        DomainMatchResult result) =>
        (ProtoMatchResult)((int)result + 1);

    private static RejectionCode ToProtoRejection(SessionRejectionCode? code) =>
        code switch
        {
            SessionRejectionCode.InvalidCommand => RejectionCode.InvalidCommand,
            SessionRejectionCode.InvalidState => RejectionCode.InvalidState,
            SessionRejectionCode.NotAuthorized => RejectionCode.NotAuthorized,
            SessionRejectionCode.OutOfOrder => RejectionCode.OutOfOrder,
            SessionRejectionCode.Duplicate => RejectionCode.Duplicate,
            SessionRejectionCode.IllegalMove => RejectionCode.IllegalMove,
            _ => RejectionCode.Unspecified
        };

    private static DomainLeaveReason ToDomainLeaveReason(ProtoLeaveReason reason) =>
        (DomainLeaveReason)((int)reason - 1);

    private static ProtoLeaveReason ToProtoLeaveReason(DomainLeaveReason reason) =>
        (ProtoLeaveReason)((int)reason + 1);
}
