# ARCHITECTURE

## High-level architecture
The backend is a modular monolith with two runtime hosts:
1. `TcpServer` host for TCP ingestion and protocol processing
2. `ImpiTrack.Api` host for auth/account/admin/ops APIs

Shared libraries provide protocol abstractions, data access, application services, auth infrastructure, and observability.

## Ingestion flow (TCP)
1. `Worker` accepts TCP sessions on configured ports
2. Framing decoder handles concatenated/partial frames
3. Protocol parser resolves payload model (Coban/Cantrack)
4. ACK strategy emits protocol-specific fast ACK
5. Raw packet is queued and persisted
6. Parsed envelope is queued for persistence + event publication
7. `InboundProcessingService` persists normalized data and publishes events
8. `RawPacketProcessingService` persists raw packets asynchronously

## API flow
1. HTTP request enters API middleware (`ApiExceptionMiddleware`, status code mapping)
2. JWT auth + policy auth evaluated
3. Controllers call application services/repositories
4. Data layer resolves provider at runtime via `DatabaseRuntimeContext`
5. Response returned with consistent API envelope/problem format

## Persistence architecture
- Runtime provider selection: InMemory, SqlServer, Postgres
- In SQL mode, a single `SqlDataRepository` dispatches provider-specific SQL snippets
- Idempotency strategy includes dedupe keys for positions and upsert logic for raw/session snapshots

## Event bus architecture
- Interface-driven event bus (`IEventBus`)
- Provider selected by config: `InMemoryEventBus` or `EmqxMqttEventBus`
- Retry with backoff and optional DLQ publish on exhaustion

## Key boundaries
- Protocol details stay in protocol projects and ack strategies
- TCP host does not call API for ingestion flow
- API business logic is separated from transport concerns via application abstractions
