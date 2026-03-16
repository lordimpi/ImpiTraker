# IMPITrack Current State

Role: current-state  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-15

This document is the canonical source of truth for the current backend/runtime state of this repository.

Not canonical:
- `README.md` is only the entry index.
- historical plans and PRDs are context, not current truth.
- runbooks are procedures, not architecture truth.
- OpenAPI/Scalar is the contract source for HTTP endpoint detail.

## 1. What This Repo Is

IMPITrack is a backend-focused GPS telemetry platform repository. The current solution is organized around:

- `TcpServer`: TCP ingestion service for device traffic.
- `ImpiTrack.Api`: HTTP API for auth, admin, me, ops, health, and OpenAPI/Scalar exposure.
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

- services:
  - `ImpiTrack/TcpServer/TcpServer.csproj`
  - `ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj`
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

Operational shape today:

1. GPS devices talk to `TcpServer` over TCP.
2. TCP framing/parsing/ACK happens in the ingestion path.
3. raw packets and normalized telemetry are persisted through `ImpiTrack.DataAccess`.
4. `ImpiTrack.Api` reads the persisted state for auth, device ownership, health, and ops endpoints.
5. OpenAPI and Scalar document the HTTP contract during Development.

## 4. Runtime State Verified In Repo

### API

- Development OpenAPI is mapped with `app.MapOpenApi()`.
- Development Scalar UI is mapped at `/scalar/v1`.
- Scalar is configured in `ImpiTrack/ImpiTrack.Api/Program.cs`.

### Development defaults

From `ImpiTrack/ImpiTrack.Api/appsettings.Development.json`:

- `Database:Provider = SqlServer`
- `IdentityStorage:Provider = SqlServer`
- `Database:EnableAutoMigrate = true`
- `EventBus:Provider = InMemory`

From `ImpiTrack/TcpServer/appsettings.Development.json`:

- TCP listeners:
  - `5001` for `COBAN`
  - `5002` for `CANTRACK`
- `Database:Provider = SqlServer`
- `EventBus:Provider = Emqx`
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

## 6. Known Limits And Boundaries

- Frontend is out of repo scope.
- Identity on PostgreSQL is not the default active path; see [`ImpiTrack/Docs/adr/README.md`](adr/README.md) and ADR-001.
- Historical MVP plans and PRDs still exist for context, but they are not the current implementation contract.
- Endpoint-level HTTP truth belongs to OpenAPI/Scalar, not to markdown copies.
- This document should describe the current verified state only. Do not turn it into a speculative roadmap dump.

## 7. Where To Update What

- Update this file when runtime behavior, architecture shape, config defaults, operational dependencies, or active constraints change.
- Update runbooks when the procedure changes but the architecture truth does not.
- Update ADRs when a decision changes or a new architectural tradeoff is accepted.
- Update history docs only to preserve context or add explicit supersession notes.

## 8. Governance

- Canonical owner: `backend-maintainers` until a named owner is assigned.
- Review cadence: review this file in any PR that changes runtime behavior, API contract exposure, configuration defaults, operational setup, or deployment assumptions.
- Completion rule for impactful PRs: if a change affects runtime, API, config, or operations, the PR is not complete until canonical documentation is updated or explicitly confirmed as not needed.

## 9. Canonical Doc Map

- Repo index: [`../../README.md`](../../README.md)
- Docs index: [`README.md`](README.md)
- Active runbooks: [`runbooks/README.md`](runbooks/README.md)
- ADRs: [`adr/README.md`](adr/README.md)
- Historical archive: [`history/README.md`](history/README.md)

## 10. Verification Sources Used For This Snapshot

This current-state document was aligned against the repository structure and existing implementation-facing docs, including:

- `ImpiTrack/ImpiTrack.Api/Program.cs`
- `ImpiTrack/ImpiTrack.Api/appsettings.Development.json`
- `ImpiTrack/TcpServer/appsettings.Development.json`
- `ImpiTrack/Docs/BACKEND_MAINTENANCE_GUIDE.md` (superseded by this file)
- the active solution/project layout under `ImpiTrack/`
