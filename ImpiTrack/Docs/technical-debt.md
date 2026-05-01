Role: technical-debt  
Status: active  
Owner: backend-maintainers  
Last Updated: 2026-05-01 (gps-device-commands)

# Technical Debt Registry

This file tracks known limitations, deferred work, and intentional shortcuts. Each entry records what does not work today, who is affected, what would trigger a revisit, and how much effort it would take.

Items here are NOT bugs. They are accepted trade-offs recorded so nothing is lost between sessions or contributors.

---

## TD-1: Custom protocol passwords

**Limitation**: The Cantrack command serializer hardcodes password `654321` in every outbound frame (`*HQ,IMEI,CMD,HHMMSS,{cmd}654321#`). The Coban serializer has no password field at all. Devices whose factory-default password has been changed will silently ignore all commands — the device receives the frame but rejects it without any error feedback to the server.

**Impact**: Any user who has changed their device password cannot issue commands. Failures are silent (command reaches `Timeout` after 60s with no indication that the password was the cause). Fleet operators with non-default passwords are completely blocked.

**Trigger to revisit**: First support ticket confirming command failure due to password mismatch, OR when multi-tenant fleet management requires per-device credential management.

**Effort estimate**: M (≤1 week)

**Impacted files**:
- `ImpiTrack.Protocols.Cantrack/CantrackCommandSerializer.cs` — inject resolved password instead of const `654321`
- `ImpiTrack.Protocols.Coban/CobanCommandSerializer.cs` — add password field support
- `ImpiTrack.DataAccess/db/postgres/V010__user_devices_password.sql` — new migration adding `device_password` column to `user_devices`
- `ImpiTrack.DataAccess/db/sqlserver/V010__user_devices_password.sql` — same
- `ImpiTrack.DataAccess/Repositories/UserDeviceRepository.cs` — expose password in device lookup
- `ImpiTrack.Application/Services/DeviceCommandService.cs` — pass resolved password to serializer
- Frontend: UI field for users to enter their device password

**Operations note**: Until this is addressed, commands require devices to use the factory-default password `654321`. This must be communicated to all users when the feature is released.

---

## TD-2: SMS fallback

**Limitation**: Commands are delivered over GPRS only. When a device is in an area without GPRS coverage, or when GPRS is manually disabled on the SIM, commands remain in `Queued` status indefinitely and eventually time out after 60s once they are sent. There is no alternative delivery channel.

**Impact**: Fleet operators in areas with spotty GPRS coverage (tunnels, underground parking, rural zones) cannot reliably command their devices. The `Timeout` status gives no indication that the failure was connectivity-related.

**Trigger to revisit**: Product requirement for guaranteed delivery SLA, or fleet operator feedback that GPRS outages make the command system unreliable in a specific geographic area.

**Effort estimate**: L (≥2 weeks)

**Impacted files**:
- New: `ImpiTrack.Application/Abstractions/ISmsFallbackService.cs`
- New: `ImpiTrack.Infrastructure/Sms/TwilioSmsFallbackService.cs` (or Infobip equivalent)
- `ImpiTrack.Application/Services/DeviceCommandService.cs` — fallback delivery strategy after Timeout
- `ImpiTrack.DataAccess` — SIM number or gateway identifier per device in `user_devices`
- `appsettings.json` — SMS provider credentials and gateway config
- Both protocol serializers — SMS command format differs from GPRS format for some commands

---

## TD-3: BoxTracker protocol

**Limitation**: A third GPS device protocol (`BoxTracker`) is documented in `Documents/Protocolo Box tracker.pdf`. No inbound parser, no outbound command serializer, no TCP protocol detection, and no capability matrix exists for BoxTracker. Devices using BoxTracker cannot send telemetry that the server parses, and cannot receive commands.

**Impact**: Any user or fleet that deploys BoxTracker hardware cannot use the platform at all. This is a full feature gap, not a partial one.

**Trigger to revisit**: First BoxTracker device registered in the system, or when a fleet migrates from Coban/Cantrack to BoxTracker hardware.

**Effort estimate**: L (≥2 weeks)

**Impacted files**:
- New: `ImpiTrack.Protocols.BoxTracker/BoxTrackerProtocolParser.cs`
- New: `ImpiTrack.Protocols.BoxTracker/BoxTrackerProtocolCommandSerializer.cs`
- New: `ImpiTrack.Protocols.BoxTracker/BoxTrackerProtocolCommandSerializer.cs`
- `ImpiTrack.Protocols.Abstractions/ProtocolId.cs` — add `BoxTracker` enum value
- `TcpServer/Worker.cs` — protocol detection for BoxTracker session identification
- `ImpiTrack.Application/Services/DeviceCommandService.cs` — capability matrix entries for BoxTracker
- New integration test suite: `ImpiTrack.Tests/Integration/DeviceCommands/BoxTrackerCommandFlowTests.cs`

---

## TD-4: Automatic retries

**Limitation**: v1 has no automatic retry mechanism for `Timeout` or `Failed` commands. Users must manually re-issue the command from the UI. This is intentional to avoid duplicating dangerous commands (e.g., a second `CutMotor` when the first timed out but actually executed on the device).

**Impact**: Operators dealing with intermittently connected devices must manually monitor command status and re-issue commands. High friction for non-dangerous commands like `RequestSinglePosition`.

**Trigger to revisit**: Product decision to allow controlled retries for non-dangerous command types, or user feedback that manual retry is too cumbersome at scale.

**Effort estimate**: M (≤1 week)

**Impacted files**:
- `ImpiTrack.Application/Services/DeviceCommandService.cs` — idempotency key per command instance, per-command-type retry policy
- `ImpiTrack.Application/Abstractions/IDeviceCommandService.cs` — `RetryAsync` or retry-policy configuration interface
- `ImpiTrack.DataAccess` — idempotency key column in `device_commands` (new migration)
- `TcpServer/BackgroundServices/DeviceCommandTimeoutService.cs` — trigger retry instead of terminal Timeout (for allowed types)
- `appsettings.json` — `DeviceCommands:RetryPolicy` config section

**Constraint**: Dangerous commands (`CutMotor`, `Reset`) MUST be explicitly excluded from any auto-retry policy, regardless of configuration.

---

## TD-5: Scheduled commands

**Limitation**: Commands can only be issued immediately. There is no way to schedule a command for a future execution time (e.g., "arm device at 22:00 every weekday").

**Impact**: Fleet operators who want automated business-hours enforcement or geo-fence schedule management must implement scheduling on the client side. No server-side automation is possible.

**Trigger to revisit**: Product requirement for scheduled automation, or fleet management product tier that includes time-based policies.

**Effort estimate**: L (≥2 weeks)

**Impacted files**:
- New: `ImpiTrack.DataAccess/db/postgres/V011__scheduled_commands.sql`
- New: `ImpiTrack.DataAccess/db/sqlserver/V011__scheduled_commands.sql`
- New: `ImpiTrack.Application/Services/ScheduledCommandService.cs`
- New: `TcpServer/BackgroundServices/ScheduledCommandDispatchService.cs` (or Hangfire/Quartz integration)
- `ImpiTrack.Api/Controllers/` — new `MeScheduledCommandsController.cs` endpoint
- `ImpiTrack.Application/Abstractions/IDeviceCommandService.cs` — no changes needed (reuses `EnqueueAsync` as execution target)

---

## TD-6: Broadcast / batch commands

**Limitation**: The API targets a single IMEI per request. A fleet operator who needs to send the same command to 50 devices must make 50 individual API calls. The rate limiter (10/min per user) would block this entirely for large fleets.

**Impact**: Fleet operators managing more than 10 devices cannot efficiently issue fleet-wide commands. Manual batching via the client is the only workaround.

**Trigger to revisit**: Fleet management product tier, or any operator actively managing more than 10 devices.

**Effort estimate**: L (≥2 weeks)

**Impacted files**:
- New: `ImpiTrack.DataAccess/db/postgres/V012__device_command_batches.sql`
- New: `ImpiTrack.DataAccess/db/sqlserver/V012__device_command_batches.sql`
- New: `ImpiTrack.Application/Services/BatchCommandService.cs` — fan-out, partial-failure handling
- New: `ImpiTrack.Api/Controllers/MeDeviceCommandBatchesController.cs` — `POST /api/me/command-batches`
- `ImpiTrack.Api/Program.cs` — rate-limit policy recalculation for batch (bypass or separate higher limit)
- Frontend: batch status UX (some devices may fail while others succeed)

---

## TD-7: Distributed rate limiting

**Limitation**: The `command-per-user` rate limiter (`AddRateLimiter`) uses in-memory storage. Each API instance maintains an independent counter. Under horizontal scaling (multiple replicas), a user can issue N×10 commands per minute by having requests round-robin across N instances.

**Impact**: Rate limiting becomes ineffective as soon as the API is deployed with more than one replica (e.g., Kubernetes with `replicas > 1`, active-active multi-region).

**Trigger to revisit**: Any deployment configuration where the API runs with more than one replica behind a load balancer.

**Effort estimate**: S (≤2 days)

**Impacted files**:
- `ImpiTrack.Api/Program.cs` — replace `AddFixedWindowLimiter` with Redis-backed `IRateLimitStore`
- `docker-compose.prod.yml` — add Redis service dependency if not already present
- `appsettings.json` — Redis connection string under `RateLimit:Redis`

**Note**: This requires a Redis instance. If Redis is already in the infrastructure for other purposes (session cache, etc.), the marginal cost is near zero.

---

## TD-8: `correlation_timestamp` column width

**Limitation**: The `device_commands.correlation_timestamp` column is defined as `VARCHAR(8)` / `NVARCHAR(8)` in the V009 migration. The spec declares `VARCHAR(16)`. Current Cantrack `HHmmss` timestamps are 6 characters, so there is no functional truncation risk.

**Impact**: Zero functional impact today. The spec divergence exists as a paper discrepancy. If a future protocol uses longer correlation timestamps (more than 8 characters), a migration would be required.

**Trigger to revisit**: Any protocol whose correlation identifier exceeds 8 characters is introduced. At that point, migrate with `ALTER TABLE device_commands ALTER COLUMN correlation_timestamp VARCHAR(16)`.

**Effort estimate**: S (≤2 days) — trivial schema change + migration version bump.

**Impacted files**:
- `ImpiTrack.DataAccess/db/postgres/V009__device_commands.sql` — column type change
- `ImpiTrack.DataAccess/db/sqlserver/V009__device_commands.sql` — column type change
- No application code changes needed (the column is stored and retrieved as a string; no length enforcement in C#)
