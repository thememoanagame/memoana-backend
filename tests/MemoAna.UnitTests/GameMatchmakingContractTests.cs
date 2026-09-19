using Google.Protobuf;
using MemoAna.Proto.GameMatchmaking.V1;

namespace MemoAna.UnitTests;

public sealed class GameMatchmakingContractTests
{
    [Fact]
    public void ServiceExposesBidirectionalConnectStream()
    {
        var method = GameMatchmakingService.Descriptor.Methods.Single();

        Assert.Equal("Connect", method.Name);
        Assert.True(method.IsClientStreaming);
        Assert.True(method.IsServerStreaming);
        Assert.Equal(typeof(ClientCommand), method.InputType.ClrType);
        Assert.Equal(typeof(ServerEvent), method.OutputType.ClrType);
    }

    [Fact]
    public void ClientCommandsUseDistinctOneofCases()
    {
        var commands = new[]
        {
            new ClientCommand { Join = new JoinSession() },
            new ClientCommand { Ready = new ReadyUp { Ready = true } },
            new ClientCommand { Leave = new LeaveSession { Reason = LeaveReason.Voluntary } },
            new ClientCommand
            {
                Gameplay = new GameplayIntent
                {
                    FlipCard = new FlipCardIntent { Position = 7 }
                }
            }
        };

        Assert.Equal(new[]
        {
            ClientCommand.CommandOneofCase.Join,
            ClientCommand.CommandOneofCase.Ready,
            ClientCommand.CommandOneofCase.Leave,
            ClientCommand.CommandOneofCase.Gameplay
        }, commands.Select(command => command.CommandCase));
        Assert.Equal(GameplayIntent.ActionOneofCase.FlipCard, commands[^1].Gameplay.ActionCase);
    }

    [Fact]
    public void ServerEventsRepresentLifecycleResultsAndRejections()
    {
        var events = new[]
        {
            new ServerEvent { SessionCreated = new SessionCreated() },
            new ServerEvent { JoinAccepted = new JoinAccepted() },
            new ServerEvent { SessionStateChanged = new SessionStateChanged { State = SessionState.Ready } },
            new ServerEvent { ReadyStateChanged = new ReadyStateChanged { PlayerId = "p1", Ready = true } },
            new ServerEvent { GameplayUpdate = new GameplayUpdate { CardFlipAccepted = new CardFlipAccepted { Position = 7 } } },
            new ServerEvent { CommandRejected = new CommandRejected { Code = RejectionCode.IllegalMove } },
            new ServerEvent { PlayerLeft = new PlayerLeft { PlayerId = "p2" } },
            new ServerEvent
            {
                SessionFinished = new SessionFinished
                {
                    TerminalState = SessionState.Completed,
                    Result = MatchResult.Win,
                    WinnerPlayerId = "p1"
                }
            }
        };

        Assert.Equal(new[]
        {
            ServerEvent.EventOneofCase.SessionCreated,
            ServerEvent.EventOneofCase.JoinAccepted,
            ServerEvent.EventOneofCase.SessionStateChanged,
            ServerEvent.EventOneofCase.ReadyStateChanged,
            ServerEvent.EventOneofCase.GameplayUpdate,
            ServerEvent.EventOneofCase.CommandRejected,
            ServerEvent.EventOneofCase.PlayerLeft,
            ServerEvent.EventOneofCase.SessionFinished
        }, events.Select(serverEvent => serverEvent.EventCase));
    }

    [Fact]
    public void CommandAndEventMetadataPreserveIdentityAndOrdering()
    {
        var command = new ClientCommand
        {
            Metadata = new CommandMetadata
            {
                CommandId = "command-1",
                ClientSequence = 12,
                Identity = new SessionIdentity
                {
                    MatchId = "match-1",
                    SessionId = "session-1",
                    PlayerId = "player-1"
                }
            },
            Ready = new ReadyUp { Ready = true }
        };
        var serverEvent = new ServerEvent
        {
            Metadata = new ServerEventMetadata
            {
                ServerEventSequence = 42,
                StateVersion = 9,
                CorrelatedCommandId = command.Metadata.CommandId,
                Identity = command.Metadata.Identity
            },
            CommandAccepted = new CommandAccepted { CommandId = command.Metadata.CommandId }
        };

        Assert.Equal("match-1", serverEvent.Metadata.Identity.MatchId);
        Assert.Equal("session-1", serverEvent.Metadata.Identity.SessionId);
        Assert.Equal("player-1", serverEvent.Metadata.Identity.PlayerId);
        Assert.Equal((ulong)12, command.Metadata.ClientSequence);
        Assert.Equal((ulong)42, serverEvent.Metadata.ServerEventSequence);
        Assert.Equal((ulong)9, serverEvent.Metadata.StateVersion);
        Assert.Equal(command.Metadata.CommandId, serverEvent.Metadata.CorrelatedCommandId);
    }

    [Fact]
    public void ContractRoundTripsThroughProtobufSerialization()
    {
        var original = new ServerEvent
        {
            Metadata = new ServerEventMetadata
            {
                ServerEventSequence = 3,
                StateVersion = 2,
                Identity = new SessionIdentity
                {
                    MatchId = "match-1",
                    SessionId = "session-1",
                    PlayerId = "player-1"
                }
            },
            SessionFinished = new SessionFinished
            {
                TerminalState = SessionState.Aborted,
                Result = MatchResult.NoContest,
                WinnerPlayerId = string.Empty
            }
        };

        var roundTripped = ServerEvent.Parser.ParseFrom(original.ToByteArray());

        Assert.Equal(original, roundTripped);
        Assert.Equal(ServerEvent.EventOneofCase.SessionFinished, roundTripped.EventCase);
        Assert.Equal(SessionState.Aborted, roundTripped.SessionFinished.TerminalState);
    }

    [Fact]
    public void LifecycleEnumsStartWithUnspecifiedAndContainTerminalStates()
    {
        Assert.Equal(0, (int)SessionState.Unspecified);
        Assert.Equal(0, (int)LeaveReason.Unspecified);
        Assert.Equal(0, (int)RejectionCode.Unspecified);
        Assert.Equal(0, (int)ProtocolErrorCode.Unspecified);
        Assert.Equal(0, (int)MatchResult.Unspecified);
        Assert.Contains(SessionState.Completed, Enum.GetValues<SessionState>());
        Assert.Contains(SessionState.Aborted, Enum.GetValues<SessionState>());
        Assert.Contains(SessionState.Expired, Enum.GetValues<SessionState>());
    }
}
