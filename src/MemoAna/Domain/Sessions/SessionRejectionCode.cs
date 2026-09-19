namespace MemoAna.Domain.Sessions;

public enum SessionRejectionCode
{
    InvalidCommand,
    InvalidState,
    NotAuthorized,
    OutOfOrder,
    Duplicate,
    IllegalMove
}
