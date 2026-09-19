namespace MemoAna.Domain.Matchmaking;

/// <summary>
/// Identifies a server-authoritative match session.
/// </summary>
public readonly record struct SessionIdentity
{
    /// <summary>
    /// Initializes a session identity.
    /// </summary>
    /// <param name="value">The non-empty identity value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    public SessionIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Session identity is required.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the identity value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns the identity value.
    /// </summary>
    /// <returns>The identity value.</returns>
    public override string ToString() => Value;
}
