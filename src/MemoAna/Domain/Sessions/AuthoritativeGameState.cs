using MemoAna.Domain.Matchmaking;

namespace MemoAna.Domain.Sessions;

/// <summary>
/// Holds the server-authoritative mutable game state for a match.
/// </summary>
/// <remarks>
/// The state is owned by <see cref="MatchSession"/> and is mutated only while a
/// session command is being processed by the authoritative session runtime.
/// </remarks>
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

    /// <summary>
    /// Gets the board positions that have already been flipped.
    /// </summary>
    public IReadOnlySet<int> FlippedPositions => _flippedPositions;

    /// <summary>
    /// Gets the authoritative score for each player.
    /// </summary>
    public IReadOnlyDictionary<PlayerIdentity, int> Scores => _scores;

    /// <summary>
    /// Gets or sets the player who owns the current turn.
    /// </summary>
    public PlayerIdentity? CurrentTurn { get; internal set; }

    /// <summary>
    /// Gets or sets the authoritative match result.
    /// </summary>
    public MatchResult Result { get; internal set; } = MatchResult.Unresolved;

    /// <summary>
    /// Gets or sets the authoritative winner, when the result has a unique winner.
    /// </summary>
    public PlayerIdentity? Winner { get; internal set; }

    internal int RegisterFlip(PlayerIdentity player, int position)
    {
        _flippedPositions.Add(position);
        _scores[player]++;
        return _scores[player];
    }
}
