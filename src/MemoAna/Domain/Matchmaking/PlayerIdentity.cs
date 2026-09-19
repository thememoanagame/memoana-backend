namespace MemoAna.Domain.Matchmaking;

public readonly record struct PlayerIdentity
{
    public PlayerIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Player identity is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
