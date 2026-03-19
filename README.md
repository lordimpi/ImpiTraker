# IMPITrack

Role: index  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-19

This README is the stable entry point for project documentation.

## Start Here

- Current backend/runtime truth: [`ImpiTrack/Docs/CURRENT_STATE.md`](ImpiTrack/Docs/CURRENT_STATE.md)
- Documentation map: [`ImpiTrack/Docs/README.md`](ImpiTrack/Docs/README.md)
- Active runbooks: [`ImpiTrack/Docs/runbooks/README.md`](ImpiTrack/Docs/runbooks/README.md)
- Architecture decisions: [`ImpiTrack/Docs/adr/README.md`](ImpiTrack/Docs/adr/README.md)
- Historical plans and PRDs: [`ImpiTrack/Docs/history/README.md`](ImpiTrack/Docs/history/README.md)

## Current Project Snapshot

- Backend repo only; frontend is out of scope for this repository.
- Runtime shape today: single unified process — `ImpiTrack.Api` hosts HTTP, SignalR, and TCP ingestion (via `TcpServer` class library). No separate TCP process.
- Current dev defaults in repo config: SQL Server for persistence and Identity, CORS origins configured for localhost, OpenAPI + Scalar + SignalR enabled in API Development.

## Architecture

### System Layers

```mermaid
graph TB
    subgraph Devices["GPS Devices"]
        D[GPS Device<br/>Coban / Cantrack]
    end

    subgraph Clients["WebSocket Clients"]
        C[Frontend / Mobile<br/>SignalR Client]
    end

    subgraph Host["ImpiTrack.Api — Unified Host"]

        subgraph API["HTTP + SignalR Layer"]
            AP[REST · OpenAPI · Scalar]
            HUB[TelemetryHub<br/>/hubs/telemetry]
            SRN[SignalRTelemetryNotifier]
            OWN[CachedDeviceOwnershipResolver]
        end

        subgraph TCP["TCP Layer — in-process"]
            TC[TcpServer<br/>class library]
            TCore[Tcp.Core<br/>framing · queue · pipeline]
            PC[Protocols.Coban]
            PCA[Protocols.Cantrack]
            PA[Protocols.Abstractions<br/>ParsedMessage contract]
        end

    end

    subgraph App["Application Layer"]
        AL[Application<br/>services · domain contracts<br/>ITelemetryNotifier · IDeviceOwnershipResolver]
        SH[Shared<br/>Options · HTTP DTOs]
    end

    subgraph Infra["Infrastructure Layer"]
        DA[DataAccess<br/>Dapper · SQL Server · PostgreSQL]
        AI[Auth.Infrastructure<br/>ASP.NET Identity · JWT]
        OBS[Observability<br/>OpenTelemetry]
        OPS[Ops<br/>health · diagnostics]
    end

    D -->|TCP| TC
    TC --> TCore
    TC --> PC
    TC --> PCA
    PC --> PA
    PCA --> PA
    TC --> DA
    TC --> SH
    TC -->|ITelemetryNotifier| SRN

    SRN --> OWN
    OWN --> DA
    SRN --> HUB
    HUB -->|push events| C

    AL --> SH
    AL --> PA

    DA --> AL
    DA --> TCore

    AI --> DA
    AI --> AL
    AI --> SH

    AP --> DA
    AP --> AL
    AP --> AI
    AP --> OPS
    AP --> SH

    TC --> OBS
    TC --> OPS
```

### Project Dependency Graph

Shows direct `<ProjectReference>` edges between projects (verified from `.csproj` files):

```mermaid
graph LR
    PA[Protocols.Abstractions]
    SH[Shared]
    AL[Application]
    DA[DataAccess]
    TC[Tcp.Core]
    PC[Protocols.Coban]
    PCA[Protocols.Cantrack]
    OBS[Observability]
    OPS[Ops]
    AI[Auth.Infrastructure]
    AP[Api]
    TS[TcpServer]
    TST[Tests]

    AL --> PA
    AL --> SH

    DA --> AL
    DA --> OPS
    DA --> TC
    DA --> PA
    DA --> SH

    AI --> DA
    AI --> SH
    AI --> AL

    AP --> OPS
    AP --> DA
    AP --> SH
    AP --> AL
    AP --> AI
    AP --> TS

    TS --> TC
    TS --> PA
    TS --> PC
    TS --> PCA
    TS --> OBS
    TS --> OPS
    TS --> DA
    TS --> SH

    TST --> TC
    TST --> PA
    TST --> PC
    TST --> PCA
    TST --> OPS
    TST --> DA
    TST --> AP
    TST --> TS
```

`Protocols.Abstractions` and `Shared` have no inward project dependencies. `Application` does not reference `DataAccess` — repository interfaces are defined in `Application.Abstractions/` and implemented by `DataAccess`, satisfying Dependency Inversion.

For the full architecture narrative, see [`ImpiTrack/Docs/CURRENT_STATE.md`](ImpiTrack/Docs/CURRENT_STATE.md).

## Documentation Rules

- Put current architecture, runtime, dependencies, limits, and governance updates in [`ImpiTrack/Docs/CURRENT_STATE.md`](ImpiTrack/Docs/CURRENT_STATE.md).
- Keep `README.md` short. If it starts becoming a wiki again, move the detail to the canonical docs and link it.
- Historical or superseded docs are not current truth. They must say so explicitly and point to the canonical replacement.
