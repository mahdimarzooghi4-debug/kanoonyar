# Sprint 30 — Private source-first event dossier

Contract: B04 WF-04/WF-05, B05 AC-X-01, KY-EVT-03 *source trace only*,
KY-LOC-03 and KY-EVL-03. S29 has the genuine versioned private event;
S12/S17 have canonical CENTER_EVENT owner source and original unverified reports.
No additional event/report persistence or invented business transition.

- GET /api/v1/centers/{centerId}/events/plans/{eventId}/source-dossier
  with optional exclusive beforeSequence. Returns the latest immutable *planned*
  S29 revision with the pinned same-center local-plan revision and up to 20
  original source-owned activity reports from S17, not copied report rows.
- A report is included only if its genuine S12 typed source binding is the
  same center/event, and its original S11 national issuance/local draft
  reference matches the **exact S29 pinned draft and revision**.
  Report itself has no event-plan-revision link; do not invent one.
  Older, unrelated, opaque or cross-plan reports are never silently
  promoted into this dossier. Empty reports mean *no matching record
  found*, never zero delivered events.
- Display original report/issuance identifiers, claimed time (possibly null),
  original evidence reference and `UNVERIFIED`. No attendee, consent,
  participant count, outcome, performance score, completion or publication.
  Prospective title/schedule come from latest S29 revision, not from the
  immutable S12 event genesis source metadata.
- Explicit independent EVENT_SOURCE_DOSSIER_READ and separate
  ICenterActivityReportReadAuthorization; both default DENY, no role-only
  grant. OIDC signed subject, active live membership and a restrictive
  read-only PostgreSQL credential with table/function privilege attestations
  and FORCE RLS; one REPEATABLE READ snapshot; 404 on foreign/missing event.
- Private center React dossier is openable only for a selected genuine
  event, validates exact identity, plan, revision and unverified flags,
  supports bounded source-sequence pagination and clears on center/token/
  event changes. No new mutation or schema.
- CI: endpoint boundary/security tests, genuine disposable PostgreSQL
  with 20+ original typed reports, cross-event, unbound/legacy and revocation
  exclusions, scoped pagination and Chromium denials/forgery/navigation.

Remaining independent Business contracts: public events and promotion,
attendance and consent, approved execution, adjudication, finance, national
staff aggregate access and indicator definitions. Keep PR Draft/Open,
do not merge/Ready/Stage/QA Gate/Release/Production without explicit instruction.
