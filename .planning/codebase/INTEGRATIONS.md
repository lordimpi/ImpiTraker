# INTEGRATIONS

## Databases
- SQL Server (primary local/provider currently validated end-to-end)
- PostgreSQL (supported in ingestion/business data access layer; Identity still deferred)
- Migration approach:
  - SQL script runner (`SqlScriptMigrationRunner`)
  - Provider-specific scripts under `ImpiTrack.DataAccess/db/sqlserver` and `db/postgres`

## Message broker
- EMQX via MQTT (`MQTTnet`)
- Event bus providers:
  - `InMemory`
  - `Emqx`
- Published topics:
  - `v1/telemetry/{imei}`
  - `v1/status/{imei}`
  - `v1/dlq/{topic}`

## Authentication and identity
- ASP.NET Core Identity + JWT bearer tokens
- Roles include `Admin` and `User`
- Identity bootstrap service can seed admin at startup
- Current runtime guard: Identity `Postgres` provider disabled for .NET 10 stability concerns

## Email
- SMTP integration through auth infrastructure
- Email verification flow available (`/api/auth/verify-email` and `/api/auth/verify-email/confirm`)

## External tooling
- Docker for local EMQX
- Postman collection under `ImpiTrack/Postman`
- PowerShell tools under `ImpiTrack/Tools`:
  - `Send-TcpPayload.ps1`
  - `Run-ProviderSmoke.ps1`

## API docs and UX
- OpenAPI generation in API
- Scalar UI route in development: `/scalar/v1`
