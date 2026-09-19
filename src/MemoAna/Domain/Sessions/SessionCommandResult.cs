namespace MemoAna.Domain.Sessions;

/// <summary>
/// Represents the authoritative outcome of processing one session command.
/// </summary>
/// <remarks>
/// Rejected commands are represented as data so transport adapters can return protocol-level
/// rejection events instead of converting normal domain validation into RPC failures.
/// </remarks>
public sealed class SessionCommandResult
{
    private SessionCommandResult(
        bool accepted,
        SessionRejectionCode? rejectionCode,
        string? reason,
        IReadOnlyList<SessionEvent> events)
    {
        Accepted = accepted;
        RejectionCode = rejectionCode;
        Reason = reason;
        Events = events;
    }

    /// <summary>
    /// Gets a value indicating whether the command changed or was accepted by the session.
    /// </summary>
    public bool Accepted { get; }

    /// <summary>
    /// Gets the rejection code when the command was rejected.
    /// </summary>
    public SessionRejectionCode? RejectionCode { get; }

    /// <summary>
    /// Gets the human-readable rejection reason when the command was rejected.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets the authoritative events produced by the command.
    /// </summary>
    public IReadOnlyList<SessionEvent> Events { get; }

    /// <summary>
    /// Creates an accepted command result containing the authoritative events produced by processing.
    /// </summary>
    /// <param name="events">The events produced by the command.</param>
    /// <returns>An accepted command result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="events"/> is <see langword="null"/>.</exception>
    public static SessionCommandResult Accept(IReadOnlyList<SessionEvent> events) =>
        new(true, null, null, events ?? throw new ArgumentNullException(nameof(events)));

    /// <summary>
    /// Creates a rejected command result.
    /// </summary>
    /// <param name="code">The reason category for the rejection.</param>
    /// <param name="reason">The caller-facing explanation of the rejection.</param>
    /// <returns>A rejected command result containing no events.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reason"/> is <see langword="null"/>.</exception>
    public static SessionCommandResult Reject(SessionRejectionCode code, string reason) =>
        new(false, code, reason ?? throw new ArgumentNullException(nameof(reason)), Array.Empty<SessionEvent>());
}
