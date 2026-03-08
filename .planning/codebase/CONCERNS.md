# CONCERNS

## P0/P1 risks to track

## 1) Identity on PostgreSQL deferred (P0 for full dual-provider parity)
- Current runtime explicitly blocks Identity provider `Postgres` in API startup.
- Business/ingestion data layer supports Postgres SQL paths, but auth parity is incomplete.
- Impact: production provider strategy must keep Identity on SQL Server or InMemory fallback patterns until stack is stabilized.

## 2) EMQX production hardening not fully codified in runtime config validation (P1)
- Local EMQX is validated (`UseTls=false`, port 1883).
- Production-ready profile (auth + ACL + TLS 8883) is documented, but enforcement is operational, not code-level policy.
- Impact: misconfiguration risk when moving from local to production.

## 3) Documentation encoding quality in root README (P1)
- Root README currently shows mojibake in multiple sections.
- Impact: onboarding friction and reduced documentation trust.

## 4) CI coverage is baseline, not full deployment governance (P1)
- CI currently covers restore/build/test and smoke dispatch.
- No full deployment workflow or environment promotion policy in-repo.
- Impact: release consistency depends on manual discipline.

## 5) Potential artifact noise in repository workflows (P2)
- Large local diagnostic logs and build artifacts exist in workspace context.
- `.gitignore` covers core local paths, but hygiene must remain strict to avoid accidental staging.

## Recommended near-term actions
1. Finalize decision for Identity provider strategy in production timeline.
2. Add explicit EMQX production config profile/template and rollout checklist.
3. Normalize README encoding to UTF-8 clean text.
4. Add release pipeline docs/check gates for deploy readiness.
