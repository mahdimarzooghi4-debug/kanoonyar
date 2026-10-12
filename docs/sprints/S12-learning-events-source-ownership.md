# Sprint 12 — Learning/Events Source Ownership and Source Catalog

## Implemented
- Independent center-owned Learning classes and scheduled sessions and Events
  records. Unknown schedule remains NULL; no invented attendance or delivery.
- Typed, immutable report-source bindings use exact center and canonical UUID
  with an atomic report/audit/outbox transaction. Unsupported, missing, or
  cross-center sources fail with no durable partial report/binding.
- GET /api/v1/centers/{centerId}/activity-sources/{sourceKind} supports the
  hard-coded kinds class-sessions/events, 20-row exclusive UUID keyset paging,
  class title lineage and nullable planned schedule.
- Endpoint requires signed subject, independent source-catalog operation grant,
  restricted SQL read login, scoped FORCE RLS, and live active same-center membership.
  The **actual** production authorization implementation always returns DENY.
- Frontend center workspace shows owner records with explicit 'planned only'
  semantics. Denied requests, malformed payload, center switch and source type
  changes do not retain protected stale content.
- Disposable PostgreSQL proves owner/source FK identity, cross-center isolation,
  rollback, RLS, 22+ report baseline. Restricted Npgsql handler test proves
  22 sessions -> 20+2 pages, one event, revoked/cross-center non-disclosure.
- Signed RSA test JWT and Chromium cases exercise route denial and UI behavior.

## No fabricated operations
- A scheduled class session is NOT an actual delivered session; an event record
  is NOT a performed event; the report with typed source stays UNVERIFIED.
- No source creation HTTP mutations, registration, attendance, participant
  records, evidence confirmation, approval or operational grants are enabled.
- No real Keycloak/Production credential trust, Stage, QA, release or deploy.
- All original stacked PRs remain Draft/Open/unmerged.