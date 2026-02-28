# Repository Guidelines (IMPITrack / ImpiTrack) — .NET 10 Only

## Project Structure & Module Organization
This repository contains one .NET **10** worker solution for GPS TCP ingestion (backend only).

- `ImpiTrack/ImpiTrack.sln`: solution file.
- `ImpiTrack/TcpServer/`: main project (**net10.0**).
- `ImpiTrack/TcpServer/Program.cs`: host bootstrap and service registration.
- `ImpiTrack/TcpServer/Worker.cs`: TCP listener, connection handling, and protocol responses.
- `ImpiTrack/TcpServer/appsettings*.json`: runtime configuration.
- `Documents/` (if present locally): protocol PDFs and architecture references.

**Where to put code**
- Place new production code under `ImpiTrack/TcpServer` unless a new project is intentionally added.
- Keep protocol-specific logic isolated (avoid spreading protocol strings across the codebase).

**Framework policy**
- **.NET 10 is required.**
- Do not introduce `net8.0` (or older) targets or compatibility layers.
- If `global.json` exists, it must pin a **.NET 10** SDK.

---

## Non-Negotiable TCP Rules (Production Guardrails)
When touching TCP ingestion, protocol parsing, framing, ACK handling, or persistence, follow these rules:

1. **Async IO only**  
   No thread-per-connection. No blocking reads/writes. Use async socket APIs end-to-end.

2. **Framing must be robust**  
   Your framing/decoder must handle:
   - **concatenated frames** (multiple messages in one read)
   - **partial frames** (message split across reads)
   - **noise** / malformed input
   - **MaxFrameBytes** enforced (hard cap) to prevent memory abuse

3. **Backpressure is required**  
   Downstream can be slower than incoming TCP. You must decouple:
   - IO loop (read/ack)
   - parsing
   - persistence  
   Use **bounded queues/channels** (or equivalent) and define behavior when full:
   - wait / drop / disconnect (must be explicit and configurable)

4. **ACK must be correct and fast**  
   Incorrect or late ACK causes device reconnection storms.
   - Send ACK as soon as a frame is validated/parsed (do **not** wait for DB unless protocol requires it).
   - Log every ACK decision with correlation fields.

5. **Input hardening is mandatory**
   - validate frame size, encoding, and basic structure
   - apply per-IP limits (connections and frames/minute)
   - track invalid frame counts; consider temporary ban windows (configurable)
   - always set socket timeouts (read/idle) to avoid zombie sessions

6. **Correlation and auditability**
   Every frame should be traceable via:
   - `SessionId` (per connection)
   - `PacketId` (per frame)
   - `Imei` (when available)
   - `RemoteIp`, `Port`, `Protocol`, `MessageType`

---

## Logging & Observability Requirements
Use structured logging with `ILogger` placeholders (no string concatenation).

### Required log fields (minimum)
For ingestion pipeline logs (receive/parse/ack/persist), include:
- `imei` (when available)
- `protocol`
- `port`
- `remoteIp`
- `sessionId`
- `packetId`
- `messageType` (login/heartbeat/gps/etc.)
- `latencyMs` (parse/persist/ack as applicable)
- `errorCode` / `exceptionType` (when failing)

### Operational signals (recommended)
- active connections per port
- frames/sec per protocol
- parse_ok / parse_fail ratio
- ack_sent count
- bounded queue backlog and drops
- persistence latency p95

---

## Build, Run, Publish (Developer Commands)
Run from repository root:

- `dotnet restore ImpiTrack/ImpiTrack.sln`  
- `dotnet build ImpiTrack/ImpiTrack.sln -c Debug`  
- `dotnet run --project ImpiTrack/TcpServer/TcpServer.csproj`  
- `dotnet publish ImpiTrack/TcpServer/TcpServer.csproj -c Release -o out`  

If a test project exists:
- `dotnet test`

**Build rule:** `dotnet build` must compile targeting **net10.0 only**.

---

## Coding Style & Naming Conventions
- Standard C# formatting: 4-space indentation, braces on new lines.
- Naming:
  - `PascalCase` for types/methods
  - `camelCase` for locals/parameters
  - `_camelCase` for private fields
- Prefer small, focused methods for:
  - framing/decoding
  - protocol parsing
  - ACK building/sending
  - validation and limits
- Keep protocol parsing deterministic and side-effect free where possible.

---

## Testing Guidelines
There is no test project yet (or it may be minimal). Add one as the codebase grows:

- Suggested path: `ImpiTrack/tests/TcpServer.Tests/`
- Framework: xUnit
- File naming: `<ClassName>Tests.cs`
- Test naming: `Method_ShouldOutcome_WhenCondition`

### Minimum coverage targets (once tests exist)
- framing:
  - concatenated frames
  - partial frames
  - max frame size rejection
- protocol parsing:
  - login parsing
  - heartbeat parsing
  - gps parsing
- ACK behavior:
  - correct ACK payload (`LOAD`, `ON`, echo, etc.)
  - ACK timing (should not depend on DB in normal cases)
- invalid payload handling and abuse limits

---

## Commit & Pull Request Guidelines
Use clear, scoped, imperative commit messages:

Examples:
- `tcp: add bounded channel backpressure`
- `tcp: harden framing for concatenated frames`
- `protocol-coban: implement login/heartbeat ACK`
- `ops: add structured log correlation fields`

PRs should include:
- purpose and behavior change summary
- build evidence (`dotnet build ...`)
- payload/log snippets for protocol-related changes
- test evidence if tests exist

Keep PRs small and focused on one logical change.

---

## Security & Configuration Tips
- Never commit secrets, IMEI allowlists, or environment-specific credentials.
- Keep local overrides in `appsettings.Development.json` or user secrets.
- Enforce:
  - max frame size
  - socket idle/read timeouts
  - per-IP rate limits
  - invalid frame thresholds (and optional ban windows)
- Avoid storing raw payloads in logs; log hashes/ids instead if payloads can contain sensitive data.

---

## Notes on Protocol Handling
- Protocol-specific strings and rules belong in protocol modules/sections within `TcpServer`.
- Do not leak protocol fields into generic TCP engine logic.
- If adding a new protocol, also add:
  - framing rules
  - parse tests with real/golden payloads
  - ACK rules and examples

---

## Documentation (XML) — Mandatory
- All new public APIs (classes, interfaces, methods, properties, records, enums) must include **XML documentation comments** (`/// <summary>...`).
- Add/maintain XML docs when modifying existing public members in touched files.
- Prefer actionable docs: purpose, parameters, return, exceptions, and protocol notes when relevant.
- Keep comments consistent and avoid restating obvious code.