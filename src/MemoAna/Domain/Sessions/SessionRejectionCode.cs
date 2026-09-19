namespace MemoAna.Domain.Sessions;

/// <summary>
/// Identifies why the authoritative session rejected a command.
/// </summary>
public enum SessionRejectionCode
{
    /// <summary>The command was malformed or unsupported.</summary>
    InvalidCommand,
    /// <summary>The command is incompatible with the current lifecycle state.</summary>
    InvalidState,
    /// <summary>The sender is not authorized for the target session/player.</summary>
    NotAuthorized,
    /// <summary>The client sequence does not follow the expected order.</summary>
    OutOfOrder,
    /// <summary>The command was already processed.</summary>
    Duplicate,
    /// <summary>The requested gameplay or lifecycle action is illegal.</summary>
    IllegalMove
}
