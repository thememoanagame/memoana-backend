namespace MemoAna.Infrastructure.Sessions;

public sealed record SessionRuntimeOptions
{
    public int CommandCapacity { get; init; } = 128;
    public int EventCapacity { get; init; } = 128;

    internal void Validate()
    {
        if (CommandCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandCapacity), "Command capacity must be positive.");
        }

        if (EventCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EventCapacity), "Event capacity must be positive.");
        }
    }
}
