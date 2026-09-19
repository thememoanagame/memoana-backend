namespace MemoAna.Infrastructure.Sessions;

/// <summary>Configures bounded capacities used by a session runtime.</summary>
public sealed record SessionRuntimeOptions
{
    /// <summary>Gets the maximum number of commands that may wait in the runtime queue.</summary>
    public int CommandCapacity { get; init; } = 128;
    /// <summary>Gets the maximum number of events buffered for a subscriber.</summary>
    public int EventCapacity { get; init; } = 128;

    internal void Validate()
    {
        if (CommandCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(CommandCapacity), "Command capacity must be positive.");
        if (EventCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(EventCapacity), "Event capacity must be positive.");
    }
}
