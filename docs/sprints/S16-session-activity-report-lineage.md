# Sprint 16 — Center Session Activity Report Lineage

## Scope and source ownership

An authenticated, independently authorized center operator may inspect the operational
**report dossier for one planned learning session**. The Learning module owns the
immutable session/class identity; Programs owns immutable activity reports, typed
source bindings, local draft links, pinned revision and national issuance. There is
no new duplicate report store and no copied participant/attendance records.

`GET /api/v1/centers/{centerId}/class-sessions/{sessionId}/report-lineage?beforeSequence=<positive long>`

Response provides exact `sessionId`, `classId`, `sessionTitle`, `classTitle`,
nullable `scheduledAt`, and at most 20 `activityReports`. Each report includes
`reportSequence`, `reportId`, `draftId`, `linkId`, `issuanceId`,
`programId`, `nationalVersion`, `pinnedRevision`, `reportedSummary`,
opaque `evidenceRef`, nullable `claimedOccurredAt`, `recordedAt`, and
literal `verificationStatus=UNVERIFIED`. `nextBeforeSequence` is an
exclusive keyset cursor only when more results exist. No counts or denominators.

Only immutable activity reports with an **exact typed source binding**
`CLASS_SESSION` and an existing same-center session and pinned national/local
plan link qualify. Legacy opaque reports, other sessions or other centers never
acquire inferred lineage. The DB's existing same-center FKs, FORCE RLS and
indexes remain authoritative. This slice is read-only; no migration or new
DB writing privilege is required.

## Access and negative invariants

- Valid OIDC subject is necessary but insufficient. New
  `ICenterSessionReportLineageAuthorization` is independent, revocable and
  configured exclusively as `UnapprovedCenterSessionReportLineageAuthorization`
  in the real application. Membership, JWT roles and PostgreSQL center GUC
  do not authorize the request.
- Only an independently authorized operation uses the restricted reader
  connection; every query is scoped by center and **live** active membership.
  Session not in scope returns non-disclosing 404; no cursor at 0 or negative.
- Existing separate occurrence and attendance history grants remain unchanged;
  this dossier does not query their tables, return participant pseudonyms, or
  infer participation. The center UI places the read-only dossier beside
  Sprint 15 history; it never merges grants.
- Scheduled is not delivered. Reported is not verified. Opaque evidence
  is not authenticated/approved. Unknown schedule or occurrence stays unknown.
  No KPI, outcome, attendance tally, badge or HQ claim is created.
- Frontend strictly validates response identities, typed fields, `UNVERIFIED`,
  pagination and UTC times and clears sensitive/stale results on denied access,
  malformed data and center/token/session switch.

## Test obligations

Unit tests prove missing subject, invalid session/cursor, independent default
deny, grant error fail-closed and unavailable datastore. PostgreSQL integration
using the real restricted read login constructs 22 typed reports plus one unbound
legacy report and proves 20+2 pages, exact issuance/draft lineage, missing/other
session, inactive/cross-center membership isolation, prohibited privileged DB
logins and no participant data. Chromium tests cover pagination, center switch,
independent denial and a forged VERIFIED payload. CI includes all normal gates.

## Release boundaries

Draft stacked PR only. No real operational grants, consent source, confirmed
attendance, verified evidence, Stage, QA gate, Merge, Release or Production.
