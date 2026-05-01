Role: reference  
Status: active  
Owner: backend-maintainers  
Last Updated: 2026-05-01 (gps-device-commands)

# Device Commands API — Reference

This document is the authoritative contract for the **Outbound Device Commands** feature (change: `gps-device-commands`). It covers HTTP endpoints, request/response schemas, the command capability matrix, the lifecycle state machine, the SignalR real-time contract, error codes, and rate limiting.

For the E2E walkthrough with curl examples, see [`TCP_API_E2E_GUIDE.md`](TCP_API_E2E_GUIDE.md).

---

## Table of Contents

1. [Overview](#overview)
2. [Endpoints](#endpoints)
   - [POST — Enqueue command](#post--enqueue-command)
   - [GET — Single command status](#get--single-command-status)
   - [GET — Command history](#get--command-history)
3. [Request / Response Schemas](#request--response-schemas)
4. [Command Capability Matrix](#command-capability-matrix)
5. [Protocol Wire Formats](#protocol-wire-formats)
6. [Lifecycle State Machine](#lifecycle-state-machine)
7. [Error Code Reference](#error-code-reference)
8. [SignalR Contract](#signalr-contract)
9. [Rate Limiting](#rate-limiting)
10. [Configuration Keys](#configuration-keys)

---

## Overview

The Device Commands API lets an authenticated user send control commands to their GPS devices (Coban or Cantrack protocol). Commands are persisted to `device_commands` before delivery so the full lifecycle is recoverable regardless of device connectivity at enqueue time.

**Base path**: `/api/me/devices/{imei}/commands`

All endpoints require a valid JWT (`Authorization: Bearer <token>`). The `{imei}` in the route must belong to the authenticated user — a mismatch returns `404` (not `403`, to prevent device enumeration).

---

## Endpoints

### POST — Enqueue command

```
POST /api/me/devices/{imei}/commands
```

Enqueues a new command for the device. The command is persisted immediately and, if the device is online, the bytes are dispatched to its TCP session.

**Route parameter**

| Parameter | Type   | Description           |
|-----------|--------|-----------------------|
| `imei`    | string | Target device IMEI    |

**Request body** (`application/json`)

| Field         | Type    | Required | Description                                                                 |
|---------------|---------|----------|-----------------------------------------------------------------------------|
| `commandType` | string  | yes      | Command name. Must be a value from the capability matrix.                   |
| `parameters`  | object  | no       | Command-specific parameters (e.g. `{ "radius": 500 }`). See matrix below.  |
| `confirm`     | boolean | no       | Required for dangerous commands (`CutMotor`, `Reset`). Must be `true`.      |

**Responses**

| Status | Condition                                                        |
|--------|------------------------------------------------------------------|
| 202    | Command accepted and queued. Body: `{ commandId, status, queuedAtUtc }` |
| 400    | Validation failure. Body: `{ code, message }`. See error codes.  |
| 401    | Missing or invalid JWT.                                          |
| 404    | IMEI not found or does not belong to authenticated user.         |
| 429    | Rate limit exceeded. Includes `Retry-After` header.              |

**202 body**

```json
{
  "commandId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Queued",
  "queuedAtUtc": "2026-05-01T14:30:00Z"
}
```

---

### GET — Single command status

```
GET /api/me/devices/{imei}/commands/{commandId}
```

Returns the full record for a single command.

**Route parameters**

| Parameter   | Type   | Description         |
|-------------|--------|---------------------|
| `imei`      | string | Device IMEI         |
| `commandId` | UUID   | Command identifier  |

**Responses**

| Status | Condition                                                  |
|--------|------------------------------------------------------------|
| 200    | Command found. Full command record in body.                |
| 401    | Missing or invalid JWT.                                    |
| 404    | Command not found or belongs to a different user's device. |

---

### GET — Command history

```
GET /api/me/devices/{imei}/commands
```

Returns a paginated list of commands for the device, ordered by `queuedAtUtc DESC`.

**Query parameters**

| Parameter  | Type     | Default | Notes                                                        |
|------------|----------|---------|--------------------------------------------------------------|
| `status`   | string   | —       | Filter by lifecycle status. Invalid value → 400.             |
| `from`     | ISO8601  | —       | Inclusive lower bound on `queuedAtUtc`.                      |
| `to`       | ISO8601  | —       | Inclusive upper bound on `queuedAtUtc`.                      |
| `page`     | integer  | 1       | 1-based page number.                                         |
| `pageSize` | integer  | 20      | Allowed values: `10`, `20`, `50`, `100`. Other → 400.        |

**Responses**

| Status | Condition                               |
|--------|-----------------------------------------|
| 200    | Paged result. See schema below.         |
| 400    | Invalid `pageSize` or `status` value.   |
| 401    | Missing or invalid JWT.                 |
| 404    | IMEI not found or not owned by user.    |

---

## Request / Response Schemas

### Command record (full — GET single)

```json
{
  "commandId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "imei": "359586015829802",
  "commandType": "CutMotor",
  "parameters": { "note": "any user-supplied params at enqueue time" },
  "protocol": "Cantrack",
  "status": "Acknowledged",
  "queuedAtUtc": "2026-05-01T14:30:00Z",
  "sentAtUtc": "2026-05-01T14:30:01Z",
  "ackedAtUtc": "2026-05-01T14:30:02Z",
  "responseCode": "stop654321",
  "responseText": "stop engine succeed",
  "failureReason": null
}
```

Null-able fields: `sentAtUtc`, `ackedAtUtc`, `responseCode`, `responseText`, `failureReason`.

### Paged result (GET history)

```json
{
  "items": [ /* array of command summaries */ ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 35,
  "totalPages": 2
}
```

Each item in `items` carries the same shape as the full command record.

### Error response

```json
{
  "code": "confirmation_required",
  "message": "CutMotor requires confirm: true in the request body."
}
```

---

## Command Capability Matrix

`✅` = supported, `❌` = not supported (returns `400 command_not_supported_by_protocol`).

Commands marked with `*` require specific `parameters` fields (see notes column).

| Command                | Coban | Cantrack | Parameters required              |
|------------------------|:-----:|:--------:|----------------------------------|
| `Arm`                  |  ✅   |    ✅    | —                                |
| `Disarm`               |  ✅   |    ✅    | —                                |
| `CutMotor`             |  ✅   |    ✅    | `confirm: true` in body          |
| `RestoreMotor`         |  ✅   |    ✅    | —                                |
| `SetMovementAlarm`     |  ✅   |    ✅    | `parameters.radius` (integer, metres) |
| `CancelMovementAlarm`  |  ✅   |    ✅    | —                                |
| `SetOverspeedAlarm`    |  ✅   |    ❌    | `parameters.speed` (integer, km/h) |
| `SetGeofence`          |  ✅   |    ❌    | `parameters.topLeftLat`, `topLeftLon`, `bottomRightLat`, `bottomRightLon` (decimal degrees) |
| `CancelGeofence`       |  ✅   |    ❌    | —                                |
| `CancelAlarm`          |  ✅   |    ✅    | —                                |
| `RequestSinglePosition`|  ✅   |    ❌    | —                                |
| `Reset`                |  ❌   |    ✅    | `confirm: true` in body          |

---

## Protocol Wire Formats

These are the raw bytes delivered over TCP. Each frame is UTF-8 encoded ASCII.

### Coban wire format

Format: `**,imei:{IMEI},{KEYWORD}[,{EXTRA}]\r\n`

| Command                | Wire frame                                                         |
|------------------------|--------------------------------------------------------------------|
| `Arm`                  | `**,imei:IMEI,111\r\n`                                             |
| `Disarm`               | `**,imei:IMEI,112\r\n`                                             |
| `CutMotor`             | `**,imei:IMEI,109\r\n`                                             |
| `RestoreMotor`         | `**,imei:IMEI,110\r\n`                                             |
| `SetMovementAlarm`     | `**,imei:IMEI,105,{radius}\r\n`                                    |
| `CancelMovementAlarm`  | `**,imei:IMEI,106\r\n`                                             |
| `SetOverspeedAlarm`    | `**,imei:IMEI,107,{speed}\r\n`                                     |
| `SetGeofence`          | `**,imei:IMEI,114,{latTL},{lonTL};{latBR},{lonBR}\r\n`             |
| `CancelGeofence`       | `**,imei:IMEI,115\r\n`                                             |
| `CancelAlarm`          | `**,imei:IMEI,104\r\n`                                             |
| `RequestSinglePosition`| `**,imei:IMEI,100\r\n`                                             |

Example — Arm for IMEI `359586015829802`:
```
**,imei:359586015829802,111\r\n
```

### Cantrack wire format

Format: `*HQ,{IMEI},CMD,{HHMMSS},{KEYWORD}{PASSWORD}[EXTRA]#`

`{HHMMSS}` is UTC time at send time in `HHmmss` format (zero-padded). `{PASSWORD}` is the device password (default `654321` — see TD-1 in `technical-debt.md`).

| Command               | Wire frame                                          |
|-----------------------|-----------------------------------------------------|
| `Arm`                 | `*HQ,IMEI,CMD,HHMMSS,arm654321#`                   |
| `Disarm`              | `*HQ,IMEI,CMD,HHMMSS,disarm654321#`                |
| `CutMotor`            | `*HQ,IMEI,CMD,HHMMSS,stop654321#`                  |
| `RestoreMotor`        | `*HQ,IMEI,CMD,HHMMSS,resume654321#`                |
| `SetMovementAlarm`    | `*HQ,IMEI,CMD,HHMMSS,move654321 {radius}#`         |
| `CancelMovementAlarm` | `*HQ,IMEI,CMD,HHMMSS,nomove654321#`                |
| `CancelAlarm`         | `*HQ,IMEI,CMD,HHMMSS,KC654321 0#`                  |
| `Reset`               | `*HQ,IMEI,CMD,HHMMSS,reset654321#`                 |

Example — CutMotor for IMEI `111222333444555` at 14:05:09 UTC:
```
*HQ,111222333444555,CMD,140509,stop654321#
```

The `HHMMSS` value (`140509`) is stored in `device_commands.correlation_timestamp` and used to correlate the device's V4 acknowledgement frame.

### Coban ACK response codes

The device echoes the keyword number after executing the command. The `CobanProtocolParser` maps these to `MessageType.CommandAck`:

| Response code | Meaning                                    |
|---------------|--------------------------------------------|
| 102           | Generic OK (matches any pending command)   |
| 104           | CancelAlarm OK                             |
| 105           | SetMovementAlarm OK                        |
| 106           | CancelMovementAlarm OK                     |
| 107           | SetOverspeedAlarm OK                       |
| 109           | CutMotor OK                                |
| 110           | RestoreMotor OK                            |
| 111           | Arm OK                                     |
| 112           | Disarm OK                                  |
| 114           | SetGeofence OK                             |
| 115           | CancelGeofence OK                          |
| 150           | RequestSinglePosition OK (data follows)    |
| 509           | CutMotor deferred OK (relay was busy)      |
| 511           | Arm Failed (arm denied by device)          |
| 525           | SetGeofence Failed                         |
| 526           | CancelGeofence Failed                      |

Codes not in this list produce `MessageType.Unknown` — no exception, no status update, a warning is logged.

---

## Lifecycle State Machine

```
                         ┌───────────┐
    POST enqueue ───────►│  Queued   │
                         └─────┬─────┘
                               │ write loop sends bytes to socket
                               ▼
                         ┌───────────┐
                         │   Sent    │
                         └──┬────┬───┘
              ACK received  │    │  ACK received
              (success code)│    │  (failure code)
                            ▼    ▼
                     ┌──────────┐  ┌────────┐
                     │Acknowledged│  │ Failed │
                     └──────────┘  └────────┘

                         ┌───────────┐
                         │   Sent    │
                         └──────┬────┘
           sent_at_utc +        │ no ACK within AckTimeoutSeconds
           AckTimeoutSeconds    │
                                ▼
                         ┌─────────┐
                         │ Timeout │
                         └─────────┘

                         ┌───────────┐
                         │   Sent    │
                         └──────┬────┘
        API process restarts    │ sent_at_utc older than RestartGraceMinutes
        (startup recovery)      │
                                ▼
                    ┌────────────────────┐
                    │ InterruptedByRestart│
                    └────────────────────┘
```

**Terminal states**: `Acknowledged`, `Failed`, `Timeout`, `InterruptedByRestart`.

A command in a terminal state is immutable. Any attempt to update it is silently ignored and logged at Warning level.

**Reconnect recovery**: when a device reconnects (TCP session re-attached), all `Queued` commands for that IMEI are automatically re-enqueued into the new session's write channel. No manual intervention is required.

---

## Error Code Reference

All error bodies follow `{ "code": "snake_case_string", "message": "..." }`.

| Code                               | HTTP status | When it occurs                                                            |
|------------------------------------|-------------|---------------------------------------------------------------------------|
| `protocol_unknown`                 | 400         | No protocol resolvable for the IMEI (no session, no `user_devices.protocol`, no position history). |
| `command_not_supported_by_protocol`| 400         | The command is not in the capability matrix for the device's protocol.    |
| `confirmation_required`            | 400         | `CutMotor` or `Reset` sent without `"confirm": true` in the body.         |
| `invalid_parameters`               | 400         | A required command parameter is missing (e.g., `radius` for `SetMovementAlarm`). |
| `invalid_command_type`             | 400         | `commandType` value is not a recognized command name.                     |
| `invalid_page_size`                | 400         | `pageSize` query parameter is not one of `{10, 20, 50, 100}`.             |
| `invalid_status`                   | 400         | `status` query parameter is not a recognized lifecycle value.             |

---

## SignalR Contract

### Hub endpoint

```
/hubs/device-commands
```

Connect with a valid JWT:

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/device-commands", {
    accessTokenFactory: () => yourJwtToken
  })
  .build();

connection.on("CommandStatusChanged", (payload) => {
  console.log("Command status update:", payload);
});

await connection.start();
```

### Group membership

On connection, the hub automatically adds the client to group `user-{userId}`. Events are scoped to this group — clients only receive events for their own devices.

### `CommandStatusChanged` event payload

```json
{
  "commandId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "imei": "359586015829802",
  "commandType": "CutMotor",
  "status": "Acknowledged",
  "responseCode": "stop654321",
  "responseText": "stop engine succeed",
  "timestampUtc": "2026-05-01T14:30:02Z"
}
```

This event is emitted on every status transition:
- `Queued` → emitted at enqueue time (if device is online)
- `Sent` → emitted when bytes reach the socket
- `Acknowledged` / `Failed` → emitted when the device ACK is correlated
- `Timeout` → emitted by the background sweeper service
- `InterruptedByRestart` → emitted by the startup recovery service (best-effort; clients may not be connected yet)

---

## Rate Limiting

Rate limiting applies to the **POST endpoint only**. GET endpoints are not rate-limited.

| Property          | Value                                         |
|-------------------|-----------------------------------------------|
| Policy name       | `device-commands`                             |
| Window            | 60 seconds (fixed window)                     |
| Limit             | 10 requests per window (configurable)         |
| Key               | Authenticated user ID (not IP address)        |
| Exceeded response | `429 Too Many Requests` + `Retry-After` header |
| Storage           | In-memory (see TD-7 for scaling caveat)       |

---

## Configuration Keys

All keys live under the `DeviceCommands` section in `appsettings.json`.

| Key                            | Default | Description                                                           |
|--------------------------------|---------|-----------------------------------------------------------------------|
| `AckTimeoutSeconds`            | 60      | Seconds to wait for device ACK before marking `Timeout`.              |
| `TimeoutPollIntervalSeconds`   | 30      | How often the timeout background service polls.                       |
| `RestartGraceMinutes`          | 5       | Grace window for startup recovery — Sent rows newer than this are left for the timeout service to handle. |
| `RateLimitPerUserPerMinute`    | 10      | Max POST commands per user per 60-second window.                      |
| `CantrackDefaultPassword`      | 654321  | Password embedded in Cantrack command frames. See TD-1.               |
| `Enabled`                      | true    | Feature flag. Set to `false` for emergency rollback (POST returns 503). |
