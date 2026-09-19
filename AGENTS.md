
# MemoAna Backend — Agent Instructions

## Purpose

This repository is the network/backend side of MemoAna. It exposes the backend boundary used by the .NET MAUI Blazor Hybrid client and is intended to host the server-side PVP session/matchmaking bridge.

The immediate architectural goal is a bidirectional gRPC game session between two MAUI clients, while retaining REST endpoints in the same deployable application.

## Core architecture: feature-aggregates

Organize code by feature aggregate, not by global technical layer.

A feature aggregate owns the code needed to implement one cohesive capability. Inside an aggregate, group files by architectural role, for example:

- GameMatchMaking/
  - Grpc/ — protobuf-facing service adapters and stream handling.
  - Application/ — use cases/orchestration.
  - Domain/ — match/session state, invariants and domain models.
  - Contracts/ — request/response DTOs and mapping contracts.
  - Infrastructure/ — persistence, transports or external integrations specific to the feature.
  - Tests/ — tests belonging specifically to the aggregate when that convention is used.

Do not create repository-wide Services, Models, Dtos, Grpc, or Controllers buckets merely because files share a technical type. A file belongs first to its feature aggregate.

Shared infrastructure is allowed only when it is genuinely cross-feature. Prefer small explicit abstractions over generic frameworks.

## Networking rules

- gRPC is the authoritative transport for real-time PVP session traffic.
- Keep protobuf contracts stable and versionable.
- Treat every client message as untrusted input.
- Never trust client-reported score, winner, turn ownership, timestamps, board state, or match completion.
- Use server-generated session, player, turn and message identifiers.
- Make ordering and idempotency explicit for bidirectional streams.
- Cancellation, disconnects and reconnects are normal state transitions, not exceptional happy-path failures.
- Never block a gRPC stream indefinitely without a cancellation path.
- Avoid putting game-domain rules inside generated gRPC classes.
- Transport adapters translate between protobuf and application/domain types; they do not become the domain model.

## Match/session design principles

The future GameMatchMakingService should be a thin streaming adapter over an application-level match session.

Conceptually separate:
1. Matchmaking — finding/creating an opponent.
2. Session — binding two authenticated players to one match.
3. Authoritative game state — server-owned state and legal transitions.
4. Projection/events — messages sent to each client.
5. Anti-cheat policy — independent validation/telemetry rules that can be added without rewriting the session transport.

A match must have an explicit lifecycle such as Waiting -> Ready -> Running -> Completed/Aborted/Expired.

A client must never be able to advance the lifecycle by merely claiming that it has advanced.

## MAUI integration

The MAUI client currently has a GameService that consumes ILocalPVPService. The intended migration is to replace the LAN transport implementation with a remote PVPService while keeping GameService focused on game orchestration/UI-facing state.

The client transport should own connection, stream lifecycle, serialization and reconnection concerns. GameService should consume a domain-oriented PVP abstraction rather than know about gRPC calls.

## Testing

Every new session rule should have deterministic unit tests.

At minimum cover:
- valid/invalid session lifecycle transitions;
- duplicate and out-of-order messages;
- two clients sending concurrently;
- illegal turn ownership;
- disconnect/cancellation;
- match completion;
- reconnect/resume policy if supported;
- malformed protobuf payloads;
- server-side rejection of client-authoritative values.

Prefer fake/in-memory transports and deterministic clocks over sleeps and real sockets in unit tests.

Integration tests should exercise the real gRPC service and a pair of clients/streams. Keep these separate from pure domain tests.

## Async/concurrency

- Use CancellationToken end-to-end.
- Never use async void.
- Prefer per-session serialized processing over shared mutable state.
- Avoid locks around network I/O.
- If a session has one authoritative command queue, process commands sequentially and publish resulting events.
- Ensure disposal closes streams and releases session ownership exactly once.

## Security and anti-cheat extensibility

Design validation as composable policies/validators that observe or validate commands and resulting state.

Do not couple anti-cheat logic to gRPC method implementations. A future validator should be able to inspect:
- command identity and sequence;
- player/session identity;
- legal turn;
- action timing;
- board/state version;
- impossible transitions;
- disconnect/reconnect patterns.

Anti-cheat decisions should be represented as domain/application outcomes (accept, reject, flag, terminate, etc.), not as ad-hoc exceptions from transport code.

## REST + gRPC hosting

REST and gRPC may live in the same ASP.NET Core process/container. Keep their presentation adapters separate while sharing application/domain aggregates.

The deployable host should own only composition/configuration concerns: dependency injection, endpoint mapping, middleware, health checks, configuration and logging.

## Agent workflow

Before changing code:
1. Identify the feature aggregate.
2. Read its contracts and existing tests.
3. Trace the caller and callee boundaries.
4. Preserve existing public behavior unless the task explicitly changes it.
5. Add/adjust tests with every behavioral change.
6. Keep transport code thin.
7. Prefer explicit domain names over generic abstractions.
8. Document architectural decisions when a new cross-cutting rule is introduced.

Do not implement the first version of the remote PVP session until the protobuf contract, session lifecycle and authoritative state model are agreed upon.
