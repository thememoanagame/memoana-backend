
# Networking Skill

## Goal

Build reliable, observable and secure network behavior for the MemoAna PVP bridge.

## Rules

- Treat network input as hostile/untrusted.
- Model connection, readiness, running, completion, abort and disconnect explicitly.
- Propagate cancellation from the gRPC call to session command processing.
- Never perform unbounded buffering.
- Bound message sizes and validate all required fields.
- Give commands explicit IDs/sequences and make duplicate handling deterministic.
- Serialize state mutation per match/session.
- Never hold a lock while awaiting network I/O.
- Distinguish transport failure from domain rejection.
- Log session/command identifiers, not sensitive payloads.
- Make reconnect semantics explicit rather than accidental.

## Bidirectional streaming

A bidi gRPC stream should be treated as two halves of one logical session:

- inbound reader: validates/decodes commands and submits them to the session;
- outbound writer: serializes session events to the client.

The two loops must not independently mutate authoritative state.

## Reliability

Design for:
- duplicate commands;
- out-of-order client messages;
- client cancellation;
- server cancellation;
- half-open connections;
- slow consumers;
- concurrent commands from both players;
- session expiration.

Use bounded queues/channels and explicit backpressure where appropriate.
