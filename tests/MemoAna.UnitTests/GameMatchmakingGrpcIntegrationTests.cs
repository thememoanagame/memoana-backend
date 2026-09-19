using Grpc.Core;
using Grpc.Net.Client;
using MemoAna.Application.Matchmaking;
using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;
using MemoAna.Proto.GameMatchmaking.V1;
using MemoAna.Presentation.Grpc;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Reflection;
using DomainSessionIdentity = MemoAna.Domain.Matchmaking.SessionIdentity;

using ProtoSessionIdentity = MemoAna.Proto.GameMatchmaking.V1.SessionIdentity;
using DomainLeaveReason = MemoAna.Domain.Sessions.LeaveReason;
using ProtoLeaveReason = MemoAna.Proto.GameMatchmaking.V1.LeaveReason;
namespace MemoAna.UnitTests;

public sealed class GameMatchmakingGrpcIntegrationTests
{
    [Fact]
    public async Task TwoGeneratedClientsJoinAndReachRunningState()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var channel = GrpcChannel.ForAddress(
            factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var client = new GameMatchmakingService.GameMatchmakingServiceClient(channel);
        var cancellationToken = TestContext.Current.CancellationToken;
        using var first = client.Connect(cancellationToken: cancellationToken);
        using var second = client.Connect(cancellationToken: cancellationToken);

        await Task.WhenAll(
            first.RequestStream.WriteAsync(Command("join-a", 1, join: true), cancellationToken),
            second.RequestStream.WriteAsync(Command("join-b", 1, join: true), cancellationToken));

        var joins = await Task.WhenAll(
            ReadUntilAsync(first.ResponseStream, ServerEvent.EventOneofCase.JoinAccepted),
            ReadUntilAsync(second.ResponseStream, ServerEvent.EventOneofCase.JoinAccepted));
        var firstJoin = joins[0];
        var secondJoin = joins[1];
        var firstIdentity = firstJoin.Metadata.Identity;
        var secondIdentity = secondJoin.Metadata.Identity;

        await first.RequestStream.WriteAsync(
            Command("spoof", 2, secondIdentity, ready: true),
            cancellationToken);
        var spoof = await ReadUntilAsync(
            first.ResponseStream,
            ServerEvent.EventOneofCase.ProtocolError);
        Assert.Equal(ProtocolErrorCode.MissingIdentity, spoof.ProtocolError.Code);

        await Task.WhenAll(
            first.RequestStream.WriteAsync(Command("ready-a", 2, firstIdentity, ready: true), cancellationToken),
            second.RequestStream.WriteAsync(Command("ready-b", 2, secondIdentity, ready: true), cancellationToken));

        var started = await Task.WhenAll(
            ReadUntilAsync(first.ResponseStream, ServerEvent.EventOneofCase.GameStarted),
            ReadUntilAsync(second.ResponseStream, ServerEvent.EventOneofCase.GameStarted));
        var firstStarted = started[0];
        var secondStarted = started[1];

        Assert.NotEmpty(firstStarted.GameStarted.FirstPlayerId);
        Assert.Equal(
            firstStarted.GameStarted.FirstPlayerId,
            secondStarted.GameStarted.FirstPlayerId);

        var activeCall = firstStarted.GameStarted.FirstPlayerId == firstIdentity.PlayerId
            ? first
            : second;
        var activeIdentity = firstStarted.GameStarted.FirstPlayerId == firstIdentity.PlayerId
            ? firstIdentity
            : secondIdentity;
        await activeCall.RequestStream.WriteAsync(
            new ClientCommand
            {
                Metadata = new CommandMetadata
                {
                    CommandId = "flip-1",
                    ClientSequence = 3,
                    Identity = activeIdentity
                },
                Gameplay = new GameplayIntent
                {
                    FlipCard = new FlipCardIntent { Position = 1 }
                }
            },
            cancellationToken);
        await ReadUntilAsync(first.ResponseStream, ServerEvent.EventOneofCase.GameplayUpdate);
        await ReadUntilAsync(second.ResponseStream, ServerEvent.EventOneofCase.GameplayUpdate);

        await activeCall.RequestStream.WriteAsync(
            new ClientCommand
            {
                Metadata = new CommandMetadata
                {
                    CommandId = "flip-1",
                    ClientSequence = 3,
                    Identity = activeIdentity
                },
                Gameplay = new GameplayIntent
                {
                    FlipCard = new FlipCardIntent { Position = 1 }
                }
            },
            cancellationToken);
        var duplicate = await ReadUntilAsync(
            activeCall.ResponseStream,
            ServerEvent.EventOneofCase.CommandRejected);
        Assert.Equal(RejectionCode.Duplicate, duplicate.CommandRejected.Code);

        await activeCall.RequestStream.WriteAsync(
            new ClientCommand
            {
                Metadata = new CommandMetadata
                {
                    CommandId = "flip-2",
                    ClientSequence = 5,
                    Identity = activeIdentity
                },
                Gameplay = new GameplayIntent
                {
                    FlipCard = new FlipCardIntent { Position = 2 }
                }
            },
            cancellationToken);
        var outOfOrder = await ReadUntilAsync(
            activeCall.ResponseStream,
            ServerEvent.EventOneofCase.CommandRejected);
        Assert.Equal(RejectionCode.OutOfOrder, outOfOrder.CommandRejected.Code);

        await activeCall.RequestStream.WriteAsync(
            Command("leave-1", 4, activeIdentity, leave: true),
            cancellationToken);
        var finished = await ReadUntilAsync(
            activeCall.ResponseStream,
            ServerEvent.EventOneofCase.SessionFinished);
        Assert.Equal(SessionState.Aborted, finished.SessionFinished.TerminalState);
    }

    [Fact]
    public async Task MalformedCommandProducesProtocolErrorWithoutCreatingMatch()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var channel = GrpcChannel.ForAddress(
            factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var client = new GameMatchmakingService.GameMatchmakingServiceClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);

        await call.RequestStream.WriteAsync(
            new ClientCommand(),
            TestContext.Current.CancellationToken);
        var response = await ReadUntilAsync(
            call.ResponseStream,
            ServerEvent.EventOneofCase.ProtocolError);

        Assert.Equal(ProtocolErrorCode.MalformedMessage, response.ProtocolError.Code);

        await call.RequestStream.WriteAsync(
            new ClientCommand
            {
                Metadata = new CommandMetadata { CommandId = "bad-gameplay", ClientSequence = 1 },
                Gameplay = new GameplayIntent
                {
                    FlipCard = new FlipCardIntent { Position = 99 }
                }
            },
            TestContext.Current.CancellationToken);
        var invalidGameplay = await ReadUntilAsync(
            call.ResponseStream,
            ServerEvent.EventOneofCase.ProtocolError);
        Assert.Equal(ProtocolErrorCode.MalformedMessage, invalidGameplay.ProtocolError.Code);
    }

    [Fact]
    public void TransportAdapterCoversValidationAndMappingBranches()
    {
        var validMetadata = new CommandMetadata
        {
            CommandId = "cmd-1",
            ClientSequence = 1,
            Identity = new ProtoSessionIdentity { MatchId = "match-1", SessionId = "session-1", PlayerId = "player-1" }
        };

        Assert.True(InvokeTryValidate(new ClientCommand { Metadata = validMetadata, Ready = new ReadyUp { Ready = true } }, out _));

        Assert.False(InvokeTryValidate(new ClientCommand { Join = new JoinSession() }, out var missingMetadataError));
        Assert.Equal(ProtocolErrorCode.MalformedMessage, missingMetadataError!.ProtocolError.Code);

        Assert.False(InvokeTryValidate(new ClientCommand
        {
            Metadata = new CommandMetadata { CommandId = string.Empty, ClientSequence = 1 },
            Join = new JoinSession()
        }, out var emptyCommandIdError));
        Assert.Equal(ProtocolErrorCode.MalformedMessage, emptyCommandIdError!.ProtocolError.Code);

        Assert.False(InvokeTryValidate(new ClientCommand
        {
            Metadata = new CommandMetadata
            {
                CommandId = "bad-identity",
                ClientSequence = 1,
                Identity = new ProtoSessionIdentity { MatchId = new string('m', 257), SessionId = "session-1", PlayerId = "player-1" }
            },
            Gameplay = new GameplayIntent { FlipCard = new FlipCardIntent { Position = 0 } }
        }, out var oversizedIdentityError));
        Assert.Equal(ProtocolErrorCode.MalformedMessage, oversizedIdentityError!.ProtocolError.Code);

        Assert.False(InvokeTryValidate(new ClientCommand
        {
            Metadata = new CommandMetadata { CommandId = "bad-gameplay", ClientSequence = 1 },
            Gameplay = new GameplayIntent { FlipCard = new FlipCardIntent { Position = MatchSession.BoardSize } }
        }, out var invalidGameplayError));
        Assert.Equal(ProtocolErrorCode.MalformedMessage, invalidGameplayError!.ProtocolError.Code);

        var match = new MatchFound(
            new MatchIdentity("match-1"),
            new DomainSessionIdentity("session-1"),
            new MatchmakingPlayerAssignment("player-a", new PlayerIdentity("player-a"), 1),
            new MatchmakingPlayerAssignment("player-b", new PlayerIdentity("player-b"), 2));

        var readyCommand = new ClientCommand
        {
            Metadata = new CommandMetadata { CommandId = "ready-1", ClientSequence = 2, Identity = new MemoAna.Proto.GameMatchmaking.V1.SessionIdentity { MatchId = "match-1", SessionId = "session-1", PlayerId = "player-a" } },
            Ready = new ReadyUp { Ready = true }
        };
        Assert.IsType<ReadyUpCommand>(InvokeToDomainCommand(readyCommand, match));

        var leaveCommand = new ClientCommand
        {
            Metadata = new CommandMetadata { CommandId = "leave-1", ClientSequence = 3, Identity = new MemoAna.Proto.GameMatchmaking.V1.SessionIdentity { MatchId = "match-1", SessionId = "session-1", PlayerId = "player-a" } },
            Leave = new LeaveSession { Reason = ProtoLeaveReason.Voluntary }
        };
        Assert.IsType<LeaveSessionCommand>(InvokeToDomainCommand(leaveCommand, match));

        var flipCommand = new ClientCommand
        {
            Metadata = new CommandMetadata { CommandId = "flip-1", ClientSequence = 4, Identity = new MemoAna.Proto.GameMatchmaking.V1.SessionIdentity { MatchId = "match-1", SessionId = "session-1", PlayerId = "player-a" } },
            Gameplay = new GameplayIntent { FlipCard = new FlipCardIntent { Position = 0 } }
        };
        Assert.IsType<FlipCardCommand>(InvokeToDomainCommand(flipCommand, match));
        Assert.Null(InvokeToDomainCommand(new ClientCommand { Metadata = new CommandMetadata { CommandId = "unsupported", ClientSequence = 5 } }, match));

        Assert.NotNull(InvokeToServerEvent(new PlayerJoinedEvent(1, 1, new PlayerIdentity("player-a"), 1, 2), match).JoinAccepted);
        Assert.NotNull(InvokeToServerEvent(new PlayerReadyChangedEvent(2, 2, new PlayerIdentity("player-a"), true), match).ReadyStateChanged);
        Assert.NotNull(InvokeToServerEvent(new SessionStateChangedEvent(3, 3, SessionLifecycle.Ready), match).SessionStateChanged);
        Assert.NotNull(InvokeToServerEvent(new GameStartedEvent(4, 4, new PlayerIdentity("player-a")), match).GameStarted);
        Assert.NotNull(InvokeToServerEvent(new CardFlipAcceptedEvent(5, 5, new PlayerIdentity("player-a"), 0, 1), match).GameplayUpdate);
        Assert.NotNull(InvokeToServerEvent(new PlayerLeftEvent(6, 6, new PlayerIdentity("player-b"), DomainLeaveReason.Disconnected), match).PlayerLeft);
        Assert.NotNull(InvokeToServerEvent(new SessionFinishedEvent(7, 7, SessionLifecycle.Completed, MemoAna.Domain.Sessions.MatchResult.Draw, new PlayerIdentity("player-a")), match).SessionFinished);
        var unsupported = Assert.Throws<TargetInvocationException>(() => InvokeToServerEvent(new UnsupportedSessionEvent(8, 8), match));
        Assert.IsType<InvalidOperationException>(unsupported.InnerException);
    }

    private static bool InvokeTryValidate(ClientCommand command, out ServerEvent? error)
    {
        var method = typeof(GameMatchmakingGrpcService).GetMethod("TryValidate", BindingFlags.Static | BindingFlags.NonPublic);
        var parameters = new object?[] { command, null };
        var result = (bool)method!.Invoke(null, parameters)!;
        error = (ServerEvent?)parameters[1];
        return result;
    }

    private static SessionCommand? InvokeToDomainCommand(ClientCommand command, MatchFound match)
    {
        var method = typeof(GameMatchmakingGrpcService).GetMethod("ToDomainCommand", BindingFlags.Static | BindingFlags.NonPublic);
        return (SessionCommand?)method!.Invoke(null, new object[] { command, match });
    }

    private static ServerEvent InvokeToServerEvent(SessionEvent @event, MatchFound match)
    {
        var method = typeof(GameMatchmakingGrpcService).GetMethod("ToServerEvent", BindingFlags.Static | BindingFlags.NonPublic);
        return (ServerEvent)method!.Invoke(null, new object[] { @event, match })!;
    }

    private sealed record UnsupportedSessionEvent(ulong Sequence, ulong StateVersion) : SessionEvent(Sequence, StateVersion);

    private static ClientCommand Command(
        string id,
        ulong sequence,
        MemoAna.Proto.GameMatchmaking.V1.SessionIdentity? identity = null,
        bool join = false,
        bool ready = false,
        bool leave = false)
    {
        var command = new ClientCommand
        {
            Metadata = new CommandMetadata
            {
                CommandId = id,
                ClientSequence = sequence,
                Identity = identity
            }
        };
        if (join)
        {
            command.Join = new JoinSession();
        }
        else if (ready)
        {
            command.Ready = new ReadyUp { Ready = true };
        }
        else if (leave)
        {
            command.Leave = new LeaveSession { Reason = ProtoLeaveReason.Voluntary };
        }

        return command;
    }

    private static async Task<ServerEvent> ReadUntilAsync(
        IAsyncStreamReader<ServerEvent> stream,
        ServerEvent.EventOneofCase expected)
    {
        while (await stream.MoveNext(TestContext.Current.CancellationToken))
        {
            if (stream.Current.EventCase == expected)
            {
                return stream.Current;
            }
        }

        throw new InvalidOperationException($"Event '{expected}' was not received.");
    }
}
