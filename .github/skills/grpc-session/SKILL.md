
# gRPC Session Skill

## Goal

Implement a bidirectional gRPC PVP service as a transport adapter over an application-level match session.

## Required separation

The generated gRPC base class is not the game engine.

Use this conceptual flow:

gRPC stream -> adapter -> validated command -> MatchSession -> domain state transition -> domain event -> outbound projection -> gRPC stream

The session must be usable from tests without a live network connection.

## Contract guidance

The protobuf contract should distinguish:
- client commands from server events;
- session/match identity from player identity;
- command sequence from server event sequence;
- state version from transport sequence;
- rejection/error information from successful game events.

Prefer a single versioned envelope for the stream when it materially reduces protocol ambiguity.

## Server authority

The server generates and owns:
- match/session IDs;
- player slots;
- turn ownership;
- board seed/deck or equivalent authoritative game state;
- state versions;
- event sequences;
- final result.

Clients send intents/commands, not authoritative outcomes.

## Lifecycle

A session should expose explicit transitions such as:

WaitingForPlayers -> Ready -> Running -> Completed

with terminal alternatives such as Aborted and Expired.

Invalid transitions are domain results, not transport exceptions.

## Future anti-cheat seam

Place validation behind an application/domain policy boundary, for example ICommandValidator, IMatchRule, or IAntiCheatSignalCollector, without making the first implementation depend on anti-cheat infrastructure.

The first implementation can contain a normal rules validator. Future anti-cheat policies can be composed around the same command/state transition pipeline.
