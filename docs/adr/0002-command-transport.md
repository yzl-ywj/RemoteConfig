# ADR-0002: Command Transport Mechanism

## Status

Accepted (2024-11)

## Context

Need to send commands to devices with two delivery semantics:
1. **Fire-and-forget** (reboot, config push) — at-least-once
2. **Request-response** (read sensor, get status) — synchronous

Options considered:
- Direct IoT Hub C2D messages only
- Service Bus + IoT Hub hybrid
- MQTT broker (EMQX) only

## Decision

**Hybrid approach**: Service Bus queue for buffering + IoT Hub for device delivery.
- C2D messages for async commands
- Direct Methods for sync commands

## Rationale

- Service Bus provides built-in retry, dead-lettering, and rate-limiting
- IoT Hub C2D is the native device channel (no extra broker)
- Direct Methods give sub-second sync response
- Decoupling API from IoT Hub prevents throttling cascades
- Service Bus dead-letter queue enables automated failure analysis

## Consequences

- Two message hops (API → SB → IoT Hub) add ~50-100ms latency
- Service Bus cost: ~$0.05 per 10K messages (Basic tier)
- IoT Hub S3 tier needed for >100K devices
- Must handle idempotency (device may receive duplicate C2D messages)
