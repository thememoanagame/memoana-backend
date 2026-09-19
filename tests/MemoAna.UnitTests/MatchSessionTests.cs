using MemoAna.Domain.Matchmaking;
using MemoAna.Domain.Sessions;

namespace MemoAna.UnitTests;

public sealed class MatchSessionTests
{
    private static readonly SessionIdentity Session = new("session-1");
    private static readonly MatchIdentity Match = new("match-1");
    private static readonly PlayerIdentity PlayerOne = new("player-1");
    private static readonly PlayerIdentity PlayerTwo = new("player-2");

    [Fact]
    public void NewSessionStartsWaitingWithEmptyAuthoritativeState()
    {
        var session = MatchSession.Create(Session, Match);

        Assert.Equal(SessionLifecycle.WaitingForPlayers, session.State);
        Assert.Empty(session.Players);
        Assert.Equal(0UL, session.StateVersion);
        Assert.Equal(0UL, session.LastEventSequence);
        Assert.Equal(MatchResult.Unresolved, session.Game.Result);
    }

    [Fact]
    public void JoinAssignsStableSlotsAndRejectsThirdPlayer()
    {
        var session = CreateWithPlayers();

        var third = session.Handle(new JoinSessionCommand("join-3", new("player-3"), 1));

        Assert.False(third.Accepted);
        Assert.Equal(SessionRejectionCode.IllegalMove, third.RejectionCode);
        Assert.Equal(1, session.Players[PlayerOne].Number);
        Assert.Equal(2, session.Players[PlayerTwo].Number);
        Assert.Equal(2, session.Players.Count);
    }

    [Fact]
    public void DuplicatePlayerAndDuplicateCommandDoNotMutateState()
    {
        var session = MatchSession.Create(Session);
        var command = new JoinSessionCommand("join-1", PlayerOne, 1);

        Assert.True(session.Handle(command).Accepted);
        var version = session.StateVersion;
        var sequence = session.LastEventSequence;

        var duplicateCommand = session.Handle(command);
        var duplicatePlayer = session.Handle(new JoinSessionCommand("join-2", PlayerOne, 2));

        Assert.False(duplicateCommand.Accepted);
        Assert.Equal(SessionRejectionCode.Duplicate, duplicateCommand.RejectionCode);
        Assert.False(duplicatePlayer.Accepted);
        Assert.Equal(SessionRejectionCode.Duplicate, duplicatePlayer.RejectionCode);
        Assert.Equal(version, session.StateVersion);
        Assert.Equal(sequence, session.LastEventSequence);
    }

    [Fact]
    public void OutOfOrderCommandIsRejectedWithoutMutation()
    {
        var session = MatchSession.Create(Session);
        Assert.True(session.Handle(new JoinSessionCommand("join-1", PlayerOne, 1)).Accepted);
        var version = session.StateVersion;

        var result = session.Handle(new ReadyUpCommand("ready-3", PlayerOne, true, 3));

        Assert.False(result.Accepted);
        Assert.Equal(SessionRejectionCode.OutOfOrder, result.RejectionCode);
        Assert.Equal(version, session.StateVersion);
    }

    [Fact]
    public void BothPlayersReadyThenSystemStartMovesSessionToRunning()
    {
        var session = CreateWithPlayers();

        Assert.True(session.Handle(new ReadyUpCommand("ready-1", PlayerOne, true, 2)).Accepted);
        var ready = session.Handle(new ReadyUpCommand("ready-2", PlayerTwo, true, 2));
        Assert.True(ready.Accepted);
        Assert.Equal(SessionLifecycle.Ready, session.State);

        var start = session.Handle(new StartGameCommand("start-1"));

        Assert.True(start.Accepted);
        Assert.Equal(SessionLifecycle.Running, session.State);
        Assert.Equal(PlayerOne, session.Game.CurrentTurn);
        Assert.Contains(start.Events, @event => @event is GameStartedEvent);
    }

    [Fact]
    public void ReadyCanBeWithdrawnBeforeStart()
    {
        var session = ReadySession();

        var result = session.Handle(new ReadyUpCommand("unready-1", PlayerOne, false, 3));

        Assert.True(result.Accepted);
        Assert.Equal(SessionLifecycle.WaitingForPlayers, session.State);
        Assert.False(session.Players[PlayerOne].IsReady);
    }

    [Fact]
    public void StartBeforeReadyIsRejected()
    {
        var session = CreateWithPlayers();

        var result = session.Handle(new StartGameCommand("start-1"));

        Assert.False(result.Accepted);
        Assert.Equal(SessionRejectionCode.InvalidState, result.RejectionCode);
        Assert.Equal(SessionLifecycle.WaitingForPlayers, session.State);
    }

    [Fact]
    public void OnlyCurrentPlayerMayFlipAnUnflippedCard()
    {
        var session = RunningSession();
        var version = session.StateVersion;

        var unauthorized = session.Handle(new FlipCardCommand("flip-2", PlayerTwo, 0, 3));
        Assert.Equal(version, session.StateVersion);
        var accepted = session.Handle(new FlipCardCommand("flip-1", PlayerOne, 0, 3));

        Assert.False(unauthorized.Accepted);
        Assert.Equal(SessionRejectionCode.IllegalMove, unauthorized.RejectionCode);
        Assert.True(accepted.Accepted);
        Assert.Contains(0, session.Game.FlippedPositions);
        Assert.Equal(PlayerTwo, session.Game.CurrentTurn);
        Assert.Equal(1, session.Game.Scores[PlayerOne]);
        Assert.Equal(version + 1, session.StateVersion);
    }

    [Fact]
    public void InvalidAndRepeatedPositionsAreRejected()
    {
        var session = RunningSession();
        Assert.True(session.Handle(new FlipCardCommand("flip-1", PlayerOne, 0, 3)).Accepted);
        var version = session.StateVersion;

        var repeated = session.Handle(new FlipCardCommand("flip-2", PlayerTwo, 0, 3));
        var invalid = session.Handle(new FlipCardCommand("flip-3", PlayerTwo, MatchSession.BoardSize, 3));

        Assert.False(repeated.Accepted);
        Assert.False(invalid.Accepted);
        Assert.Equal(SessionRejectionCode.IllegalMove, repeated.RejectionCode);
        Assert.Equal(SessionRejectionCode.IllegalMove, invalid.RejectionCode);
        Assert.Equal(version, session.StateVersion);
    }

    [Fact]
    public void EveryAcceptedEventHasIncreasingServerSequence()
    {
        var session = RunningSession();
        var result = session.Handle(new FlipCardCommand("flip-1", PlayerOne, 1, 3));

        Assert.True(result.Accepted);
        Assert.Equal(
            result.Events.Select(@event => @event.Sequence).OrderBy(sequence => sequence),
            result.Events.Select(@event => @event.Sequence));
        Assert.Equal(session.LastEventSequence, result.Events[^1].Sequence);
        Assert.True(session.StateVersion > 0);
    }

    [Fact]
    public void AllCardsFlippedCompletesSessionWithServerDerivedResult()
    {
        var session = RunningSession();

        var nextSequence = new Dictionary<PlayerIdentity, ulong>
        {
            [PlayerOne] = 3,
            [PlayerTwo] = 3
        };

        for (var position = 0; position < MatchSession.BoardSize; position++)
        {
            var player = session.Game.CurrentTurn!.Value;
            var sequence = nextSequence[player]++;
            var result = session.Handle(new FlipCardCommand($"flip-{position}", player, position, sequence));
            Assert.True(result.Accepted);
        }

        Assert.Equal(SessionLifecycle.Completed, session.State);
        Assert.Equal(MatchResult.Draw, session.Game.Result);
        Assert.Null(session.Game.CurrentTurn);
        Assert.Equal(
            SessionRejectionCode.InvalidState,
            session.Handle(new StartGameCommand("start-after-complete")).RejectionCode);
    }

    [Fact]
    public void LeaveAbortsSessionAndTerminalStateCannotBeChanged()
    {
        var session = RunningSession();
        var left = session.Handle(new LeaveSessionCommand("leave-1", PlayerOne, LeaveReason.Disconnected, 3));
        var version = session.StateVersion;
        var later = session.Handle(new FlipCardCommand("flip-after-leave", PlayerTwo, 1, 3));

        Assert.True(left.Accepted);
        Assert.Equal(SessionLifecycle.Aborted, session.State);
        Assert.Equal(MatchResult.NoContest, session.Game.Result);
        Assert.False(later.Accepted);
        Assert.Equal(SessionRejectionCode.InvalidState, later.RejectionCode);
        Assert.Equal(version, session.StateVersion);
    }

    [Fact]
    public void AbortAndExpireAreExplicitTerminalTransitions()
    {
        var aborted = CreateWithPlayers();
        var expired = CreateWithPlayers();

        Assert.True(aborted.Handle(new AbortSessionCommand("abort")).Accepted);
        Assert.True(expired.Handle(new ExpireSessionCommand("expire")).Accepted);
        Assert.Equal(SessionLifecycle.Aborted, aborted.State);
        Assert.Equal(SessionLifecycle.Expired, expired.State);
        Assert.False(aborted.Handle(new AbortSessionCommand("abort-again")).Accepted);
        Assert.False(expired.Handle(new ExpireSessionCommand("expire-again")).Accepted);
    }

    [Fact]
    public void UnknownPlayerAndMalformedCommandsAreRejected()
    {
        var session = CreateWithPlayers();

        var unknown = session.Handle(new FlipCardCommand("flip-unknown", new("outsider"), 1, 1));
        var malformed = session.Handle(new JoinSessionCommand(string.Empty, new("new-player"), 1));

        Assert.Equal(SessionRejectionCode.NotAuthorized, unknown.RejectionCode);
        Assert.Equal(SessionRejectionCode.InvalidCommand, malformed.RejectionCode);
        Assert.Equal(2, session.Players.Count);
    }

    private static MatchSession CreateWithPlayers()
    {
        var session = MatchSession.Create(Session, Match);
        Assert.True(session.Handle(new JoinSessionCommand("join-1", PlayerOne, 1)).Accepted);
        Assert.True(session.Handle(new JoinSessionCommand("join-2", PlayerTwo, 1)).Accepted);
        return session;
    }

    private static MatchSession ReadySession()
    {
        var session = CreateWithPlayers();
        Assert.True(session.Handle(new ReadyUpCommand("ready-1", PlayerOne, true, 2)).Accepted);
        Assert.True(session.Handle(new ReadyUpCommand("ready-2", PlayerTwo, true, 2)).Accepted);
        return session;
    }

    private static MatchSession RunningSession()
    {
        var session = ReadySession();
        Assert.True(session.Handle(new StartGameCommand("start-1")).Accepted);
        return session;
    }
}
