# Sprint 29 — Private, versioned center event planning

Business: KY-EVT-01, B05A AC-EVT-01 (planning aspect only), KY-NAT-04, KY-LOC-04,
B04 WF-04 and B05 AC-X-01. Public publication, registration and capacity allocation
need their independent permissions and contracts; **not enabled in this sprint**.

- Append immutable schedule revisions, with event title, stated purpose, textual venue
  and an explicit UTC scheduled instant; future schedule revisions never silently update
  old rows. No event occurrence, attendance, result, payment or winner is inferred.
- Reuse the existing S12 `events.center_events` as the canonical source-owned
  `CENTER_EVENT` report identity. New `events.center_event_plan_bindings` fixes one
  exact same-center local-plan draft revision and `center_event_plan_revisions`
  records all prospective changes. Existing source report bindings remain valid.
  Genesis title/schedule on `center_events` are immutable; current planned schedule
  is read **only from latest plan revision**, not silently from genesis.
- POST `/api/v1/centers/{centerId}/events/plans/revisions`; GET
  `/api/v1/centers/{centerId}/events/plans` (20 UUID keyset); GET
  `/api/v1/centers/{centerId}/events/plans/{eventId}/revisions` (20 revision keyset).
  All responses declare PRIVATE_DRAFT, no publication/execution/registration.
- Independently authorized EVENT_PLAN_APPEND/LIST_READ/HISTORY_READ.
  Default DENY; OIDC signed subject, live center membership on every request,
  restricted SQL writer/reader and RLS. No role or tenant GUC alone is permission.
  SQL SECURITY DEFINER owner NOLOGIN, atomic audit+outbox, advisory-locked
  expected-revision CAS and immutable same-center foreign keys.
- Private center UI: pick a real local plan, create and revise an event, browse
  private list/history; rejects forged execution/publication or wrong source plan.
  Cross-center/token switches clear protected state.
- CI: SQL role/tenant/immutability tests, handler negative auth/validation, genuine
  disposable PostgreSQL restricted user + concurrency/rollbacks, Chromium UI.
  Test grants are synthetic, never Production authorization.

**Blocked by future contracts:** public events, publication moderation, attendee
consent/capacity, legal guardians, payments, event/contest completion and score.
No Stage, QA gate, Release, Production, Ready for Review or merge. Keep Draft.
