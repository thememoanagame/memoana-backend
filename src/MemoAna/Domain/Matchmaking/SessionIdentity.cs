namespace MemoAna.Domain.Matchmaking;

public readonly record struct SessionIdentity
{
    public SessionIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Session identity is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
