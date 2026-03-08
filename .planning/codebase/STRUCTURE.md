# STRUCTURE

## Repository layout (major)
- `ImpiTrack/ImpiTrack.sln` - solution root
- `ImpiTrack/TcpServer` - TCP worker host and runtime composition
- `ImpiTrack/ImpiTrack.Api` - Web API host
- `ImpiTrack/ImpiTrack.Tcp.Core` - core TCP contracts/config/queue/security/event bus abstractions
- `ImpiTrack/ImpiTrack.Protocols.*` - protocol parsers and ACK strategies
- `ImpiTrack/ImpiTrack.DataAccess` - provider-aware data access + migrations + SQL scripts
- `ImpiTrack/ImpiTrack.Application` - account/admin application services
- `ImpiTrack/ImpiTrack.Auth.Infrastructure` - identity, token, email, auth services
- `ImpiTrack/ImpiTrack.Ops` - operational models/store contracts
- `ImpiTrack/ImpiTrack.Observability` - metrics contracts and implementation
- `ImpiTrack/ImpiTrack.Tests` - unit and integration tests
- `ImpiTrack/Docs` - backend guides and runbooks
- `ImpiTrack/Tools` - smoke and TCP helper scripts

## API endpoint organization
- `ImpiTrack.Api/Controllers`
  - `HealthController`
  - `MeController`
  - `AdminUsersController`
  - `OpsController`
- `ImpiTrack.Api/Auth/Controllers`
  - `AuthController`

## Data scripts organization
- `ImpiTrack.DataAccess/db/sqlserver/Vxxx__*.sql`
- `ImpiTrack.DataAccess/db/postgres/Vxxx__*.sql`

## CI/CD files
- `.github/workflows/backend-ci.yml`
- `.github/workflows/smoke-sql-emqx.yml`

## Documentation map
- `README.md` - technical overview and roadmap/debts
- `ImpiTrack/Docs/TCP_API_E2E_GUIDE.md` - E2E operations guide
- `ImpiTrack/Docs/MANUAL_E2E_EXECUTION_PLAN.md` - copy/paste manual execution runbook
