
# Testing Skill

## Goal

Make networking and match-session behavior deterministic and regression-safe.

## Test layers

### Domain/application unit tests

No gRPC server, sockets or real time.

Cover:
- lifecycle transitions;
- legal/illegal commands;
- simultaneous commands;
- sequence/version handling;
- duplicate commands;
- terminal states;
- cancellation semantics;
- deterministic game outcomes.

### gRPC integration tests

Use the real ASP.NET Core gRPC endpoint and real generated client types.

Cover:
- bidi stream establishment;
- two clients joining one match;
- command/event exchange;
- malformed or invalid commands;
- cancellation/disconnect;
- server rejection;
- completion and final event.

### Contract tests

Keep protobuf compatibility intentional. Test that generated contracts preserve required fields/enums/oneof cases when evolving the protocol.

## Determinism

- Inject clocks/randomness where gameplay depends on them.
- Avoid Task.Delay in domain tests.
- Prefer fake transports and deterministic schedulers.
- Assert observable state/events, not implementation details.

## Concurrency

Include tests where both players issue commands concurrently. The expected result must be independent of task scheduling except where the protocol explicitly defines ordering.

## Naming

Test names should describe the behavior and expected outcome, e.g. RejectsCommandFromPlayerWhoDoesNotOwnTurn.
