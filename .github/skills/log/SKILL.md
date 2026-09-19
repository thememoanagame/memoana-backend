# Logging Skill

## Purpose

All implemented operational behavior must be instrumented with structured logging through `ILogger<T>`.

The goal is diagnosability without coupling domain logic to logging infrastructure.

## Core rules

- Use the generic Microsoft.Extensions.Logging abstraction: `ILogger<T>`.
- Inject `ILogger<T>` through constructors/primary constructors at the application, infrastructure and presentation boundaries that perform operational work.
- Do not use `Console.WriteLine`, `Debug.WriteLine`, static/global loggers, or ad-hoc logging wrappers unless an existing architectural abstraction explicitly requires one.
- Domain entities should remain free of `ILogger<T>`; instrument their callers/boundaries instead.
- Log structured values, not interpolated strings.
- Do not log secrets, credentials, authentication tokens, full protobuf payloads, or sensitive user data.
- Prefer stable identifiers such as match ID, session ID, player ID, command ID and event sequence where appropriate.

## Logging levels

Use all `ILogger<T>` levels where their semantics apply:

- **Trace** — extremely detailed lifecycle/diagnostic information useful only during deep troubleshooting.
- **Debug** — normal diagnostic details needed to understand control flow, command handling, queueing, mapping and state transitions during development/diagnosis.
- **Information** — meaningful operational milestones such as connection established, matchmaking completed, session started/completed, graceful disconnect and service startup.
- **Warning** — recoverable abnormal conditions such as invalid client input, rejected commands, duplicate/out-of-order requests, slow consumers, unexpected disconnects or degraded fallback behavior.
- **Error** — an operation failed unexpectedly but the process/service can continue; include the exception and stable correlation identifiers.
- **Critical** — failures that threaten the service/process or make an essential subsystem unavailable.

"Use all levels" means the implementation must deliberately classify events across these levels where applicable; do not emit every level for every method or duplicate the same event at multiple levels.

## Structured logging

Prefer:

```csharp
logger.LogInformation(
    "Matchmaking completed for {MatchId} and {SessionId}.",
    matchId,
    sessionId);
```

Avoid:

```csharp
logger.LogInformation($"Matchmaking completed for {matchId}.");
```

For hot paths, use `LoggerMessage` source generation when practical, especially for high-frequency gameplay operations.

## Lifecycle coverage

Instrument, where applicable:
- service/host startup and shutdown;
- gRPC connection and stream lifecycle;
- matchmaking requests, matches and cancellations;
- session creation, command submission, rejection and completion;
- disconnect/cancellation;
- queue saturation/backpressure;
- unexpected exceptions;
- resource disposal and cleanup.

Every newly implemented operational path should have enough logging to reconstruct its lifecycle from logs.

## Exceptions

- Log exceptions at the boundary where they are handled or converted.
- Do not log and rethrow the same exception repeatedly at every layer.
- Normal domain/application rejections are not exceptional failures; log them at an appropriate diagnostic/warning level without an exception object.
- Include correlation identifiers whenever available.

## Localization

Log messages are user/operator-facing strings and must follow the repository's `l10n` skill. Do not hard-code new natural-language log messages when a localized resource key can be used.

## Testing

Logging must not change behavior, ordering, cancellation or domain state.

Tests should not depend on exact localized log text unless the behavior under test is specifically localization.
