# IMPITrack

Role: index  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-15

This README is the stable entry point for project documentation.

## Start Here

- Current backend/runtime truth: [`ImpiTrack/Docs/CURRENT_STATE.md`](ImpiTrack/Docs/CURRENT_STATE.md)
- Documentation map: [`ImpiTrack/Docs/README.md`](ImpiTrack/Docs/README.md)
- Active runbooks: [`ImpiTrack/Docs/runbooks/README.md`](ImpiTrack/Docs/runbooks/README.md)
- Architecture decisions: [`ImpiTrack/Docs/adr/README.md`](ImpiTrack/Docs/adr/README.md)
- Historical plans and PRDs: [`ImpiTrack/Docs/history/README.md`](ImpiTrack/Docs/history/README.md)

## Current Project Snapshot

- Backend repo only; frontend is out of scope for this repository.
- Runtime shape today: `TcpServer` + `ImpiTrack.Api` + shared data/auth/observability libraries.
- Current dev defaults in repo config: SQL Server for API and TCP persistence, SQL Server for Identity, EMQX enabled in TCP development settings, OpenAPI + Scalar enabled in API Development.

## Documentation Rules

- Put current architecture, runtime, dependencies, limits, and governance updates in [`ImpiTrack/Docs/CURRENT_STATE.md`](ImpiTrack/Docs/CURRENT_STATE.md).
- Keep `README.md` short. If it starts becoming a wiki again, move the detail to the canonical docs and link it.
- Historical or superseded docs are not current truth. They must say so explicitly and point to the canonical replacement.
