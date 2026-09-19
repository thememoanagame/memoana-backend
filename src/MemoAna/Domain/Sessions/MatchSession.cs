using MemoAna.Domain.Matchmaking;

namespace MemoAna.Domain.Sessions;

public sealed class MatchSession
{
    public const int MaximumPlayers = 2;
    public const int BoardSize = 8;

    private readonly Dictionary<PlayerIdentity, PlayerSlot> _players = [];
    private readonly Dictionary<PlayerIdentity, ulong> _lastClientSequences = [];
    private readonly HashSet<string> _processedCommandIds = [];

    private MatchSession(SessionIdentity session, MatchIdentity? match)
    {
        Session = session;
        Match = match;
        State = SessionLifecycle.WaitingForPlayers;
        Game = new AuthoritativeGameState([]);
    }

    public SessionIdentity Session { get; }
    public MatchIdentity? Match { get; }
    public SessionLifecycle State { get; private set; }
    public ulong StateVersion { get; private set; }
    public ulong LastEventSequence { get; private set; }
    public IReadOnlyDictionary<PlayerIdentity, PlayerSlot> Players => _players;
    public AuthoritativeGameState Game { get; private set; }

    public static MatchSession Create(SessionIdentity session, MatchIdentity? match = null) =>
        new(session, match);

    public SessionCommandResult Handle(SessionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.CommandId))
        {
            return Reject(SessionRejectionCode.InvalidCommand, "Command id is required.");
        }

        if (!_processedCommandIds.Add(command.CommandId))
        {
            return Reject(SessionRejectionCode.Duplicate, "Command has already been processed.");
        }

        var validation = ValidateOrdering(command);
        if (validation is not null)
        {
            _processedCommandIds.Remove(command.CommandId);
            return validation;
        }

        var result = command switch
        {
            JoinSessionCommand join => Join(join),
            ReadyUpCommand ready => Ready(ready),
            StartGameCommand start => Start(start),
            LeaveSessionCommand leave => Leave(leave),
            FlipCardCommand flip => Flip(flip),
            AbortSessionCommand abort => End(abort, SessionLifecycle.Aborted, MatchResult.NoContest, null),
            ExpireSessionCommand expire => End(expire, SessionLifecycle.Expired, MatchResult.NoContest, null),
            _ => Reject(SessionRejectionCode.InvalidCommand, "Unsupported command.")
        };

        if (!result.Accepted)
        {
            _processedCommandIds.Remove(command.CommandId);
        }

        return result;
    }

    private SessionCommandResult? ValidateOrdering(SessionCommand command)
    {
        if (command.IsSystemCommand)
        {
            return null;
        }

        if (command.Player is null || command.ClientSequence == 0)
        {
            return Reject(SessionRejectionCode.InvalidCommand, "Player and client sequence are required.");
        }

        if (command is not JoinSessionCommand && !_players.ContainsKey(command.Player.Value))
        {
            return Reject(SessionRejectionCode.NotAuthorized, "Player does not belong to this session.");
        }

        if (_lastClientSequences.TryGetValue(command.Player.Value, out var last)
            && command.ClientSequence != last + 1)
        {
            return Reject(
                command.ClientSequence <= last
                    ? SessionRejectionCode.Duplicate
                    : SessionRejectionCode.OutOfOrder,
                "Client sequence is not the next expected sequence.");
        }

        return null;
    }

    private SessionCommandResult Join(JoinSessionCommand command)
    {
        if (IsTerminal())
        {
            return Reject(SessionRejectionCode.InvalidState, "Terminal sessions cannot accept players.");
        }

        if (_players.ContainsKey(command.JoiningPlayer))
        {
            return Reject(SessionRejectionCode.Duplicate, "Player already belongs to this session.");
        }

        if (_players.Count == MaximumPlayers)
        {
            return Reject(SessionRejectionCode.IllegalMove, "Session has no available player slot.");
        }

        var slotNumber = _players.Count + 1;
        _players.Add(command.JoiningPlayer, new PlayerSlot(command.JoiningPlayer, slotNumber, false));
        _lastClientSequences[command.JoiningPlayer] = command.ClientSequence;
        Game = new AuthoritativeGameState(_players.Keys);

        return Mutated(
            new PlayerJoinedEvent(NextEvent(), NextVersion(), command.JoiningPlayer, slotNumber, _players.Count));
    }

    private SessionCommandResult Ready(ReadyUpCommand command)
    {
        if (State != SessionLifecycle.WaitingForPlayers && State != SessionLifecycle.Ready)
        {
            return Reject(SessionRejectionCode.InvalidState, "Players can only change readiness before the game starts.");
        }

        if (_players.Count != MaximumPlayers)
        {
            return Reject(SessionRejectionCode.InvalidState, "Both player slots must be occupied.");
        }

        UpdatePlayer(command.PlayerId, command.Ready);
        _lastClientSequences[command.PlayerId] = command.ClientSequence;
        var events = new List<SessionEvent>
        {
            new PlayerReadyChangedEvent(NextEvent(), NextVersion(), command.PlayerId, command.Ready)
        };

        if (_players.Values.All(player => player.IsReady))
        {
            State = SessionLifecycle.Ready;
            events.Add(new SessionStateChangedEvent(NextEvent(), NextVersion(), State));
        }
        else if (State == SessionLifecycle.Ready)
        {
            State = SessionLifecycle.WaitingForPlayers;
            events.Add(new SessionStateChangedEvent(NextEvent(), NextVersion(), State));
        }

        return SessionCommandResult.Accept(events);
    }

    private SessionCommandResult Start(StartGameCommand command)
    {
        if (State != SessionLifecycle.Ready)
        {
            return Reject(SessionRejectionCode.InvalidState, "Only a ready session can start.");
        }

        State = SessionLifecycle.Running;
        var firstPlayer = _players.Values.OrderBy(player => player.Number).First().Player;
        Game.CurrentTurn = firstPlayer;
        return Mutated(
            new SessionStateChangedEvent(NextEvent(), NextVersion(), State),
            new GameStartedEvent(NextEvent(), NextVersion(), firstPlayer));
    }

    private SessionCommandResult Leave(LeaveSessionCommand command)
    {
        if (IsTerminal())
        {
            return Reject(SessionRejectionCode.InvalidState, "Terminal sessions cannot be changed.");
        }

        _lastClientSequences[command.PlayerId] = command.ClientSequence;
        _players.Remove(command.PlayerId);
        State = SessionLifecycle.Aborted;
        Game.CurrentTurn = null;
        Game.Result = MatchResult.NoContest;
        return Mutated(
            new PlayerLeftEvent(NextEvent(), NextVersion(), command.PlayerId, command.Reason),
            new SessionStateChangedEvent(NextEvent(), NextVersion(), State),
            new SessionFinishedEvent(NextEvent(), NextVersion(), State, Game.Result, null));
    }

    private SessionCommandResult Flip(FlipCardCommand command)
    {
        if (State != SessionLifecycle.Running)
        {
            return Reject(SessionRejectionCode.InvalidState, "Gameplay is only available while running.");
        }

        if (Game.CurrentTurn != command.PlayerId)
        {
            return Reject(SessionRejectionCode.IllegalMove, "Player does not own the current turn.");
        }

        if (command.Position < 0 || command.Position >= BoardSize || Game.FlippedPositions.Contains(command.Position))
        {
            return Reject(SessionRejectionCode.IllegalMove, "Card position is not a legal unflipped position.");
        }

        _lastClientSequences[command.PlayerId] = command.ClientSequence;
        var score = Game.RegisterFlip(command.PlayerId, command.Position);
        Game.CurrentTurn = OtherPlayer(command.PlayerId);
        var events = new List<SessionEvent>
        {
            new CardFlipAcceptedEvent(NextEvent(), NextVersion(), command.PlayerId, command.Position, score)
        };

        if (Game.FlippedPositions.Count == BoardSize)
        {
            State = SessionLifecycle.Completed;
            Game.Result = DetermineResult();
            Game.Winner = Game.Result == MatchResult.Win
                ? Game.Scores.MaxBy(scoreEntry => scoreEntry.Value).Key
                : null;
            Game.CurrentTurn = null;
            events.Add(new SessionStateChangedEvent(NextEvent(), NextVersion(), State));
            events.Add(new SessionFinishedEvent(NextEvent(), NextVersion(), State, Game.Result, Game.Winner));
        }

        return SessionCommandResult.Accept(events);
    }

    private SessionCommandResult End(
        SessionCommand command,
        SessionLifecycle terminalState,
        MatchResult result,
        PlayerIdentity? winner)
    {
        if (IsTerminal())
        {
            return Reject(SessionRejectionCode.InvalidState, "Terminal sessions are immutable.");
        }

        State = terminalState;
        Game.Result = result;
        Game.Winner = winner;
        Game.CurrentTurn = null;
        return Mutated(
            new SessionStateChangedEvent(NextEvent(), NextVersion(), State),
            new SessionFinishedEvent(NextEvent(), NextVersion(), State, result, winner));
    }

    private PlayerIdentity OtherPlayer(PlayerIdentity player) =>
        _players.Keys.Single(candidate => candidate != player);

    private MatchResult DetermineResult()
    {
        var scores = Game.Scores.Values.Distinct().ToArray();
        return scores.Length == 1 ? MatchResult.Draw : MatchResult.Win;
    }

    private void UpdatePlayer(PlayerIdentity player, bool ready)
    {
        var current = _players[player];
        _players[player] = current with { IsReady = ready };
    }

    private SessionCommandResult Mutated(params SessionEvent[] events)
    {
        foreach (var player in events.SelectMany(_ => _players.Keys).Distinct())
        {
            if (!_lastClientSequences.ContainsKey(player))
            {
                _lastClientSequences[player] = 0;
            }
        }

        return SessionCommandResult.Accept(events);
    }

    private SessionCommandResult Reject(SessionRejectionCode code, string reason) =>
        SessionCommandResult.Reject(code, reason);

    private ulong NextEvent() => ++LastEventSequence;
    private ulong NextVersion() => ++StateVersion;
    private bool IsTerminal() => State.IsTerminal();
}
