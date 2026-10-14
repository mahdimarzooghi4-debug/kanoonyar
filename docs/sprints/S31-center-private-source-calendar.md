# Sprint 31 — Private center schedule (canonical class sessions + event plans)

Business references: B04 WF-02/04 and B05-A AC-EVT-01 **private planning only**.
This is an internal scheduling aid, not a public calendar, proof of delivery,
registration, an approved KPI, or a nationwide staff monitoring feed.

- GET `/api/v1/centers/{centerId}/schedule/private?fromUtc=<Z>&untilUtc=<Z>`,
  optionally `beforeAt`, `beforeKind`, `beforeSourceId` as one exclusive
  composite keyset cursor. UTC half-open time interval, 20 source rows/page.
- Reads directly from S12 owner `learning.class_sessions` (with explicit
  `scheduled_at`) and S29 latest immutable `events.center_event_plan_revisions`
  on S12 canonical event identity, never from genesis event timestamp.
  Undated sessions remain unknown and are omitted, not interpreted as absent
  or zero. Latest event revision may move an event between pages across
  separate requests; refresh the calendar to reconcile.
- Every row explicitly `PRIVATE_PLAN`, `isPublished=false`,
  `isExecuted=false`, `isAttendanceVerified=false`; event row has
  pinned actual local plan revision and latest event plan revision; class
  session has `null` for event-specific lineage. No joined person records.
- Signed OIDC subject, live same-center membership, independent
  `CENTER_PRIVATE_CALENDAR_READ` event grant + independent class source
  catalog read grant; both default DENY. Existing restricted read-only
  database role and FORCE RLS on both sources. One PostgreSQL REPEATABLE READ
  snapshot. No new tables, duplication, or unapproved write rights.
- Center UI selects date window, loads and pages genuine source rows, resets
  on center/identity change, denies forged published/executed/verified status.
- CI: handler negative tests, disposable restricted PostgreSQL test with
  source owner and revised event scheduling, tenant isolation and cursor,
  Chromium calendar window/denial/forgery.

Pending independent contracts: publication, cancellation lifecycle, event
capacity/consent, actual occurrence and attendance, cross-center staffing,
national staff access and derived statistics. No Ready for Review, Merge,
Stage, QA, Release or Production; PR remains Draft/Open.
