---
name: playbook-backend
description: Create or update AI_PLAYBOOK.md for IMPITrack backend execution. Use when a user asks for a backend playbook, development roadmap, phased delivery plan, Definition of Done, PR checklist, or TCP worker guardrails for .NET 10 GPS ingestion. Enforce backend-only scope (no frontend) and include robust framing, bounded channels/backpressure, correct and fast ACK behavior, limits/timeouts, SessionId/PacketId correlation, and structured logging.
---

# Playbook Backend

Create or maintain `AI_PLAYBOOK.md` at repository root as an execution guide for Backend + Worker TCP GPS.

## Workflow

1. Read current context:
- `AGENTS.md`
- `README.md`
- Existing `AI_PLAYBOOK.md` (if present)
- Relevant backend project files (solution, worker, config)

2. Generate or update `AI_PLAYBOOK.md` in repo root.
- Keep scope strict: Backend (.NET 10) + Worker TCP GPS only.
- Do not include frontend tasks, UX, or Angular work.
- Use concise, execution-oriented language.

3. Enforce required sections exactly:
- `Purpose & Scope`
- `Phase plan (0-4) with deliverables`
- `Definition of Done per phase`
- `PR checklist`
- `Operational guardrails summary`

4. Enforce non-negotiable TCP content:
- Robust framing (partial/multiple frames, no 1 read = 1 packet assumption)
- Bounded queues/channels and explicit backpressure strategy
- Correct and fast ACK by protocol and message type
- Timeouts and limits (`read`, `idle`, `handshake`, max frame/session bytes)
- Correlation strategy with `SessionId` and `PacketId`
- Structured logs with stable fields and severity levels

5. Keep the document action-oriented:
- Use concrete deliverables per phase (0 to 4).
- Make Definition of Done testable and binary.
- Include PR checks for build evidence, logs, limits/timeouts, and tests (if test projects exist).
- Prefer checklists and short bullets over narrative paragraphs.

## Output Contract

Write or update:
- `AI_PLAYBOOK.md` at repo root.

Quality bar:
- Developer-execution oriented.
- Concise but complete.
- No frontend content.
- .NET 10 backend only.

## Authoring Rules

- Use imperative, implementation-focused wording.
- Prefer ASCII output.
- Avoid generic architecture prose; prioritize actionable steps and acceptance criteria.
