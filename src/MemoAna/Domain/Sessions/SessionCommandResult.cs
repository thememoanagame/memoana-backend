namespace MemoAna.Domain.Sessions;

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

    public bool Accepted { get; }
    public SessionRejectionCode? RejectionCode { get; }
    public string? Reason { get; }
    public IReadOnlyList<SessionEvent> Events { get; }

    public static SessionCommandResult Accept(IReadOnlyList<SessionEvent> events) =>
        new(true, null, null, events);

    public static SessionCommandResult Reject(SessionRejectionCode code, string reason) =>
        new(false, code, reason, Array.Empty<SessionEvent>());
}
