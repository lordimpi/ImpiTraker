# IMPITrack Current State

Role: current-state  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-19  
Last Updated: 2026-03-19

This document is the canonical source of truth for the current backend/runtime state of this repository.

Not canonical:
- `README.md` is only the entry index.
- historical plans and PRDs are context, not current truth.
- runbooks are procedures, not architecture truth.
- OpenAPI/Scalar is the contract source for HTTP endpoint detail.

## 1. What This Repo Is

IMPITrack is a backend-focused GPS telemetry platform repository. The current solution runs as a **single unified process**:

- `ImpiTrack.Api`: HTTP API for auth, admin, me, ops, health, OpenAPI/Scalar, and SignalR real-time push. Hosts TCP ingestion services in-process.
- `TcpServer`: class library (not a standalone executable) providing TCP ingestion services (`Worker`, `InboundProcessingService`, `RawPacketProcessingService`). Registered into the API host via `AddTcpServerServices()`. A `#if STANDALONE_HOST` guard in `Program.cs` preserves a standalone entry point for isolated debugging only.
- shared libraries for application logic, auth infrastructure, SQL persistence, protocol parsing, observability, and tests.

Frontend work is not part of this repository.

## 2. Canonical Documentation Model

The documentation taxonomy for this repository is:

- `index`: stable entry points such as `README.md` and `ImpiTrack/Docs/README.md`
- `current-state`: this file only
- `runbook`: repeatable operational procedures
- `adr`: architecture decisions with status and rationale
- `history`: MVP plans, PRDs, and superseded notes kept for context

If a future document does not fit one of those roles, it should not be treated as canonical until that is resolved.

## 3. Architecture Snapshot

Current repository structure verified from `ImpiTrack/ImpiTrack.sln` and project layout:

- host:
  - `ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj` — single executable host (HTTP + TCP + SignalR)
- class libraries (TCP ingestion):
  - `ImpiTrack/TcpServer/TcpServer.csproj` — class library, hosted in-process by API
- shared/backend libraries:
  - `ImpiTrack/ImpiTrack.Application`
  - `ImpiTrack/ImpiTrack.Auth.Infrastructure`
  - `ImpiTrack/ImpiTrack.DataAccess`
  - `ImpiTrack/ImpiTrack.Observability`
  - `ImpiTrack/ImpiTrack.Ops`
  - `ImpiTrack/ImpiTrack.Protocols.Abstractions`
  - `ImpiTrack/ImpiTrack.Protocols.Coban`
  - `ImpiTrack/ImpiTrack.Protocols.Cantrack`
  - `ImpiTrack/ImpiTrack.Tcp.Core`
  - `ImpiTrack/ImpiTrack.Shared`
- verification:
  - `ImpiTrack/ImpiTrack.Tests`

### 3.1 Project Dependency Graph

Verified from `.csproj` project references as of 2026-03-18:

```
Protocols.Abstractions   ← no dependencies
Shared                   ← no dependencies  (Options pattern + HTTP DTOs)
Application              → Shared, Protocols.Abstractions
DataAccess               → Application, Ops, Tcp.Core, Protocols.Abstractions, Shared
Auth.Infrastructure      → DataAccess, Shared, Application
Api                      → Ops, DataAccess, Shared, Application, Auth.Infrastructure, TcpServer
TcpServer                → Tcp.Core, Protocols.Coban, Protocols.Cantrack, Protocols.Abstractions,
                           Observability, Ops, DataAccess, Shared
Tests                    → Tcp.Core, Protocols.Abstractions, Protocols.Coban, Protocols.Cantrack,
                           Ops, DataAccess, Api, TcpServer
```

Key architectural constraints enforced:

- `Application` does **not** reference `DataAccess`. Repository interfaces (`IUserAccountRepository`, `ITelemetryQueryRepository`) live in `Application.Abstractions/` and are implemented by `DataAccess`. This satisfies the Dependency Inversion Principle: the domain layer defines the contract; the infrastructure layer implements it.
- `Shared` does **not** reference any other project in this solution. It holds cross-cutting concerns with no inward dependencies: the Options pattern (`IGenericOptionsService`, `GenericOptionsService` in `ImpiTrack.Shared.Options`) and shared HTTP DTOs.
- `DataAccess` references `Application` (to implement its repository contracts) and `Tcp.Core` (for `IIngestionRepository` which depends on the TCP queue).

### 3.2 Abstractions Placement

| Abstraction | Namespace | Project | Reason |
|---|---|---|---|
| `IUserAccountRepository` | `ImpiTrack.Application.Abstractions` | `Application` | Domain contract, implemented by DataAccess |
| `ITelemetryQueryRepository` | `ImpiTrack.Application.Abstractions` | `Application` | Domain contract, implemented by DataAccess |
| `TelemetryModels`, `UserAccountModels` | `ImpiTrack.Application.Abstractions` | `Application` | Domain models shared between service and repo layers |
| `IIngestionRepository` | `ImpiTrack.DataAccess` | `DataAccess` | Infrastructure-only; depends on `Tcp.Core.Queue` — intentionally kept in DataAccess |
| `IOpsRepository`, `IDbConnectionFactory`, `IMigrationRunner` | `ImpiTrack.DataAccess` | `DataAccess` | Pure infrastructure contracts with no domain meaning |
| `IGenericOptionsService`, `GenericOptionsService` | `ImpiTrack.Shared.Options` | `Shared` | Cross-cutting; used by TcpServer and DataAccess without circular deps |
| `ITelemetryNotifier` | `ImpiTrack.Application.Abstractions` | `Application` | Domain contract for real-time push; implemented by `SignalRTelemetryNotifier` (Api) and `NullTelemetryNotifier` (Application, fallback) |
| `IDeviceOwnershipResolver` | `ImpiTrack.Application.Abstractions` | `Application` | Resolves IMEI → userId(s); implemented by `CachedDeviceOwnershipResolver` (Api) |
| `PositionUpdatedMessage`, `DeviceStatusChangedMessage`, `TelemetryEventOccurredMessage` | `ImpiTrack.Application.Abstractions` | `Application` | SignalR push DTOs in `RealtimeDtos.cs` |

Operational shape today:

1. GPS devices connect to TCP listeners hosted in-process by the API (via `TcpServer` class library).
2. TCP framing/parsing/ACK happens in the ingestion path (`Worker` → `InboundProcessingService`).
3. Raw packets and normalized telemetry are persisted through `ImpiTrack.DataAccess`.
4. After successful persistence, `InboundProcessingService` fires real-time notifications via `ITelemetryNotifier` (see §6.8).
5. `ImpiTrack.Api` serves REST endpoints for auth, device ownership, health, ops, and exposes SignalR hub at `/hubs/telemetry`.
6. OpenAPI and Scalar document the HTTP contract during Development.

## 4. Runtime State Verified In Repo

### API

- Development OpenAPI is mapped with `app.MapOpenApi()`.
- Development Scalar UI is mapped at `/scalar/v1`.
- Scalar is configured in `ImpiTrack/ImpiTrack.Api/Program.cs`.
- SignalR hub mapped at `/hubs/telemetry` (authenticated, JWT via `?access_token` query string).
- CORS: configurable origins via `Cors:AllowedOrigins` in appsettings. Uses `AllowCredentials()` — required for SignalR WebSocket transport. Previous `AllowAnyOrigin()` was incompatible with `AllowCredentials()` and was replaced.

### Development defaults

From `ImpiTrack/ImpiTrack.Api/appsettings.Development.json`:

- `Database:Provider = SqlServer`
- `IdentityStorage:Provider = SqlServer`
- `Database:EnableAutoMigrate = true`
- `EventBus:Provider = InMemory`
- `Cors:AllowedOrigins = ["http://localhost:4200", "http://localhost:3000"]`
- TCP listeners (hosted in-process):
  - `5001` for `COBAN`
  - `5002` for `CANTRACK`
- `TcpServerConfig:Pipeline:ConsumerWorkers = 2`
- `TcpServerConfig:Pipeline:RawConsumerWorkers = 2`

These are repository development defaults, not an environment-agnostic production statement.

## 5. Operational Dependencies

The current backend documentation and configuration indicate these operational dependencies or integration points:

- SQL Server is the active default persistence provider in development for both API and TCP runtime.
- PostgreSQL exists as a supported data-access path, but Identity on PostgreSQL is still constrained and documented separately in ADR-001.
- EMQX is wired as the TCP development event-bus provider in current repo settings.
- Gmail SMTP setup exists as an optional operational runbook for real email delivery.
- OpenTelemetry and observability assets exist in the repository, including `ImpiTrack/Observability/`.

## 6. Telemetry Pipeline — Protocol Parsing And Events

### 6.1 Coban Protocol Parser

The Coban protocol parser (`ImpiTrack.Protocols.Coban`) is the active parser for devices on TCP port 5001.

Key parsing behavior verified in source:

- `GpsTimeUtc` is assembled by combining the **date** from field[2] (local device time, UTC-5 Colombia) with the **UTC time** from field[5]. Day rollover is detected and compensated. Prior to this fix, the parser was using field[2] as UTC, causing a 5-hour drift in all stored timestamps.
- `IgnitionOn` is parsed from field[14]: `1` = ACC ON, `0` = ACC OFF. Event type `"acc on"` is also recognized.
- `PowerConnected` is derived from field[3] (battery percentage) as a heuristic proxy. Not a direct hardware signal.
- `DoorOpen` parsing from field[15] is **pending** — no confirmed packet with door-open state has been captured yet (TODO B.12). Field is currently unparsed; `DoorOpen` will be null on all Coban messages until confirmed.
- Cantrack parser: `IgnitionOn` remains null until real packet format is confirmed.

### 6.2 Canonical ParsedMessage Model

`ImpiTrack.Protocols.Abstractions.ParsedMessage` is the normalized contract produced by all protocol parsers:

- `GpsTimeUtc` — GPS timestamp in UTC (assembled correctly per §6.1).
- `IgnitionOn` — `bool?` — ACC state. Null if the protocol does not provide this field.
- `PowerConnected` — `bool?` — external power. Null if not available.
- `DoorOpen` — `bool?` — door sensor. Null until field[15] (Coban) is confirmed (TODO B.12).

### 6.3 State Change Event Emission (InboundProcessingService)

For every `Tracking` message, `InboundProcessingService` compares the current `ParsedMessage` fields against the last known per-IMEI state (held in a `ConcurrentDictionary<string, DeviceIoState>`). On state transitions it persists records to `device_events`:

| Transition | Event code emitted |
|---|---|
| IgnitionOn: any → true | `ACC_ON` |
| IgnitionOn: true → false | `ACC_OFF` |
| PowerConnected: any → true | `PWR_ON` |
| PowerConnected: true → false | `PWR_OFF` |

`occurred_at_utc` on each event record is set to `GpsTimeUtc` when available, falling back to `ReceivedAtUtc`.

### 6.4 Database Schema — device_events (V007)

Migration `V007__device_events_occurred_at` (SqlServer and PostgreSQL) adds column:

```
occurred_at_utc  DATETIMEOFFSET  NULL
```

This column stores the event timestamp derived from the GPS clock. It is used by the trip detection engine to correlate ACC events with position data.

### 6.5 Database Schema — user_devices alias (V008)

Migration `V008__user_devices_alias` (SqlServer and PostgreSQL) adds column:

```
alias  NVARCHAR(50) / VARCHAR(50)  NULL
```

This column stores a user-assigned friendly name for a device binding. The alias is scoped per user-device binding (not global to the device). It is nullable: when null or empty, the device has no alias.

Affected domain models:

| Model | Project | Change |
|---|---|---|
| `UserDeviceBinding` | `Application.Abstractions` | Added `string? Alias = null` |
| `TelemetryDeviceSummaryDto` | `Application.Abstractions` | Added `string? Alias = null` |
| `DeviceAliasResult` | `Application.Abstractions` | New record: `(string Imei, string? Alias)` |
| `UpdateDeviceAliasRequest` | `Shared.Models` | New DTO: `{ string? Alias }` |
| `UpdateDeviceAliasStatus` | `Application.Abstractions` | New enum: `Updated`, `UserNotFound`, `BindingNotFound`, `AliasTooLong` |

### 6.6 Device Alias API Endpoints

Two endpoints expose alias management:

| Endpoint | Route | Auth |
|---|---|---|
| User self-service | `PUT /api/me/telemetry/devices/{imei}/alias` | `[Authorize]` |
| Admin on behalf of user | `PUT /api/admin/users/{userId}/telemetry/devices/{imei}/alias` | `[Authorize(Policy = "Admin")]` |

Request body: `{ "alias": "string or null" }`

Behavior:
- Sets alias when `alias` is a non-empty string (trimmed, max 50 chars).
- Clears alias when `alias` is null, empty, or whitespace-only (stored as `NULL`).
- Returns `200 OK` with `DeviceAliasResult(Imei, Alias)` on success.
- Returns `400 Bad Request` (`alias_too_long`) when trimmed alias exceeds 50 characters.
- Returns `404 Not Found` (`device_binding_not_found`) when no active binding exists for the IMEI.
- Admin endpoint also returns `404` (`user_not_found`) when the target user does not exist.

The alias is also included in `GET /api/me/telemetry/devices` and `GET /api/admin/users/{userId}/telemetry/devices` responses via `TelemetryDeviceSummaryDto.Alias`.

Service layer: `MeAccountService.UpdateDeviceAliasAsync` and `AdminUsersService.UpdateDeviceAliasAsync` implement identical validation logic (normalize whitespace, enforce max length) and delegate to `IUserAccountRepository.UpdateDeviceAliasAsync`.

### 6.7 Trip Detection — movement_2d_acc_v2

Trip detection runs in `TelemetryQueryService.BuildTrips`. The active algorithm identifier is `movement_2d_acc_v2`.

**Movement detection criteria:**

- Speed threshold: `>= 12 km/h` (raised from previous 5 km/h).
- 2D spatial threshold: `|dLat| >= 0.00018°` AND `|dLon| >= 0.00018°` (≈20 m per axis, both axes required). Replaces prior Haversine distance calculation.

**ACC as primary trip signal:**

- Trip open: `ACC_ON` event (ignition transitions from non-true to true). Speed+2D is the fallback when no ACC data is available for the window.
- Trip close: `ACC_OFF` event (ignition transitions from true to false). Temporal gap (`> 10 min since last moving point`) is the fallback close signal.

**Data flow:**

1. `GetTripCandidatesAsync` fetches position points and ACC events for the window in parallel.
2. `GetAccEventsForWindowAsync` queries `device_events` for `ACC_ON` / `ACC_OFF` records.
3. `AnnotateWithAccState` stamps each position point with the last known ACC state prior to its timestamp.
4. `BuildTrips` uses annotated `IgnitionOn` field to drive open/close logic; falls back to speed+2D if `hasAccData = false`.

### 6.8 Real-Time Telemetry — SignalR Push Notifications

Real-time push notifications are delivered to connected clients via a SignalR hub at `/hubs/telemetry`.

**Architecture:**

The notification chain is: `InboundProcessingService` → `ITelemetryNotifier` → `IDeviceOwnershipResolver` → `IHubContext<TelemetryHub>`.

| Component | Project | Responsibility |
|---|---|---|
| `ITelemetryNotifier` | `Application` (abstractions) | Domain contract for push notifications |
| `SignalRTelemetryNotifier` | `Api` (`Services/`) | Production implementation: resolves device owners, sends to SignalR groups |
| `NullTelemetryNotifier` | `Application` (abstractions) | No-op fallback when SignalR is unavailable (e.g. TcpServer standalone mode) |
| `IDeviceOwnershipResolver` | `Application` (abstractions) | Resolves IMEI → `IReadOnlyList<Guid>` userId(s) |
| `CachedDeviceOwnershipResolver` | `Api` (`Services/`) | Production implementation with `IMemoryCache`, TTL 30s per IMEI |
| `TelemetryHub` | `Api` (`Hubs/`) | SignalR hub, `[Authorize]`, groups by `user_{userId}` |

**Push events (server → client, unidirectional):**

| SignalR Event | Trigger | Payload |
|---|---|---|
| `PositionUpdated` | Each GPS tracking message persisted | `PositionUpdatedMessage(Imei, Latitude, Longitude, SpeedKmh, HeadingDeg, OccurredAtUtc, IgnitionOn)` |
| `DeviceStatusChanged` | Device connection state change | `DeviceStatusChangedMessage(Imei, Status, ChangedAtUtc)` |
| `TelemetryEventOccurred` | ACC/PWR state change event persisted | `TelemetryEventOccurredMessage(Imei, EventType, Latitude, Longitude, OccurredAtUtc)` |

**Client authentication:** JWT is passed via `?access_token` query string parameter (standard SignalR pattern for WebSocket connections where `Authorization` header is unavailable).

**User isolation:** Each authenticated client is added to group `user_{userId}` on connect. Notifications are sent only to groups corresponding to the device's owners via `IDeviceOwnershipResolver`.

**Resilience:** `SignalRTelemetryNotifier` wraps every notification in try/catch. A SignalR failure (hub unavailable, client disconnected, network error) is logged but **never** interrupts the persistence pipeline. `InboundProcessingService` calls `ITelemetryNotifier` **after** successful persistence.

**DI registration in API (`Program.cs`):**

- `AddSignalR()` — registers SignalR infrastructure.
- `AddMemoryCache()` — for `CachedDeviceOwnershipResolver`.
- `AddSingleton<IDeviceOwnershipResolver, CachedDeviceOwnershipResolver>()`.
- `AddSingleton<ITelemetryNotifier, SignalRTelemetryNotifier>()` — overrides the `NullTelemetryNotifier` default registered by `TcpServer.ServiceCollectionExtensions` via `TryAddSingleton`.

**TcpServer fallback registration:** `ServiceCollectionExtensions.AddTcpServerServices()` registers `NullTelemetryNotifier` via `TryAddSingleton<ITelemetryNotifier>`. Since API registers `SignalRTelemetryNotifier` before calling `AddTcpServerServices()`, the `TryAdd` is a no-op in the unified host. In standalone debug mode, `NullTelemetryNotifier` activates as the default.

### 6.9 Testing Strategy — Integration Tests With TestHostedServiceHelper

Integration tests for API endpoints (`ApiAuthFlowTests`, `ApiOpsAuthTests`, `ApiRegistrationAndAccountTests`, `ApiTelemetryTests`, `ApiPasswordRecoveryTests`) use `WebApplicationFactory<Program>`. Because the API now hosts TCP services in-process, these hosted services (`Worker`, `InboundProcessingService`, `RawPacketProcessingService`) would attempt to bind TCP ports during test startup.

`TestHostedServiceHelper.RemoveTcpHostedServices(IServiceCollection)` removes the three TCP `IHostedService` registrations from the DI container during test configuration, preventing port binding conflicts.

SignalR-specific tests (`SignalRTelemetryNotifierTests`, `NotificationResilienceTests`) test the notification chain in isolation with mocked `IHubContext<TelemetryHub>` and stub `IDeviceOwnershipResolver`, verifying:

- Correct routing to `user_{userId}` groups.
- All three events are dispatched to the right recipients.
- Exception resilience: notifier failures do not propagate to the caller.
- `NullTelemetryNotifier` silently no-ops without exceptions.

## 7. Known Limits And Boundaries

- Frontend is out of repo scope.
- Identity on PostgreSQL is not the default active path; see [`ImpiTrack/Docs/adr/README.md`](adr/README.md) and ADR-001.
- Historical MVP plans and PRDs still exist for context, but they are not the current implementation contract.
- Endpoint-level HTTP truth belongs to OpenAPI/Scalar, not to markdown copies.
- This document should describe the current verified state only. Do not turn it into a speculative roadmap dump.

## 8. Known Technical Debt

- **TODO B.12 — Coban field[15] (Door sensor):** `DoorOpen` parsing is deferred until a real packet with door-open state is captured and the field position confirmed. Currently `DoorOpen` is null on all Coban messages.
- **Cantrack IgnitionOn:** Remains null until the Cantrack packet format for ACC state is confirmed against real hardware data.
- **PowerConnected heuristic:** The Coban parser derives `PowerConnected` from battery percentage (field[3]), which is an indirect proxy, not a direct hardware line signal.

## 9. Where To Update What

- Update this file when runtime behavior, architecture shape, config defaults, operational dependencies, or active constraints change.
- Update runbooks when the procedure changes but the architecture truth does not.
- Update ADRs when a decision changes or a new architectural tradeoff is accepted.
- Update history docs only to preserve context or add explicit supersession notes.

## 10. Governance

- Canonical owner: `backend-maintainers` until a named owner is assigned.
- Review cadence: review this file in any PR that changes runtime behavior, API contract exposure, configuration defaults, operational setup, or deployment assumptions.
- Completion rule for impactful PRs: if a change affects runtime, API, config, or operations, the PR is not complete until canonical documentation is updated or explicitly confirmed as not needed.

## 11. Canonical Doc Map

- Repo index: [`../../README.md`](../../README.md)
- Docs index: [`README.md`](README.md)
- Active runbooks: [`runbooks/README.md`](runbooks/README.md)
- ADRs: [`adr/README.md`](adr/README.md)
- Historical archive: [`history/README.md`](history/README.md)

## 12. Verification Sources Used For This Snapshot

This current-state document was aligned against the repository structure and existing implementation-facing docs, including:

- `ImpiTrack/ImpiTrack.Api/Program.cs`
- `ImpiTrack/ImpiTrack.Api/appsettings.Development.json`
- `ImpiTrack/TcpServer/appsettings.Development.json`
- `ImpiTrack/ImpiTrack.Protocols.Coban/CobanProtocolParser.cs`
- `ImpiTrack/TcpServer/InboundProcessingService.cs`
- `ImpiTrack/ImpiTrack.Application/Services/TelemetryQueryService.cs`
- `ImpiTrack/ImpiTrack.Protocols.Abstractions/ParsedMessage.cs`
- `ImpiTrack/ImpiTrack.DataAccess/db/sqlserver/V007__device_events_occurred_at.sql`
- `ImpiTrack/ImpiTrack.DataAccess/db/sqlserver/V008__user_devices_alias.sql`
- `ImpiTrack/ImpiTrack.DataAccess/db/postgres/V008__user_devices_alias.sql`
- `ImpiTrack/ImpiTrack.Api/Controllers/MeTelemetryController.cs`
- `ImpiTrack/ImpiTrack.Api/Controllers/AdminUserTelemetryController.cs`
- `ImpiTrack/ImpiTrack.Application/Abstractions/ServiceResults.cs`
- `ImpiTrack/ImpiTrack.Application/Abstractions/UserAccountModels.cs`
- `ImpiTrack/ImpiTrack.Application/Abstractions/TelemetryModels.cs`
- `ImpiTrack/ImpiTrack.Shared/Models/UpdateDeviceAliasRequest.cs`
- `ImpiTrack/ImpiTrack.Api/Hubs/TelemetryHub.cs`
- `ImpiTrack/ImpiTrack.Api/Services/SignalRTelemetryNotifier.cs`
- `ImpiTrack/ImpiTrack.Api/Services/CachedDeviceOwnershipResolver.cs`
- `ImpiTrack/ImpiTrack.Application/Abstractions/ITelemetryNotifier.cs`
- `ImpiTrack/ImpiTrack.Application/Abstractions/IDeviceOwnershipResolver.cs`
- `ImpiTrack/ImpiTrack.Application/Abstractions/NullTelemetryNotifier.cs`
- `ImpiTrack/ImpiTrack.Application/Abstractions/RealtimeDtos.cs`
- `ImpiTrack/TcpServer/ServiceCollectionExtensions.cs`
- `ImpiTrack/ImpiTrack.Tests/TestHostedServiceHelper.cs`
- `ImpiTrack/ImpiTrack.Tests/SignalRTelemetryNotifierTests.cs`
- `ImpiTrack/ImpiTrack.Tests/NotificationResilienceTests.cs`
- `ImpiTrack/Docs/BACKEND_MAINTENANCE_GUIDE.md` (superseded by this file)
- the active solution/project layout under `ImpiTrack/`
- all `.csproj` project references (verified 2026-03-19, post `signalr-realtime-telemetry`)
