using Grpc.Core;
using Grpc.Net.Client;
using MemoAna.Proto.GameMatchmaking.V1;
using Microsoft.AspNetCore.Mvc.Testing;

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

    private static ClientCommand Command(
        string id,
        ulong sequence,
        SessionIdentity? identity = null,
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
            command.Leave = new LeaveSession { Reason = LeaveReason.Voluntary };
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
