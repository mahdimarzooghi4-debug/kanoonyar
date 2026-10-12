# Sprint 12 — Learning / Events source-owned planning and report binding

## Technical scope
- KY-LMS-01/02 and KY-EVT-01 structural foundations: independent learning
  class + class session IDs, and independent event IDs, all center-scoped.
- Session/event planned schedule may be unknown (NULL); no invented dates,
  capacity, payment, enrollment, attendance or execution lifecycle.
- Initial records are immutable until a versioned scheduling contract is designed.
- Typed report-source binding strictly matches report's center, exact source_kind
  and canonical UUID source reference against an actual source-owner row.
- Existing opaque unverified reports remain untouched and are NOT retrospectively
  inferred to come from classes or events.
- New atomic sourced-report command calls original report/audit/outbox command,
  then inserts the exact source-owner binding in one transaction. A broken
  reference rolls back every write. All reports remain UNVERIFIED.
- PostgreSQL enforces composite FKs, source type/ref consistency, immutability
  and RLS; function/public table grants remain DENY.
- Test fixture uses real disposable PostgreSQL data for same-center, cross-center,
  missing source, unsupported kind, duplicate report, rollback and RLS.

## Explicitly not implemented
- Neither an event record nor a class session schedule proves delivery.
- No class attendance, enrollment, session-delivered event, verified outcomes,
  source completion, external evidence validation, real runtime source writer
  or publication/activation permission is provided.
- Operation grants and real authentication remain separate and default DENY.
- No Stage, QA Gate, Release, Production or merges under Draft PR rules.