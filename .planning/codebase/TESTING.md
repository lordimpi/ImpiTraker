# TESTING

## Test project
- Project: `ImpiTrack/ImpiTrack.Tests`
- Framework: xUnit
- Supporting packages:
  - `Microsoft.NET.Test.Sdk`
  - `Microsoft.AspNetCore.Mvc.Testing`
  - `coverlet.collector`

## Current test areas
- Framing and parsing:
  - `DelimiterFrameDecoderTests`
  - `CobanProtocolTests`
  - `CantrackProtocolTests`
- Queue/backpressure:
  - `QueueBackpressureTests`
  - `RawPacketQueueTests`
- Abuse and TCP behavior:
  - `AbuseGuardTests`
  - `TcpServerIntegrationTests`
- API/auth/account flows:
  - `ApiAuthFlowTests`
  - `ApiRegistrationAndAccountTests`
  - `ApiOpsAuthTests`

## Execution commands
- `dotnet test ImpiTrack/ImpiTrack.sln -c Debug`
- CI build/test job runs in `.github/workflows/backend-ci.yml`

## Smoke testing
- Provider smoke script: `ImpiTrack/Tools/Run-ProviderSmoke.ps1`
- MQTT smoke workflow: `.github/workflows/smoke-sql-emqx.yml`

## Observed gaps / opportunities
- No dedicated load/perf test suite committed for high-volume TCP sessions
- EMQX TLS/auth smoke is documented but not fully automated in CI
- Postgres + Identity end-to-end tests are intentionally deferred
