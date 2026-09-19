using MemoAna.Domain.Matchmaking;

namespace MemoAna.Domain.Sessions;

public sealed class AuthoritativeGameState
{
    private readonly HashSet<int> _flippedPositions = [];
    private readonly Dictionary<PlayerIdentity, int> _scores = [];

    internal AuthoritativeGameState(IEnumerable<PlayerIdentity> players)
    {
        foreach (var player in players)
        {
            _scores[player] = 0;
        }
    }

    public IReadOnlySet<int> FlippedPositions => _flippedPositions;
    public IReadOnlyDictionary<PlayerIdentity, int> Scores => _scores;
    public PlayerIdentity? CurrentTurn { get; internal set; }
    public MatchResult Result { get; internal set; } = MatchResult.Unresolved;
    public PlayerIdentity? Winner { get; internal set; }

    internal int RegisterFlip(PlayerIdentity player, int position)
    {
        _flippedPositions.Add(position);
        _scores[player]++;
        return _scores[player];
    }
}
