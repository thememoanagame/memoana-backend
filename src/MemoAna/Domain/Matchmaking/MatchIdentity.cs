namespace MemoAna.Domain.Matchmaking;

public readonly record struct MatchIdentity
{
    public MatchIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Match identity is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
