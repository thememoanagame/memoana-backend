# MemoAna Backend — Architecture

## Official architecture

The gRPC/REST backend will use a **feature-oriented application/domain architecture** with explicit separation between application orchestration, domain state, infrastructure adapters, presentation endpoints, and protobuf contracts.

The project structure is:

```text
src/MemoAna/
├── Application/
│   ├── Abstractions/
│   ├── Commands/
│   ├── Handlers/
│   ├── Notifications/
│   ├── Queries/
│   ├── Requests/
│   ├── Responses/
│   └── Validators/
├── Domain/
│   ├── Matchmaking/
│   └── Sessions/
├── Infrastructure/
│   ├── Grpc/
│   ├── Rest/
│   ├── Persistence/
│   └── AntiCheat/
├── Presentation/
│   ├── Rest/
│   └── Grpc/
└── Proto/
    └── game_matchmaking.proto
```

This structure is the architecture to be used for the project. New implementation work must respect these boundaries rather than introducing repository-wide technical folders such as `Services/`, `Dtos/`, or `Controllers/`.

## Responsibilities

### Application

Application contains use-case orchestration and application contracts.

- **Abstractions/** — ports/interfaces consumed by application use cases.
- **Commands/** — intent to change state.
- **Handlers/** — orchestration for commands, queries and requests.
- **Notifications/** — application-level notifications/events that do not themselves define domain state.
- **Queries/** — read-side requests.
- **Requests/** — application input models at use-case boundaries.
- **Responses/** — application output models.
- **Validators/** — input and application-level validation.

Application code must not depend on concrete gRPC/REST presentation details.

### Domain

Domain contains the authoritative game/match concepts and invariants.

- **Matchmaking/** — finding and creating matches, player pairing and matchmaking lifecycle.
- **Sessions/** — the authoritative PVP match session, player slots, lifecycle, command processing, state transitions and domain events.

A `Session` is the unit of authoritative state and concurrency. Its state must be mutated through explicit commands and deterministic transitions.

The domain must be testable without ASP.NET Core, gRPC networking, databases or real clocks.

### Infrastructure

Infrastructure provides implementations of application ports and external-system adapters.

- **Grpc/** — gRPC-specific infrastructure/client or integration concerns that are not the endpoint adapter itself.
- **Rest/** — REST-specific infrastructure/integration concerns.
- **Persistence/** — persistence implementations.
- **AntiCheat/** — future anti-cheat implementations, telemetry and policy integrations.

Anti-cheat must remain replaceable and must not be coupled to the gRPC endpoint. The first implementation can expose validation/policy seams without requiring a complete anti-cheat system.

### Presentation

Presentation exposes the application to external callers.

- **Rest/** — REST controllers/endpoints and HTTP-to-application mapping.
- **Grpc/** — gRPC service implementations and transport-to-application mapping.

Presentation is deliberately thin. Generated gRPC types and HTTP request types must not become domain models.

### Proto

`Proto/game_matchmaking.proto` is the source contract for the PVP streaming protocol.

The protocol must distinguish client commands from server events and must provide enough metadata for deterministic ordering, idempotency and future reconnection.

## Data flow

The intended MAUI-to-backend flow is:

```text
MAUI GameService
      │
      ▼
IPVPService / PVPService
      │
      ▼
gRPC client / bidi stream
      │
      ▼
Presentation/Grpc
      │
      ▼
Application Commands / Requests
      │
      ▼
Handlers + Validators
      │
      ▼
Domain/Sessions
      │
      ├── Domain rules
      ├── authoritative state
      ├── state transition
      └── domain events
      │
      ├──────────────► Infrastructure/AntiCheat
      │
      ├──────────────► Infrastructure/Persistence
      │
      ▼
Application Responses / Notifications
      │
      ▼
Presentation/Grpc
      │
      ▼
gRPC server event stream
      │
      ▼
MAUI PVPService
      │
      ▼
GameService
```

REST follows the same application/domain path:

```text
REST client
   │
   ▼
Presentation/Rest
   │
   ▼
Application
   │
   ▼
Domain
   │
   ▼
Infrastructure
```

REST and gRPC are therefore two presentation transports over the same application/domain model rather than two independent implementations of game rules.

## Authoritative PVP session

The server is authoritative for:

- match/session identity;
- player slots;
- lifecycle;
- turn ownership;
- board/game state;
- scores and results;
- state versions;
- server event sequence;
- legality of player commands.

Clients send **intent**, not authoritative outcomes.

For example, a client may send:

```text
FlipCard(position=7)
```

but must not send:

```text
TurnResult(match=true, nextPlayer=..., score=...)
```

The server derives those results from its authoritative state.

## Bidirectional streaming

The gRPC session is conceptually:

```text
Client command stream
        │
        ▼
transport validation/mapping
        │
        ▼
session command queue
        │
        ▼
single authoritative session processor
        │
        ▼
domain events
        │
        ▼
player-specific event projection
        │
        ▼
server event stream
```

The inbound and outbound stream loops must not independently mutate match state.

Each session should use serialized command processing, preferably with a bounded asynchronous queue/channel, so concurrent commands from both players are resolved deterministically.

## Reliability and protocol rules

Commands/events should support explicit:

- `match_id`;
- `session_id`;
- `player_id`;
- `command_id`;
- client command sequence;
- server event sequence;
- `state_version`.

Duplicate commands must have deterministic behavior. Out-of-order commands must be rejected or handled according to an explicit protocol rule.

Cancellation and disconnect are normal session lifecycle events. Reconnect/resume semantics should be designed into identifiers and versions even if full resume is implemented later.

## Anti-cheat extension point

Anti-cheat is an extension of the command/state pipeline:

```text
Client Command
     │
     ▼
Application validation
     │
     ▼
Domain rules
     │
     ├── accept/reject
     │
     └── anti-cheat signals
             │
             ▼
      Infrastructure/AntiCheat
```

The initial system should not depend on an external anti-cheat product or implementation. Instead, the architecture must make it possible to add policies that observe/validate:

- command identity and sequence;
- player/session identity;
- turn ownership;
- timing;
- state version;
- impossible state transitions;
- repeated invalid commands;
- disconnect/reconnect behavior.

Anti-cheat outcomes should be explicit application/domain outcomes rather than transport-specific exceptions.

## MAUI integration boundary

On the MAUI side, `GameService` should consume a PVP abstraction rather than know about gRPC internals.

The intended evolution is:

```text
GameService
    │
    ▼
IPVPService
    │
    ▼
PVPService / RemotePVPService
    │
    ▼
generated gRPC client
```

This replaces the transport implementation represented by `LocalPVPService` without moving gRPC concerns into `GameService`.

The new remote PVP implementation should preserve the GameService-facing contract wherever practical while translating the new server-authoritative event model into the events/state expected by the game layer.

## Testing architecture

Tests follow the same boundaries:

1. **Domain tests** — deterministic session/rule tests without networking.
2. **Application tests** — handlers, validators, matchmaking and orchestration using fakes.
3. **gRPC integration tests** — real ASP.NET Core endpoint and generated clients, including two concurrent players.
4. **Contract tests** — protobuf compatibility and envelope/oneof evolution.

Concurrency tests must cover both players issuing commands at the same time.

## Dependency direction

The intended dependency direction is:

```text
Presentation ──► Application ──► Domain
Infrastructure ─► Application
Infrastructure ─► Domain (only where implementation requires domain types)
Proto ──► Presentation/Grpc
```

Domain must not depend on Presentation or concrete Infrastructure.

Application must not contain transport-specific implementation details.

## Architectural decision

This document is normative for the project. Implementation issues and pull requests should treat this structure and data flow as the baseline architecture unless a subsequent architectural decision explicitly supersedes it.
