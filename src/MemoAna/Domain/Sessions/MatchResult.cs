namespace MemoAna.Domain.Sessions;

/// <summary>
/// Describes the authoritative outcome of a match.
/// </summary>
public enum MatchResult
{
    /// <summary>The match has no terminal result yet.</summary>
    Unresolved,
    /// <summary>One player won the match.</summary>
    Win,
    /// <summary>The match ended without a unique winner.</summary>
    Draw,
    /// <summary>The match ended without a competitive result.</summary>
    NoContest
}
