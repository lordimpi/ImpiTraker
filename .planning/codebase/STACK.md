# STACK

## Runtime and language
- Primary language: C#
- Framework: .NET 10 (`net10.0`) across all projects
- Hosting models:
  - Worker host (`TcpServer`) for TCP ingestion
  - ASP.NET Core Web API (`ImpiTrack.Api`) for auth/account/ops endpoints

## Solution shape
- Solution: `ImpiTrack/ImpiTrack.sln`
- Projects: TCP core, protocol parsers (Coban/Cantrack), DataAccess, Ops, Application, Auth infrastructure, API, tests

## Core libraries in use
- ASP.NET Core + Identity + JWT
- Entity Framework Core (Identity context)
- Dapper for ingestion and ops data access
- SQL Server client (`Microsoft.Data.SqlClient`)
- PostgreSQL client (`Npgsql`) for dual-provider data layer
- MQTTnet for EMQX event bus publishing
- Scalar.AspNetCore for API docs UI

## Build and test
- Restore/build/test via `dotnet` CLI
- Test framework: xUnit + `Microsoft.NET.Test.Sdk`
- Integration tests use `Microsoft.AspNetCore.Mvc.Testing`

## Observability and operations
- Structured logging with `ILogger`
- Custom metrics project: `ImpiTrack.Observability`
- Ops endpoints in API: `/api/ops/*`
- CI workflows present in `.github/workflows`:
  - `backend-ci.yml`
  - `smoke-sql-emqx.yml`
